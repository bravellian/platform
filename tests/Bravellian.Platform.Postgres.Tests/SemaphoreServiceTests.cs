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
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SemaphoreServiceTests : PostgresTestBase
{
    private ISemaphoreService? semaphoreService;

    public SemaphoreServiceTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure semaphore schema exists
        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        // Create service
        var options = Options.Create(new PostgresSemaphoreOptions
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

        semaphoreService = new PostgresSemaphoreService(
            options,
            NullLogger<PostgresSemaphoreService>.Instance);
    }

    #region Basic Correctness Tests

    /// <summary>When TryAcquire is called for a non-existent semaphore, then it returns NotAcquired.</summary>
    /// <intent>Verify acquisition fails if the semaphore has not been created.</intent>
    /// <scenario>Given a random semaphore name with no prior EnsureExistsAsync call.</scenario>
    /// <behavior>The acquire result status is NotAcquired.</behavior>
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

    /// <summary>When acquiring up to the semaphore limit, then all acquires succeed.</summary>
    /// <intent>Verify capacity enforcement allows up to the configured limit.</intent>
    /// <scenario>Given a semaphore with limit 3 and three acquire attempts.</scenario>
    /// <behavior>All results are Acquired with unique tokens and fencing counters.</behavior>
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

    /// <summary>When acquiring beyond the limit, then the extra acquire returns NotAcquired.</summary>
    /// <intent>Verify acquisitions beyond capacity are rejected.</intent>
    /// <scenario>Given a semaphore with limit 2 already fully acquired.</scenario>
    /// <behavior>The extra acquire result status is NotAcquired.</behavior>
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

    /// <summary>When a lease is released, then capacity becomes available for another acquire.</summary>
    /// <intent>Verify release frees a slot for a new owner.</intent>
    /// <scenario>Given a semaphore with limit 1 and one acquired token that is released.</scenario>
    /// <behavior>Release returns Released and the next acquire succeeds.</behavior>
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
    /// <intent>Verify releases are idempotent for the same token.</intent>
    /// <scenario>Given a semaphore token released twice.</scenario>
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

    /// <summary>When renewing a valid token, then the expiry is extended.</summary>
    /// <intent>Verify renew updates ExpiresAtUtc for active leases.</intent>
    /// <scenario>Given an acquired token that is renewed after a short delay.</scenario>
    /// <behavior>Renew returns Renewed and ExpiresAtUtc is later than the original value.</behavior>
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

    /// <summary>When renewing after a release, then the renewal returns Lost.</summary>
    /// <intent>Verify renew fails once the lease has been released.</intent>
    /// <scenario>Given an acquired token that has been released before renew.</scenario>
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

    /// <summary>When acquiring sequentially, then fencing counters strictly increase.</summary>
    /// <intent>Verify fencing tokens are monotonically increasing for each acquisition.</intent>
    /// <scenario>Given five successive acquire calls for the same semaphore.</scenario>
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

    /// <summary>When renewing with a shorter TTL, then expiry is not shortened.</summary>
    /// <intent>Verify renewal does not reduce the existing expiration time.</intent>
    /// <scenario>Given a lease acquired with a 60-second TTL and renewed with 10 seconds.</scenario>
    /// <behavior>The renew result is Renewed and ExpiresAtUtc is not earlier than the original expiry.</behavior>
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

    /// <summary>When a lease expires, then a new acquire succeeds.</summary>
    /// <intent>Verify expired leases free capacity for new owners.</intent>
    /// <scenario>Given a semaphore acquired with a short TTL that is allowed to expire.</scenario>
    /// <behavior>The subsequent acquire returns Acquired.</behavior>
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

    /// <summary>When ReapExpired runs after expiry, then expired leases are deleted.</summary>
    /// <intent>Verify reaping removes expired lease rows.</intent>
    /// <scenario>Given three short-lived leases that have expired.</scenario>
    /// <behavior>ReapExpired deletes three rows.</behavior>
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

    /// <summary>When ReapExpired uses maxRows, then deletions are capped.</summary>
    /// <intent>Verify maxRows limits the number of deleted rows per reaper run.</intent>
    /// <scenario>Given five expired leases and a reap call with maxRows set to 2.</scenario>
    /// <behavior>The first reap deletes 2 rows and the subsequent reap deletes the remaining 3.</behavior>
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

    /// <summary>When the limit is increased, then additional acquires succeed.</summary>
    /// <intent>Verify limit increases open capacity for new leases.</intent>
    /// <scenario>Given a semaphore at limit 1 with one active lease, then limit raised to 2.</scenario>
    /// <behavior>The next acquire returns Acquired.</behavior>
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

    /// <summary>When the limit is decreased below the active count, then new acquires are blocked.</summary>
    /// <intent>Verify limit decreases enforce the new capacity immediately.</intent>
    /// <scenario>Given two active leases and a limit reduced to one.</scenario>
    /// <behavior>Acquires remain NotAcquired until the active count drops below the new limit.</behavior>
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

    /// <summary>When the same clientRequestId is reused, then TryAcquire returns the existing lease.</summary>
    /// <intent>Verify idempotent acquisition by client request id.</intent>
    /// <scenario>Given two TryAcquire calls with the same clientRequestId.</scenario>
    /// <behavior>The second result matches the first token and fencing counter.</behavior>
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

    /// <summary>When TryAcquire is called with a blank name, then it throws ArgumentException.</summary>
    /// <intent>Verify semaphore name validation rejects empty or whitespace names.</intent>
    /// <scenario>Given empty or whitespace semaphore names.</scenario>
    /// <behavior>TryAcquireAsync throws ArgumentException.</behavior>
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

    /// <summary>When the semaphore name exceeds the maximum length, then TryAcquire throws ArgumentException.</summary>
    /// <intent>Verify name length validation for semaphore operations.</intent>
    /// <scenario>Given a semaphore name longer than 200 characters.</scenario>
    /// <behavior>TryAcquireAsync throws ArgumentException.</behavior>
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

    /// <summary>When the semaphore name uses allowed characters, then TryAcquire succeeds.</summary>
    /// <intent>Verify allowed name characters are accepted.</intent>
    /// <scenario>Given valid semaphore names that are ensured and acquired.</scenario>
    /// <behavior>The acquire result status is Acquired.</behavior>
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

    /// <summary>When the semaphore name has invalid characters, then TryAcquire throws ArgumentException.</summary>
    /// <intent>Verify disallowed characters are rejected.</intent>
    /// <scenario>Given semaphore names containing spaces or forbidden symbols.</scenario>
    /// <behavior>TryAcquireAsync throws ArgumentException.</behavior>
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

    /// <summary>When ttlSeconds is below the minimum, then TryAcquire throws ArgumentException.</summary>
    /// <intent>Verify TTL lower bound enforcement.</intent>
    /// <scenario>Given ttlSeconds set to 0.</scenario>
    /// <behavior>TryAcquireAsync throws ArgumentException.</behavior>
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

    /// <summary>When ttlSeconds exceeds the maximum, then TryAcquire throws ArgumentException.</summary>
    /// <intent>Verify TTL upper bound enforcement.</intent>
    /// <scenario>Given ttlSeconds set to 3601.</scenario>
    /// <behavior>TryAcquireAsync throws ArgumentException.</behavior>
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

    /// <summary>When EnsureExists is called with a limit below minimum, then it throws ArgumentException.</summary>
    /// <intent>Verify semaphore limit lower bound validation.</intent>
    /// <scenario>Given a limit value of 0.</scenario>
    /// <behavior>EnsureExistsAsync throws ArgumentException.</behavior>
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

    /// <summary>When EnsureExists is called with a limit above maximum, then it throws ArgumentException.</summary>
    /// <intent>Verify semaphore limit upper bound validation.</intent>
    /// <scenario>Given a limit value of 10001.</scenario>
    /// <behavior>EnsureExistsAsync throws ArgumentException.</behavior>
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

    /// <summary>When acquiring different semaphore names, then each name is isolated.</summary>
    /// <intent>Verify independent capacity for different semaphore names.</intent>
    /// <scenario>Given two distinct semaphore names, each ensured with limit 1.</scenario>
    /// <behavior>Both acquires succeed with different tokens.</behavior>
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


