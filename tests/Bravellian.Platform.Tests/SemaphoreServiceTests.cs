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
