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


using System.Collections.Concurrent;
using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Concurrency and stress tests for the distributed semaphore service.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SemaphoreConcurrencyTests : SqlServerTestBase
{
    private ISemaphoreService? semaphoreService;

    public SemaphoreConcurrencyTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure semaphore schema exists
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(ConnectionString, "dbo").ConfigureAwait(false);

        // Create service
        var options = Options.Create(new SemaphoreOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "dbo",
            MinTtlSeconds = 1,
            MaxTtlSeconds = 3600,
            DefaultTtlSeconds = 30,
            MaxLimit = 10000,
            ReaperCadenceSeconds = 30,
            ReaperBatchSize = 1000,
        });

        semaphoreService = new SqlSemaphoreService(
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

        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Track successful acquisitions
        var successfulTokens = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        var tasks = new List<Task>();

        // Act - Launch many parallel workers trying to acquire
        for (int i = 0; i < workerCount; i++)
        {
            var ownerId = $"owner{i}";
            tasks.Add(Task.Run(async () =>
            {
                var result = await semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: ownerId,
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

                if (result.Status == SemaphoreAcquireStatus.Acquired)
                {
                    successfulTokens.Add(result.Token!.Value);
                }
            }, Xunit.TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // Assert
        var acquired = successfulTokens.Count;
        TestOutputHelper.WriteLine($"Acquired {acquired} out of {workerCount} attempts with limit {limit}");

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

        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        var successfulAcquires = 0;
        var successfulReleases = 0;

        // Act - Each worker tries to acquire, hold briefly, and release
        var tasks = new List<Task>();
        for (int i = 0; i < iterations; i++)
        {
            var ownerId = $"owner{i}";
            tasks.Add(Task.Run(async () =>
            {
                var result = await semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: ownerId,
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

                if (result.Status == SemaphoreAcquireStatus.Acquired)
                {
                    Interlocked.Increment(ref successfulAcquires);

                    // Hold briefly
                    await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken).ConfigureAwait(false);

                    // Release
                    var releaseResult = await semaphoreService.ReleaseAsync(
                        name,
                        result.Token!.Value,
                        TestContext.Current.CancellationToken).ConfigureAwait(false);

                    if (releaseResult.Status == SemaphoreReleaseStatus.Released)
                    {
                        Interlocked.Increment(ref successfulReleases);
                    }
                }
            }, Xunit.TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // Assert - All successful acquires should be released
        TestOutputHelper.WriteLine($"Successful acquires: {successfulAcquires}, releases: {successfulReleases}");
        successfulReleases.ShouldBe(successfulAcquires);
    }

    [Fact]
    public async Task HighContentionAcquire_EventuallySucceeds()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 2; // Two slots to allow more throughput
        var workerCount = 10;

        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

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
                    var result = await semaphoreService.TryAcquireAsync(
                        name,
                        ttlSeconds: 1, // Short TTL so others can acquire
                        ownerId: ownerId,
                        cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

                    if (result.Status == SemaphoreAcquireStatus.Acquired)
                    {
                        return true;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken).ConfigureAwait(false);
                }

                return false;
            }));
        }

        var results = await Task.WhenAll(tasks);

        // Assert - With 2 slots and retries, most workers should eventually succeed
        var successCount = results.Count(r => r);
        TestOutputHelper.WriteLine($"{successCount} out of {workerCount} workers eventually acquired the semaphore");
        successCount.ShouldBeGreaterThan(workerCount / 2); // At least half should succeed
    }

    [Fact]
    public async Task ConcurrentUpdateLimit_DoesNotCorruptState()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 5, TestContext.Current.CancellationToken);

        // Act - Multiple concurrent limit updates
        var tasks = new List<Task>();
        var newLimits = new[] { 10, 7, 15, 3, 20 };

        foreach (var newLimit in newLimits)
        {
            tasks.Add(Task.Run(async () =>
            {
                await semaphoreService.UpdateLimitAsync(
                    name,
                    newLimit,
                    ensureIfMissing: false,
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
            }, Xunit.TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        // Assert - Should be able to acquire at least once (any limit > 0 should work)
        var result = await semaphoreService.TryAcquireAsync(
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
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Create some expired leases
        for (int i = 0; i < 3; i++)
        {
            await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 1,
                ownerId: $"expired-owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
        }

        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Concurrent reaping and acquiring
        var reapTask = Task.Run(async () =>
        {
            return await semaphoreService.ReapExpiredAsync(
                name,
                maxRows: 100,
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });

        var acquireTasks = new List<Task<SemaphoreAcquireResult>>();
        for (int i = 0; i < limit; i++)
        {
            acquireTasks.Add(Task.Run(async () =>
            {
                return await semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: $"new-owner{i}",
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
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
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Act - Concurrent acquires
        var tasks = new List<Task<long?>>();
        for (int i = 0; i < limit; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var result = await semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 30,
                    ownerId: $"owner{i}",
                    cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);

                return result.Fencing;
            }));
        }

        var fencingCounters = (await Task.WhenAll(tasks))
            .Where(f => f.HasValue)
            .Select(f => f!.Value)
            .OrderBy(f => f)
            .ToList();

        // Assert - All fencing counters should be unique and increasing
        TestOutputHelper.WriteLine($"Fencing counters: {string.Join(", ", fencingCounters)}");
        fencingCounters.Count.ShouldBe(fencingCounters.Distinct().Count()); // All unique

        // Check strict ordering
        for (int i = 1; i < fencingCounters.Count; i++)
        {
            fencingCounters[i].ShouldBeGreaterThan(fencingCounters[i - 1]);
        }
    }

    [Fact]
    public async Task Renewals_WithIntermittentGcPausesMaintainLeases()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 3;
        var ttlSeconds = 8;
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        var acquisitions = new List<SemaphoreAcquireResult>();
        for (var i = 0; i < limit; i++)
        {
            var acquired = await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: ttlSeconds,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);

            acquired.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
            acquisitions.Add(acquired);
        }

        var random = new Random(42);
        var renewResults = new ConcurrentBag<SemaphoreRenewResult>();

        // Act - Renew each lease several times with jitter and occasional long pauses to mimic GC
        var renewalTasks = acquisitions.Select(acquired => Task.Run(async () =>
        {
            for (var iteration = 0; iteration < 6; iteration++)
            {
                // Short jitter between renewals
                await Task.Delay(TimeSpan.FromMilliseconds(random.Next(50, 250)), TestContext.Current.CancellationToken);

                // Simulate a rare GC pause
                if (iteration == 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(random.Next(1500, 2000)), TestContext.Current.CancellationToken);
                }

                var renewed = await semaphoreService.RenewAsync(
                    name,
                    acquired.Token!.Value,
                    ttlSeconds: ttlSeconds,
                    cancellationToken: TestContext.Current.CancellationToken);

                renewResults.Add(renewed);
                renewed.Status.ShouldBe(SemaphoreRenewStatus.Renewed);
            }
        }, Xunit.TestContext.Current.CancellationToken)).ToList();

        await Task.WhenAll(renewalTasks);

        // Assert - Every renewal succeeded despite jitter and pauses
        renewResults.ShouldNotBeEmpty();
        renewResults.ShouldAllBe(r => r.Status == SemaphoreRenewStatus.Renewed);

        // Cleanup
        foreach (var acquired in acquisitions)
        {
            await semaphoreService.ReleaseAsync(name, acquired.Token!.Value, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task StarvedAcquisition_AppliesBackpressureUntilCapacityReturns()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        var holder = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 5,
            ownerId: "holder",
            cancellationToken: TestContext.Current.CancellationToken);

        holder.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var notAcquiredCount = 0;

        // Act - Saturate the semaphore and verify contenders are backpressured, not lost
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var attempt = await semaphoreService.TryAcquireAsync(
                    name,
                    ttlSeconds: 1,
                    ownerId: Guid.NewGuid().ToString(),
                    cancellationToken: TestContext.Current.CancellationToken);

                if (attempt.Status == SemaphoreAcquireStatus.NotAcquired)
                {
                    Interlocked.Increment(ref notAcquiredCount);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            }
        }, Xunit.TestContext.Current.CancellationToken)));

        // Release capacity and confirm acquisition immediately resumes
        await semaphoreService.ReleaseAsync(name, holder.Token!.Value, TestContext.Current.CancellationToken);

        var recovered = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 5,
            ownerId: "recovered",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        notAcquiredCount.ShouldBeGreaterThan(0); // Prolonged backpressure observed
        recovered.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
    }
}
