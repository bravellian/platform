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


using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform.Tests;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SystemLeaseTests : SqlServerTestBase
{
    private SqlLeaseFactory? leaseFactory;

    public SystemLeaseTests(ITestOutputHelper testOutputHelper, SqlServerCollectionFixture fixture)
        : base(testOutputHelper, fixture)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        var config = new LeaseFactoryConfig
        {
            ConnectionString = ConnectionString,
            SchemaName = "infra",
            RenewPercent = 0.6,
            UseGate = false,
            GateTimeoutMs = 200,
        };

        var logger = NullLogger<SqlLeaseFactory>.Instance;
        leaseFactory = new SqlLeaseFactory(config, logger);

        // Ensure the distributed lock schema exists
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            ConnectionString,
            config.SchemaName).ConfigureAwait(false);
    }

    /// <summary>When a lease is acquired for a new resource, then a valid lease instance is returned.</summary>
    /// <intent>Verify AcquireAsync produces a lease with expected identifiers and fencing token.</intent>
    /// <scenario>Given a SqlLeaseFactory and a unique resource name.</scenario>
    /// <behavior>Then the lease has a non-empty owner token, positive fencing token, and matching resource name.</behavior>
    [Fact]
    public async Task AcquireAsync_WithValidResource_CanAcquireLease()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act
        var lease = await leaseFactory!.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        lease.ShouldNotBeNull();
        lease.ResourceName.ShouldBe(resourceName);
        lease.OwnerToken.ShouldNotBe(OwnerToken.Empty);
        lease.FencingToken.ShouldBeGreaterThan(0);
        lease.CancellationToken.IsCancellationRequested.ShouldBeFalse();

        // Cleanup
        await lease.DisposeAsync();
    }

    /// <summary>When AcquireAsync is called twice for the same resource, then the second call returns null.</summary>
    /// <intent>Ensure a lease cannot be acquired concurrently for the same resource.</intent>
    /// <scenario>Given a SqlLeaseFactory and two sequential AcquireAsync calls for the same resource.</scenario>
    /// <behavior>Then the first lease is non-null and the second result is null.</behavior>
    [Fact]
    public async Task AcquireAsync_SameResourceTwice_SecondCallReturnsNull()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act
        var firstLease = await leaseFactory!.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);
        var secondLease = await leaseFactory.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        firstLease.ShouldNotBeNull();
        secondLease.ShouldBeNull();

        // Cleanup
        await firstLease.DisposeAsync();
    }

    /// <summary>When a lease is released, then the resource can be acquired again with a higher fencing token.</summary>
    /// <intent>Validate lease release frees the resource for subsequent acquisition.</intent>
    /// <scenario>Given a resource acquired once, then disposed, and acquired again.</scenario>
    /// <behavior>Then the second lease succeeds and has a higher fencing token.</behavior>
    [Fact]
    public async Task AcquireAsync_AfterLeaseReleased_CanAcquireAgain()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act & Assert - First acquisition
        var firstLease = await leaseFactory!.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);
        firstLease.ShouldNotBeNull();

        var firstFencingToken = firstLease.FencingToken;

        // Release the first lease
        await firstLease.DisposeAsync();

        // Second acquisition should succeed with higher fencing token
        var secondLease = await leaseFactory.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);
        secondLease.ShouldNotBeNull();
        secondLease.FencingToken.ShouldBeGreaterThan(firstFencingToken);

        // Cleanup
        await secondLease.DisposeAsync();
    }

    /// <summary>When TryRenewNowAsync is called on an active lease, then it succeeds and increments the fencing token.</summary>
    /// <intent>Ensure renewals advance the fencing token for valid leases.</intent>
    /// <scenario>Given an acquired lease and its initial fencing token value.</scenario>
    /// <behavior>Then TryRenewNowAsync returns true and the fencing token increases.</behavior>
    [Fact]
    public async Task TryRenewNowAsync_WithValidLease_SucceedsAndIncrementsFencingToken()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        var lease = await leaseFactory!.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);
        lease.ShouldNotBeNull();

        var originalFencingToken = lease.FencingToken;

        // Act
        var renewed = await lease.TryRenewNowAsync(TestContext.Current.CancellationToken);

        // Assert
        renewed.ShouldBeTrue();
        lease.FencingToken.ShouldBeGreaterThan(originalFencingToken);

        // Cleanup
        await lease.DisposeAsync();
    }

    /// <summary>When a lease is still valid, then ThrowIfLost does not throw.</summary>
    /// <intent>Verify lost-lease guard does not trigger for active leases.</intent>
    /// <scenario>Given an acquired lease that has not expired or been released.</scenario>
    /// <behavior>Then calling ThrowIfLost completes without exception.</behavior>
    [Fact]
    public async Task ThrowIfLost_WhenLeaseIsValid_DoesNotThrow()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        var lease = await leaseFactory!.AcquireAsync(resourceName, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);
        lease.ShouldNotBeNull();

        // Act & Assert
        Should.NotThrow(() => lease.ThrowIfLost());

        // Cleanup
        await lease.DisposeAsync();
    }

    /// <summary>When acquiring leases for different resources, then both acquisitions succeed independently.</summary>
    /// <intent>Ensure leases are isolated per resource name.</intent>
    /// <scenario>Given two distinct resource names acquired sequentially.</scenario>
    /// <behavior>Then both leases are non-null with matching resource names and distinct owner tokens.</behavior>
    [Fact]
    public async Task AcquireAsync_WithDifferentResources_BothSucceed()
    {
        // Arrange
        var resource1 = $"test-resource-1-{Guid.NewGuid():N}";
        var resource2 = $"test-resource-2-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act
        var lease1 = await leaseFactory!.AcquireAsync(resource1, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);
        var lease2 = await leaseFactory.AcquireAsync(resource2, leaseDuration, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        lease1.ShouldNotBeNull();
        lease2.ShouldNotBeNull();
        lease1.ResourceName.ShouldBe(resource1);
        lease2.ResourceName.ShouldBe(resource2);
        lease1.OwnerToken.ShouldNotBe(lease2.OwnerToken);

        // Cleanup
        await lease1.DisposeAsync();
        await lease2.DisposeAsync();
    }

    /// <summary>When a custom owner token is provided, then the acquired lease uses that token.</summary>
    /// <intent>Verify custom owner tokens are honored by AcquireAsync.</intent>
    /// <scenario>Given AcquireAsync invoked with a specific OwnerToken value.</scenario>
    /// <behavior>Then the lease OwnerToken equals the supplied token.</behavior>
    [Fact]
    public async Task AcquireAsync_WithCustomOwnerToken_UsesProvidedToken()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var customOwnerToken = OwnerToken.GenerateNew();

        // Act
        var lease = await leaseFactory!.AcquireAsync(resourceName,
            leaseDuration,
            ownerToken: customOwnerToken, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        lease.ShouldNotBeNull();
        lease.OwnerToken.ShouldBe(customOwnerToken);

        // Cleanup
        await lease.DisposeAsync();
    }

    /// <summary>When the same owner token acquires a lease twice, then both acquisitions succeed with increasing fencing tokens.</summary>
    /// <intent>Validate re-entrant acquisition with the same owner token.</intent>
    /// <scenario>Given two AcquireAsync calls for the same resource using the same OwnerToken.</scenario>
    /// <behavior>Then both leases are non-null and the second fencing token is greater than the first.</behavior>
    [Fact]
    public async Task AcquireAsync_ReentrantWithSameOwnerToken_Succeeds()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);
        Bravellian.Platform.OwnerToken ownerToken = Bravellian.Platform.OwnerToken.GenerateNew();

        // Act
        var firstLease = await leaseFactory!.AcquireAsync(resourceName,
            leaseDuration,
            ownerToken: ownerToken, cancellationToken: TestContext.Current.CancellationToken);

        var secondLease = await leaseFactory.AcquireAsync(resourceName,
            leaseDuration,
            ownerToken: ownerToken, cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        firstLease.ShouldNotBeNull();
        secondLease.ShouldNotBeNull();
        firstLease.OwnerToken.ShouldBe(secondLease.OwnerToken);
        secondLease.FencingToken.ShouldBeGreaterThan(firstLease.FencingToken);

        // Cleanup
        await firstLease.DisposeAsync();
        await secondLease.DisposeAsync();
    }
}
