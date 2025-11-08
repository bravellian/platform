// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace Bravellian.Platform.Tests;

using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

/// <summary>
/// Concurrency and stress tests for the distributed semaphore service.
/// </summary>
public class SemaphoreConcurrencyTests : SqlServerTestBase
{
    private ISemaphoreService? semaphoreService;

    public SemaphoreConcurrencyTests(ITestOutputHelper testOutputHelper)
        : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure semaphore schema exists
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(this.ConnectionString, "dbo").ConfigureAwait(false);

        // Create service
        var options = Options.Create(new SemaphoreOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            MinTtlSeconds = 1,
            MaxTtlSeconds = 3600,
            DefaultTtlSeconds = 30,
            MaxLimit = 10000,
            ReaperCadenceSeconds = 30,
            ReaperBatchSize = 1000,
        });

        this.semaphoreService = new SqlSemaphoreService(
            options,
            NullLogger<SqlSemaphoreService>.Instance);
    }

    [Fact]
    public async Task ParallelAcquires_NeverExceedsLimit()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 10;
        var workerCount = 50;

        await this.semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Track successful acquisitions
        var successfulTokens = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var tasks = new List<Task>();

        // Act - Launch many parallel workers trying to acquire
        for (int i = 0; i < workerCount; i++)
        {
            var ownerId = $"owner{i}";
            tasks.Add(Task.Run(async () =>
            {
                var result = await this.semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: ownerId,
                    cancellationToken: TestContext.Current.CancellationToken);

                if (result.Status == SemaphoreAcquireStatus.Acquired)
                {
                    successfulTokens.Add(result.Token!.Value);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        var acquired = successfulTokens.Count;
        this.TestOutputHelper.WriteLine($"Acquired {acquired} out of {workerCount} attempts with limit {limit}");
        
        acquired.ShouldBeLessThanOrEqualTo(limit);
        acquired.ShouldBeGreaterThan(0); // At least some should succeed
    }

    [Fact]
    public async Task ParallelAcquireAndRelease_MaintainsInvariant()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 5;
        var iterations = 20;

        await this.semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        var successfulAcquires = 0;
        var successfulReleases = 0;

        // Act - Each worker tries to acquire, hold briefly, and release
        var tasks = new List<Task>();
        for (int i = 0; i < iterations; i++)
        {
            var ownerId = $"owner{i}";
            tasks.Add(Task.Run(async () =>
            {
                var result = await this.semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: ownerId,
                    cancellationToken: TestContext.Current.CancellationToken);

                if (result.Status == SemaphoreAcquireStatus.Acquired)
                {
                    Interlocked.Increment(ref successfulAcquires);

                    // Hold briefly
                    await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

                    // Release
                    var releaseResult = await this.semaphoreService.ReleaseAsync(
                        name,
                        result.Token!.Value,
                        TestContext.Current.CancellationToken);

                    if (releaseResult.Status == SemaphoreReleaseStatus.Released)
                    {
                        Interlocked.Increment(ref successfulReleases);
                    }
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - All successful acquires should be released
        this.TestOutputHelper.WriteLine($"Successful acquires: {successfulAcquires}, releases: {successfulReleases}");
        successfulReleases.ShouldBe(successfulAcquires);
    }

    [Fact]
    public async Task HighContentionAcquire_EventuallySucceeds()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 2; // Two slots to allow more throughput
        var workerCount = 10;

        await this.semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Act - Try to acquire with retries
        var tasks = new List<Task<bool>>();
        for (int i = 0; i < workerCount; i++)
        {
            var ownerId = $"owner{i}";
            tasks.Add(Task.Run(async () =>
            {
                // Retry up to 15 times with small delays
                for (int attempt = 0; attempt < 15; attempt++)
                {
                    var result = await this.semaphoreService.TryAcquireAsync(
                        name,
                        ttlSeconds: 1, // Short TTL so others can acquire
                        ownerId: ownerId,
                        cancellationToken: TestContext.Current.CancellationToken);

                    if (result.Status == SemaphoreAcquireStatus.Acquired)
                    {
                        return true;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
                }

                return false;
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - With 2 slots and retries, most workers should eventually succeed
        var successCount = results.Count(r => r);
        this.TestOutputHelper.WriteLine($"{successCount} out of {workerCount} workers eventually acquired the semaphore");
        successCount.ShouldBeGreaterThan(workerCount / 2); // At least half should succeed
    }

    [Fact]
    public async Task ConcurrentUpdateLimit_DoesNotCorruptState()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await this.semaphoreService!.EnsureExistsAsync(name, 5, TestContext.Current.CancellationToken);

        // Act - Multiple concurrent limit updates
        var tasks = new List<Task>();
        var newLimits = new[] { 10, 7, 15, 3, 20 };

        foreach (var newLimit in newLimits)
        {
            tasks.Add(Task.Run(async () =>
            {
                await this.semaphoreService.UpdateLimitAsync(
                    name,
                    newLimit,
                    ensureIfMissing: false,
                    cancellationToken: TestContext.Current.CancellationToken);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - Should be able to acquire at least once (any limit > 0 should work)
        var result = await this.semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        result.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
    }

    [Fact]
    public async Task ConcurrentReapAndAcquire_MaintainsCorrectness()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 5;
        await this.semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Create some expired leases
        for (int i = 0; i < 3; i++)
        {
            await this.semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 1,
                ownerId: $"expired-owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Concurrent reaping and acquiring
        var reapTask = Task.Run(async () =>
        {
            return await this.semaphoreService.ReapExpiredAsync(
                name,
                maxRows: 100,
                cancellationToken: TestContext.Current.CancellationToken);
        });

        var acquireTasks = new List<Task<SemaphoreAcquireResult>>();
        for (int i = 0; i < limit; i++)
        {
            acquireTasks.Add(Task.Run(async () =>
            {
                return await this.semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: $"new-owner{i}",
                    cancellationToken: TestContext.Current.CancellationToken);
            }));
        }

        var deletedCount = await reapTask;
        var acquireResults = await Task.WhenAll(acquireTasks);

        // Assert
        // Note: The acquire operations also opportunistically reap top 10 expired leases,
        // so the explicit reap call might find 0-3 expired leases depending on timing
        deletedCount.ShouldBeLessThanOrEqualTo(3); // At most 3 (all expired)
        var acquired = acquireResults.Count(r => r.Status == SemaphoreAcquireStatus.Acquired);
        acquired.ShouldBeLessThanOrEqualTo(limit); // Never exceed limit
    }

    [Fact]
    public async Task FencingCounters_StrictlyIncreasingUnderConcurrency()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 20;
        await this.semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Act - Concurrent acquires
        var tasks = new List<Task<long?>>();
        for (int i = 0; i < limit; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var result = await this.semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: $"owner{i}",
                    cancellationToken: TestContext.Current.CancellationToken);

                return result.Fencing;
            }));
        }

        var fencingCounters = (await Task.WhenAll(tasks))
            .Where(f => f.HasValue)
            .Select(f => f!.Value)
            .OrderBy(f => f)
            .ToList();

        // Assert - All fencing counters should be unique and increasing
        this.TestOutputHelper.WriteLine($"Fencing counters: {string.Join(", ", fencingCounters)}");
        fencingCounters.Count.ShouldBe(fencingCounters.Distinct().Count()); // All unique
        
        // Check strict ordering
        for (int i = 1; i < fencingCounters.Count; i++)
        {
            fencingCounters[i].ShouldBeGreaterThan(fencingCounters[i - 1]);
        }
    }
}
