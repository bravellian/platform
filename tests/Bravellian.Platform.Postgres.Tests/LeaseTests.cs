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

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class LeaseTests : PostgresTestBase
{
    private PostgresLeaseApi? PostgresLeaseApi;

    public LeaseTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        // Ensure the lease schema exists
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(ConnectionString, "infra").ConfigureAwait(false);

        PostgresLeaseApi = new PostgresLeaseApi(ConnectionString, "infra");
    }

    /// <summary>
    /// When acquiring a free lease, then the acquisition succeeds and returns server time and expiry.
    /// </summary>
    /// <intent>
    /// Verify acquisition succeeds for an unused lease name.
    /// </intent>
    /// <scenario>
    /// Given a new lease name, an owner, and a 30-second lease duration.
    /// </scenario>
    /// <behavior>
    /// The lease is acquired, serverUtcNow is set, and leaseUntilUtc is shortly after server time.
    /// </behavior>
    [Fact]
    public async Task AcquireAsync_WithFreeResource_SucceedsAndReturnsServerTime()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseSeconds = 30;

        // Act
        var result = await PostgresLeaseApi!.AcquireAsync(leaseName, owner, leaseSeconds, TestContext.Current.CancellationToken);

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
    /// When acquiring an already-held lease, then the acquisition fails and returns server time.
    /// </summary>
    /// <intent>
    /// Verify acquisition fails for an occupied lease.
    /// </intent>
    /// <scenario>
    /// Given owner1 already holds the lease and owner2 attempts to acquire it.
    /// </scenario>
    /// <behavior>
    /// The second acquisition fails, serverUtcNow is set, and leaseUntilUtc is null.
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
        var firstResult = await PostgresLeaseApi!.AcquireAsync(leaseName, owner1, leaseSeconds, TestContext.Current.CancellationToken);
        firstResult.acquired.ShouldBeTrue();

        // Act - Second acquisition attempt
        var secondResult = await PostgresLeaseApi.AcquireAsync(leaseName, owner2, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        secondResult.acquired.ShouldBeFalse();
        secondResult.serverUtcNow.ShouldNotBe(default(DateTime));
        secondResult.leaseUntilUtc.ShouldBeNull();
    }

    /// <summary>
    /// When a lease has expired, then a new owner can acquire it and receives a new expiry.
    /// </summary>
    /// <intent>
    /// Verify acquisition succeeds after lease expiration.
    /// </intent>
    /// <scenario>
    /// Given a short-lived lease is acquired, allowed to expire, and then re-acquired by a new owner.
    /// </scenario>
    /// <behavior>
    /// The second acquisition succeeds with serverUtcNow set and a non-null leaseUntilUtc.
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
        var firstResult = await PostgresLeaseApi!.AcquireAsync(leaseName, owner1, shortLeaseSeconds, TestContext.Current.CancellationToken);
        firstResult.acquired.ShouldBeTrue();

        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Second acquisition after expiry
        var secondResult = await PostgresLeaseApi.AcquireAsync(leaseName, owner2, 30, TestContext.Current.CancellationToken);

        // Assert
        secondResult.acquired.ShouldBeTrue();
        secondResult.serverUtcNow.ShouldNotBe(default(DateTime));
        secondResult.leaseUntilUtc.ShouldNotBeNull();
        secondResult.leaseUntilUtc.Value.ShouldBeGreaterThan(secondResult.serverUtcNow);
    }

    /// <summary>
    /// When renewing with the current owner, then the lease is extended.
    /// </summary>
    /// <intent>
    /// Verify renewal succeeds for the lease holder.
    /// </intent>
    /// <scenario>
    /// Given a lease acquired by an owner and a subsequent renew attempt by the same owner.
    /// </scenario>
    /// <behavior>
    /// Renewal succeeds, server time advances, and the lease expiry moves forward.
    /// </behavior>
    [Fact]
    public async Task RenewAsync_WithValidOwner_SucceedsAndExtendsLease()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var leaseSeconds = 30;

        // Acquire lease first
        var acquireResult = await PostgresLeaseApi!.AcquireAsync(leaseName, owner, leaseSeconds, TestContext.Current.CancellationToken);
        acquireResult.acquired.ShouldBeTrue();

        // Wait a moment
        await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        // Act - Renew the lease
        var renewResult = await PostgresLeaseApi.RenewAsync(leaseName, owner, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        renewResult.renewed.ShouldBeTrue();
        renewResult.serverUtcNow.ShouldNotBe(default(DateTime));
        renewResult.leaseUntilUtc.ShouldNotBeNull();
        renewResult.serverUtcNow.ShouldBeGreaterThan(acquireResult.serverUtcNow);
        renewResult.leaseUntilUtc.Value.ShouldBeGreaterThan(acquireResult.leaseUntilUtc!.Value);
    }

    /// <summary>
    /// When renewing with a different owner, then the renewal fails and no expiry is returned.
    /// </summary>
    /// <intent>
    /// Verify renewal fails for a non-owner.
    /// </intent>
    /// <scenario>
    /// Given owner1 holds the lease and owner2 attempts renewal.
    /// </scenario>
    /// <behavior>
    /// Renewed is false, serverUtcNow is set, and leaseUntilUtc is null.
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
        var acquireResult = await PostgresLeaseApi!.AcquireAsync(leaseName, owner1, leaseSeconds, TestContext.Current.CancellationToken);
        acquireResult.acquired.ShouldBeTrue();

        // Act - Try to renew with owner2
        var renewResult = await PostgresLeaseApi.RenewAsync(leaseName, owner2, leaseSeconds, TestContext.Current.CancellationToken);

        // Assert
        renewResult.renewed.ShouldBeFalse();
        renewResult.serverUtcNow.ShouldNotBe(default(DateTime));
        renewResult.leaseUntilUtc.ShouldBeNull();
    }

    /// <summary>
    /// When renewing after the lease expires, then the renewal fails and returns no expiry.
    /// </summary>
    /// <intent>
    /// Verify renewal fails for an expired lease.
    /// </intent>
    /// <scenario>
    /// Given a short lease is acquired, allowed to expire, and then renewed by the same owner.
    /// </scenario>
    /// <behavior>
    /// Renewed is false, serverUtcNow is set, and leaseUntilUtc is null.
    /// </behavior>
    [Fact]
    public async Task RenewAsync_WithExpiredLease_FailsAndReturnsServerTime()
    {
        // Arrange
        var leaseName = $"test-lease-{Guid.NewGuid():N}";
        var owner = "test-owner";
        var shortLeaseSeconds = 1;

        // Acquire lease with short duration
        var acquireResult = await PostgresLeaseApi!.AcquireAsync(leaseName, owner, shortLeaseSeconds, TestContext.Current.CancellationToken);
        acquireResult.acquired.ShouldBeTrue();

        // Wait for lease to expire
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        // Act - Try to renew expired lease
        var renewResult = await PostgresLeaseApi.RenewAsync(leaseName, owner, 30, TestContext.Current.CancellationToken);

        // Assert
        renewResult.renewed.ShouldBeFalse();
        renewResult.serverUtcNow.ShouldNotBe(default(DateTime));
        renewResult.leaseUntilUtc.ShouldBeNull();
    }
}



