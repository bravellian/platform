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


using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Tests for the distributed semaphore service.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SemaphoreServiceTests : SqlServerTestBase
{
    private ISemaphoreService? semaphoreService;

    public SemaphoreServiceTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure semaphore schema exists
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        // Create service
        var options = Options.Create(new SemaphoreOptions
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
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

    #region Basic Correctness Tests

    /// <summary>When acquiring a semaphore that has not been created, then status is NotAcquired.</summary>
    /// <intent>Confirm missing semaphores are not implicitly created.</intent>
    /// <scenario>Use a new semaphore name without calling EnsureExistsAsync.</scenario>
    /// <behavior>TryAcquireAsync returns NotAcquired.</behavior>
    [Fact]
    public async Task TryAcquire_WithNonExistentSemaphore_ReturnsNotAcquired()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";

        // Act
        var result = await semaphoreService!.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(SemaphoreAcquireStatus.NotAcquired);
    }

    /// <summary>When acquiring up to the configured limit, then each acquisition succeeds with unique tokens.</summary>
    /// <intent>Verify limit enforcement allows acquisitions up to capacity.</intent>
    /// <scenario>Ensure a semaphore with limit 3 and acquire three times with different owners.</scenario>
    /// <behavior>All results are Acquired with unique tokens and fencing values.</behavior>
    [Fact]
    public async Task TryAcquire_UpToLimit_AllAcquiresSucceed()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 3;
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Act - Try to acquire up to the limit
        var results = new List<SemaphoreAcquireResult>();
        for (int i = 0; i < limit; i++)
        {
            var result = await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 30,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
            results.Add(result);
        }

        // Assert
        results.ShouldAllBe(r => r.Status == SemaphoreAcquireStatus.Acquired);
        results.Select(r => r.Token).Distinct().Count().ShouldBe(limit); // All unique tokens
        results.Select(r => r.Fencing).Distinct().Count().ShouldBe(limit); // All unique fencing counters
    }

    /// <summary>When acquisitions exceed the limit, then additional acquire attempts return NotAcquired.</summary>
    /// <intent>Ensure the semaphore blocks acquisitions beyond capacity.</intent>
    /// <scenario>Ensure a semaphore with limit 2, acquire twice, then attempt a third acquire.</scenario>
    /// <behavior>The third attempt returns NotAcquired.</behavior>
    [Fact]
    public async Task TryAcquire_BeyondLimit_ReturnsNotAcquired()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 2;
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Acquire up to limit
        for (int i = 0; i < limit; i++)
        {
            await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 30,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Act - Try to acquire one more
        var result = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "ownerExtra",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(SemaphoreAcquireStatus.NotAcquired);
    }

    /// <summary>When a lease is released, then a new acquire can succeed.</summary>
    /// <intent>Verify ReleaseAsync frees capacity for new leases.</intent>
    /// <scenario>Acquire a lease on a limit-1 semaphore, release it, then attempt another acquire.</scenario>
    /// <behavior>The release succeeds and the second acquire is Acquired.</behavior>
    [Fact]
    public async Task Release_FreesCapacity()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 1;
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        var firstAcquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);
        firstAcquire.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);

        // Act - Release and try to acquire again
        var releaseResult = await semaphoreService.ReleaseAsync(
            name,
            firstAcquire.Token!.Value,
            TestContext.Current.CancellationToken);

        var secondAcquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner2",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        releaseResult.Status.ShouldBe(SemaphoreReleaseStatus.Released);
        secondAcquire.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
    }

    /// <summary>When the same token is released twice, then the second release reports NotFound.</summary>
    /// <intent>Ensure ReleaseAsync is idempotent.</intent>
    /// <scenario>Acquire a lease and call ReleaseAsync twice with the same token.</scenario>
    /// <behavior>The first release is Released and the second is NotFound.</behavior>
    [Fact]
    public async Task Release_IdempotentWhenCalledTwice()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        var acquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Act - Release twice
        var firstRelease = await semaphoreService.ReleaseAsync(
            name,
            acquire.Token!.Value,
            TestContext.Current.CancellationToken);

        var secondRelease = await semaphoreService.ReleaseAsync(
            name,
            acquire.Token.Value,
            TestContext.Current.CancellationToken);

        // Assert
        firstRelease.Status.ShouldBe(SemaphoreReleaseStatus.Released);
        secondRelease.Status.ShouldBe(SemaphoreReleaseStatus.NotFound);
    }

    /// <summary>When renewing an active lease, then the expiry moves forward.</summary>
    /// <intent>Verify RenewAsync extends the lease expiration.</intent>
    /// <scenario>Acquire a lease, wait briefly, then renew with the same TTL.</scenario>
    /// <behavior>The renew result is Renewed and ExpiresAtUtc is later than the original.</behavior>
    [Fact]
    public async Task Renew_ExtendsLeaseExpiry()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        var acquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        var originalExpiry = acquire.ExpiresAtUtc!.Value;

        // Act - Wait a bit and renew
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var renewResult = await semaphoreService.RenewAsync(
            name,
            acquire.Token!.Value,
            ttlSeconds: 30,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        renewResult.Status.ShouldBe(SemaphoreRenewStatus.Renewed);
        renewResult.ExpiresAtUtc!.Value.ShouldBeGreaterThan(originalExpiry);
    }

    /// <summary>When renewing after a release, then the lease is reported as lost.</summary>
    /// <intent>Ensure released tokens cannot be renewed.</intent>
    /// <scenario>Acquire a lease, release it, then attempt to renew the same token.</scenario>
    /// <behavior>The renew result status is Lost.</behavior>
    [Fact]
    public async Task Renew_AfterRelease_ReturnsLost()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        var acquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        await semaphoreService.ReleaseAsync(
            name,
            acquire.Token!.Value,
            TestContext.Current.CancellationToken);

        // Act - Try to renew after release
        var renewResult = await semaphoreService.RenewAsync(
            name,
            acquire.Token.Value,
            ttlSeconds: 30,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        renewResult.Status.ShouldBe(SemaphoreRenewStatus.Lost);
    }

    /// <summary>When multiple leases are acquired, then fencing counters strictly increase.</summary>
    /// <intent>Verify fencing counters are monotonic across acquires.</intent>
    /// <scenario>Acquire five leases sequentially and record fencing counters.</scenario>
    /// <behavior>Each fencing counter is greater than the previous one.</behavior>
    [Fact]
    public async Task Fencing_StrictlyIncreases()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 5, TestContext.Current.CancellationToken);

        // Act - Acquire multiple times
        var fencingCounters = new List<long>();
        for (int i = 0; i < 5; i++)
        {
            var result = await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 30,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
            fencingCounters.Add(result.Fencing!.Value);
        }

        // Assert - Fencing counters should be strictly increasing
        for (int i = 1; i < fencingCounters.Count; i++)
        {
            fencingCounters[i].ShouldBeGreaterThan(fencingCounters[i - 1]);
        }
    }

    /// <summary>When renewing with a shorter TTL, then the expiry does not move backward.</summary>
    /// <intent>Ensure RenewAsync enforces a monotonic expiry.</intent>
    /// <scenario>Acquire a lease with TTL 60 and renew it with TTL 10.</scenario>
    /// <behavior>The renewed ExpiresAtUtc is greater than or equal to the original.</behavior>
    [Fact]
    public async Task Renew_MonotonicExtension_NeverShortensExpiry()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        var acquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 60,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        var originalExpiry = acquire.ExpiresAtUtc!.Value;

        // Act - Try to renew with shorter TTL
        var renewResult = await semaphoreService.RenewAsync(
            name,
            acquire.Token!.Value,
            ttlSeconds: 10,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Expiry should not be shortened
        renewResult.Status.ShouldBe(SemaphoreRenewStatus.Renewed);
        renewResult.ExpiresAtUtc!.Value.ShouldBeGreaterThanOrEqualTo(originalExpiry);
    }

    #endregion

    #region Expiry and Reaping Tests

    /// <summary>When a lease expires, then a subsequent acquire succeeds.</summary>
    /// <intent>Verify expired leases free capacity for new acquisitions.</intent>
    /// <scenario>Acquire with a short TTL, wait for expiry, then attempt another acquire.</scenario>
    /// <behavior>The second acquire returns Acquired.</behavior>
    [Fact]
    public async Task TryAcquire_AfterExpiry_Succeeds()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        var limit = 1;
        await semaphoreService!.EnsureExistsAsync(name, limit, TestContext.Current.CancellationToken);

        // Acquire with very short TTL
        var firstAcquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 1,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);
        firstAcquire.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);

        // Wait for expiry
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Try to acquire again
        var secondAcquire = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner2",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        secondAcquire.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
    }

    /// <summary>When reaping expired leases, then expired rows are deleted.</summary>
    /// <intent>Validate ReapExpiredAsync removes expired leases.</intent>
    /// <scenario>Acquire multiple leases with short TTLs, wait for expiry, then reap.</scenario>
    /// <behavior>The deleted count equals the number of expired leases.</behavior>
    [Fact]
    public async Task ReapExpired_RemovesExpiredLeases()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 5, TestContext.Current.CancellationToken);

        // Acquire multiple with short TTL
        for (int i = 0; i < 3; i++)
        {
            await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 1,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Wait for expiry
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Reap expired leases
        var deletedCount = await semaphoreService.ReapExpiredAsync(
            name,
            maxRows: 1000,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        deletedCount.ShouldBe(3);
    }

    /// <summary>When reaping with a max row limit, then deletions are capped to that limit.</summary>
    /// <intent>Ensure ReapExpiredAsync respects the maxRows parameter.</intent>
    /// <scenario>Acquire several short-lived leases, wait for expiry, then reap with maxRows 2.</scenario>
    /// <behavior>The first reap deletes 2 leases and the next reap deletes the remainder.</behavior>
    [Fact]
    public async Task ReapExpired_WithMaxRows_LimitsDeletions()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 10, TestContext.Current.CancellationToken);

        // Acquire multiple with short TTL
        for (int i = 0; i < 5; i++)
        {
            await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 1,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
        }

        // Wait for expiry
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Reap with batch size of 2
        var deletedCount = await semaphoreService.ReapExpiredAsync(
            name,
            maxRows: 2,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        deletedCount.ShouldBe(2);

        // Clean up remaining
        var remaining = await semaphoreService.ReapExpiredAsync(
            name,
            maxRows: 10,
            cancellationToken: TestContext.Current.CancellationToken);
        remaining.ShouldBe(3);
    }

    #endregion

    #region Limit Changes Tests

    /// <summary>When the limit is increased, then additional acquires can succeed.</summary>
    /// <intent>Verify UpdateLimitAsync expands capacity.</intent>
    /// <scenario>Acquire at limit 1, update limit to 2, then acquire again.</scenario>
    /// <behavior>The second acquire returns Acquired.</behavior>
    [Fact]
    public async Task UpdateLimit_Increase_EnablesMoreAcquires()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        // Acquire to capacity
        var first = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);
        first.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);

        // Act - Increase limit
        await semaphoreService.UpdateLimitAsync(
            name,
            newLimit: 2,
            ensureIfMissing: false,
            cancellationToken: TestContext.Current.CancellationToken);

        var second = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner2",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        second.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
    }

    /// <summary>When the limit is decreased below active count, then new acquires are blocked until capacity frees.</summary>
    /// <intent>Ensure UpdateLimitAsync enforces the lowered limit.</intent>
    /// <scenario>Acquire two leases, lower limit to one, attempt acquires after each release.</scenario>
    /// <behavior>Acquires are NotAcquired while at the limit and succeed after both releases.</behavior>
    [Fact]
    public async Task UpdateLimit_Decrease_BlocksNewAcquires()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 3, TestContext.Current.CancellationToken);

        // Acquire 2 leases
        var tokens = new List<Guid>();
        for (int i = 0; i < 2; i++)
        {
            var result = await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 30,
                ownerId: $"owner{i}",
                cancellationToken: TestContext.Current.CancellationToken);
            tokens.Add(result.Token!.Value);
        }

        // Act - Decrease limit to 1 (currently have 2 active)
        await semaphoreService.UpdateLimitAsync(
            name,
            newLimit: 1,
            ensureIfMissing: false,
            cancellationToken: TestContext.Current.CancellationToken);

        // Try to acquire new lease (should fail because current count >= new limit)
        var blocked = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner3",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        blocked.Status.ShouldBe(SemaphoreAcquireStatus.NotAcquired);

        // Release one (now have 1 active, still at the limit of 1, so new acquire should fail)
        await semaphoreService.ReleaseAsync(name, tokens[0], TestContext.Current.CancellationToken);

        var afterFirstRelease = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner4",
            cancellationToken: TestContext.Current.CancellationToken);

        afterFirstRelease.Status.ShouldBe(SemaphoreAcquireStatus.NotAcquired); // Still at limit (1 active, limit 1)

        // Release the second one (now have 0 active, under the limit of 1, so new acquire should succeed)
        await semaphoreService.ReleaseAsync(name, tokens[1], TestContext.Current.CancellationToken);

        var afterSecondRelease = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner5",
            cancellationToken: TestContext.Current.CancellationToken);

        afterSecondRelease.Status.ShouldBe(SemaphoreAcquireStatus.Acquired); // Now under limit (0 active, limit 1)
    }

    #endregion

    #region Idempotency Tests

    /// <summary>When acquiring twice with the same client request id, then the same lease is returned.</summary>
    /// <intent>Verify TryAcquireAsync is idempotent by client request id.</intent>
    /// <scenario>Call TryAcquireAsync twice with the same clientRequestId.</scenario>
    /// <behavior>The second result matches the first token and fencing values.</behavior>
    [Fact]
    public async Task TryAcquire_WithSameClientRequestId_ReturnsExistingLease()
    {
        // Arrange
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        var clientRequestId = Guid.NewGuid().ToString();

        // Act - Acquire twice with same client request ID
        var first = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            clientRequestId: clientRequestId,
            cancellationToken: TestContext.Current.CancellationToken);

        var second = await semaphoreService.TryAcquireAsync(
            name,
            ttlSeconds: 30,
            ownerId: "owner1",
            clientRequestId: clientRequestId,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Should get the same token and fencing
        first.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
        second.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
        second.Token.ShouldBe(first.Token);
        second.Fencing.ShouldBe(first.Fencing);
    }

    #endregion

    #region Validation Tests

    /// <summary>When the semaphore name is empty or whitespace, then TryAcquireAsync throws ArgumentException.</summary>
    /// <intent>Validate semaphore name input is required.</intent>
    /// <scenario>Call TryAcquireAsync with invalid name inputs from InlineData.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquire_InvalidName_ThrowsArgumentException(string invalidName)
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService!.TryAcquireAsync(
                invalidName,
                ttlSeconds: 30,
                ownerId: "owner1",
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>When the semaphore name exceeds the maximum length, then TryAcquireAsync throws ArgumentException.</summary>
    /// <intent>Enforce maximum semaphore name length.</intent>
    /// <scenario>Call TryAcquireAsync with a 201-character name.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Fact]
    public async Task TryAcquire_NameTooLong_ThrowsArgumentException()
    {
        var longName = new string('a', 201); // Max is 200

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService!.TryAcquireAsync(
                longName,
                ttlSeconds: 30,
                ownerId: "owner1",
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>When the semaphore name uses allowed characters, then TryAcquireAsync succeeds.</summary>
    /// <intent>Verify valid name patterns are accepted.</intent>
    /// <scenario>Ensure semaphores exist for valid names and call TryAcquireAsync.</scenario>
    /// <behavior>Each attempt returns Acquired.</behavior>
    [Theory]
    [InlineData("test-semaphore")]
    [InlineData("test_semaphore")]
    [InlineData("test:semaphore")]
    [InlineData("test/semaphore")]
    [InlineData("test.semaphore")]
    [InlineData("test123")]
    public async Task TryAcquire_ValidNameCharacters_Succeeds(string validName)
    {
        // Arrange
        await semaphoreService!.EnsureExistsAsync(validName, 1, TestContext.Current.CancellationToken);

        // Act
        var result = await semaphoreService.TryAcquireAsync(
            validName,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        result.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
    }

    /// <summary>When the semaphore name contains invalid characters, then TryAcquireAsync throws ArgumentException.</summary>
    /// <intent>Reject invalid semaphore name characters.</intent>
    /// <scenario>Call TryAcquireAsync with names containing spaces or punctuation like @ or #.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Theory]
    [InlineData("test semaphore")] // Space not allowed
    [InlineData("test@semaphore")] // @ not allowed
    [InlineData("test#semaphore")] // # not allowed
    public async Task TryAcquire_InvalidNameCharacters_ThrowsArgumentException(string invalidName)
    {
        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService!.TryAcquireAsync(
                invalidName,
                ttlSeconds: 30,
                ownerId: "owner1",
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>When the TTL is below the minimum, then TryAcquireAsync throws ArgumentException.</summary>
    /// <intent>Validate minimum TTL constraints.</intent>
    /// <scenario>Ensure a semaphore exists and call TryAcquireAsync with TTL 0.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Fact]
    public async Task TryAcquire_TtlBelowMinimum_ThrowsArgumentException()
    {
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 0, // Below min of 1
                ownerId: "owner1",
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>When the TTL exceeds the maximum, then TryAcquireAsync throws ArgumentException.</summary>
    /// <intent>Validate maximum TTL constraints.</intent>
    /// <scenario>Ensure a semaphore exists and call TryAcquireAsync with TTL 3601.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Fact]
    public async Task TryAcquire_TtlAboveMaximum_ThrowsArgumentException()
    {
        var name = $"test-semaphore-{Guid.NewGuid():N}";
        await semaphoreService!.EnsureExistsAsync(name, 1, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService.TryAcquireAsync(
                name,
                ttlSeconds: 3601, // Above max of 3600
                ownerId: "owner1",
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>When EnsureExistsAsync receives a limit below minimum, then it throws ArgumentException.</summary>
    /// <intent>Enforce minimum semaphore limit.</intent>
    /// <scenario>Call EnsureExistsAsync with limit 0.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Fact]
    public async Task EnsureExists_LimitBelowMinimum_ThrowsArgumentException()
    {
        var name = $"test-semaphore-{Guid.NewGuid():N}";

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService!.EnsureExistsAsync(
                name,
                limit: 0, // Below min of 1
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    /// <summary>When EnsureExistsAsync receives a limit above maximum, then it throws ArgumentException.</summary>
    /// <intent>Enforce maximum semaphore limit.</intent>
    /// <scenario>Call EnsureExistsAsync with limit 10001.</scenario>
    /// <behavior>An ArgumentException is thrown.</behavior>
    [Fact]
    public async Task EnsureExists_LimitAboveMaximum_ThrowsArgumentException()
    {
        var name = $"test-semaphore-{Guid.NewGuid():N}";

        await Should.ThrowAsync<ArgumentException>(async () =>
        {
            await semaphoreService!.EnsureExistsAsync(
                name,
                limit: 10001, // Above max of 10000
                cancellationToken: TestContext.Current.CancellationToken).ConfigureAwait(false);
        });
    }

    #endregion

    #region Multi-Name Isolation Tests

    /// <summary>When acquiring leases for different semaphore names, then each name is isolated.</summary>
    /// <intent>Verify semaphore names do not share capacity.</intent>
    /// <scenario>Create two named semaphores and acquire one lease from each.</scenario>
    /// <behavior>Both acquisitions succeed and tokens differ.</behavior>
    [Fact]
    public async Task MultipleNames_AreIsolated()
    {
        // Arrange
        var name1 = $"test-semaphore-{Guid.NewGuid():N}";
        var name2 = $"test-semaphore-{Guid.NewGuid():N}";

        await semaphoreService!.EnsureExistsAsync(name1, 1, TestContext.Current.CancellationToken);
        await semaphoreService.EnsureExistsAsync(name2, 1, TestContext.Current.CancellationToken);

        // Act - Acquire both
        var acquire1 = await semaphoreService.TryAcquireAsync(
            name1,
            ttlSeconds: 30,
            ownerId: "owner1",
            cancellationToken: TestContext.Current.CancellationToken);

        var acquire2 = await semaphoreService.TryAcquireAsync(
            name2,
            ttlSeconds: 30,
            ownerId: "owner2",
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert - Both should succeed (separate semaphores)
        acquire1.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
        acquire2.Status.ShouldBe(SemaphoreAcquireStatus.Acquired);
        acquire1.Token.ShouldNotBe(acquire2.Token);
    }

    #endregion
}
