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

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class LeaseTests : SqlServerTestBase
{
    private LeaseApi? leaseApi;

    public LeaseTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure the lease schema exists
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        leaseApi = new LeaseApi(ConnectionString, "infra");
    }

    /// <summary>
    /// When a free lease is acquired, then the acquisition succeeds and returns server time with a calculated expiry.
    /// </summary>
    /// <intent>
    /// Verify successful acquisition returns timing data from the server.
    /// </intent>
    /// <scenario>
    /// Given a new lease name, owner, and lease duration against the lease schema.
    /// </scenario>
    /// <behavior>
    /// Then AcquireAsync returns acquired true with a non-default server time and an expiry near the expected duration.
    /// </behavior>
    [Fact]
    public async Task AcquireAsync_WithFreeResource_SucceedsAndReturnsServerTime()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseSeconds = 30;

        // Act
        var result = await leaseApi!.AcquireAsync(leaseName, owner, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        result.acquired.ShouldBeTrue();
        result.serverUtcNow.ShouldNotBe(default(DateTime));
        result.leaseUntilUtc.ShouldNotBeNull();
        result.leaseUntilUtc.Value.ShouldBeGreaterThan(result.serverUtcNow);

        var expectedExpiry = result.serverUtcNow.AddSeconds(leaseSeconds);
        var timeDiff = Math.Abs((result.leaseUntilUtc.Value - expectedExpiry).TotalSeconds);
        timeDiff.ShouldBeLessThan(1); // Allow for small timing differences
    }

    /// <summary>
    /// When a lease is already held, then a second acquisition attempt fails and returns server time without an expiry.
    /// </summary>
    /// <intent>
    /// Ensure AcquireAsync reports contention without granting a lease.
    /// </intent>
    /// <scenario>
    /// Given owner1 acquires the lease and owner2 attempts to acquire it before expiry.
    /// </scenario>
    /// <behavior>
    /// Then AcquireAsync returns acquired false with server time set and a null lease expiry.
    /// </behavior>
    [Fact]
    public async Task AcquireAsync_WithOccupiedResource_FailsAndReturnsServerTime()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner1 = "owner1";
        var owner2 = "owner2";
        var leaseSeconds = 30;

        // First acquisition
        var firstResult = await leaseApi!.AcquireAsync(leaseName, owner1, leaseSeconds, TestContext.Current.CancellationToken);
        firstResult.acquired.ShouldBeTrue();

        // Act - Second acquisition attempt
        var secondResult = await leaseApi.AcquireAsync(leaseName, owner2, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        secondResult.acquired.ShouldBeFalse();
        secondResult.serverUtcNow.ShouldNotBe(default(DateTime));
        secondResult.leaseUntilUtc.ShouldBeNull();
    }

    /// <summary>
    /// When a prior lease has expired, then a new owner can acquire it and receives a fresh expiry.
    /// </summary>
    /// <intent>
    /// Confirm expired leases can be reacquired by another owner.
    /// </intent>
    /// <scenario>
    /// Given a short lease acquired by owner1 and allowed to expire before owner2 acquires it.
    /// </scenario>
    /// <behavior>
    /// Then AcquireAsync returns acquired true with a new lease expiry after server time.
    /// </behavior>
    [Fact]
    public async Task AcquireAsync_WithExpiredLease_SucceedsAndReturnsNewExpiry()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner1 = "owner1";
        var owner2 = "owner2";
        var shortLeaseSeconds = 1; // Very short lease

        // First acquisition with short lease
        var firstResult = await leaseApi!.AcquireAsync(leaseName, owner1, shortLeaseSeconds, TestContext.Current.CancellationToken);
        firstResult.acquired.ShouldBeTrue();

        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Second acquisition after expiry
        var secondResult = await leaseApi.AcquireAsync(leaseName, owner2, 30, TestContext.Current.CancellationToken);

        // Assert
        secondResult.acquired.ShouldBeTrue();
        secondResult.serverUtcNow.ShouldNotBe(default(DateTime));
        secondResult.leaseUntilUtc.ShouldNotBeNull();
        secondResult.leaseUntilUtc.Value.ShouldBeGreaterThan(secondResult.serverUtcNow);
    }

    /// <summary>
    /// When the current lease owner renews, then the lease is extended and server time advances.
    /// </summary>
    /// <intent>
    /// Validate successful renewals extend the lease window.
    /// </intent>
    /// <scenario>
    /// Given a lease acquired by a specific owner and a short delay before renewal.
    /// </scenario>
    /// <behavior>
    /// Then RenewAsync returns renewed true with a later server time and a later lease expiry.
    /// </behavior>
    [Fact]
    public async Task RenewAsync_WithValidOwner_SucceedsAndExtendsLease()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseSeconds = 30;

        // Acquire lease first
        var acquireResult = await leaseApi!.AcquireAsync(leaseName, owner, leaseSeconds, TestContext.Current.CancellationToken);
        acquireResult.acquired.ShouldBeTrue();

        // Wait a moment
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Act - Renew the lease
        var renewResult = await leaseApi.RenewAsync(leaseName, owner, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        renewResult.renewed.ShouldBeTrue();
        renewResult.serverUtcNow.ShouldNotBe(default(DateTime));
        renewResult.leaseUntilUtc.ShouldNotBeNull();
        renewResult.serverUtcNow.ShouldBeGreaterThan(acquireResult.serverUtcNow);
        renewResult.leaseUntilUtc.Value.ShouldBeGreaterThan(acquireResult.leaseUntilUtc!.Value);
    }

    /// <summary>
    /// When a non-owner attempts to renew a lease, then renewal fails and no expiry is returned.
    /// </summary>
    /// <intent>
    /// Prevent renewals from unauthorized owners.
    /// </intent>
    /// <scenario>
    /// Given owner1 acquires the lease and owner2 attempts to renew it.
    /// </scenario>
    /// <behavior>
    /// Then RenewAsync returns renewed false with server time set and a null lease expiry.
    /// </behavior>
    [Fact]
    public async Task RenewAsync_WithWrongOwner_FailsAndReturnsServerTime()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner1 = "owner1";
        var owner2 = "owner2";
        var leaseSeconds = 30;

        // Acquire lease with owner1
        var acquireResult = await leaseApi!.AcquireAsync(leaseName, owner1, leaseSeconds, TestContext.Current.CancellationToken);
        acquireResult.acquired.ShouldBeTrue();

        // Act - Try to renew with owner2
        var renewResult = await leaseApi.RenewAsync(leaseName, owner2, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        renewResult.renewed.ShouldBeFalse();
        renewResult.serverUtcNow.ShouldNotBe(default(DateTime));
        renewResult.leaseUntilUtc.ShouldBeNull();
    }

    /// <summary>
    /// When a lease has already expired, then renewal fails and no expiry is returned.
    /// </summary>
    /// <intent>
    /// Ensure expired leases cannot be renewed.
    /// </intent>
    /// <scenario>
    /// Given a short lease for an owner that is allowed to expire before renewal.
    /// </scenario>
    /// <behavior>
    /// Then RenewAsync returns renewed false with server time set and a null lease expiry.
    /// </behavior>
    [Fact]
    public async Task RenewAsync_WithExpiredLease_FailsAndReturnsServerTime()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var shortLeaseSeconds = 1;

        // Acquire lease with short duration
        var acquireResult = await leaseApi!.AcquireAsync(leaseName, owner, shortLeaseSeconds, TestContext.Current.CancellationToken);
        acquireResult.acquired.ShouldBeTrue();

        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Try to renew expired lease
        var renewResult = await leaseApi.RenewAsync(leaseName, owner, 30, TestContext.Current.CancellationToken);

        // Assert
        renewResult.renewed.ShouldBeFalse();
        renewResult.serverUtcNow.ShouldNotBe(default(DateTime));
        renewResult.leaseUntilUtc.ShouldBeNull();
    }
}
