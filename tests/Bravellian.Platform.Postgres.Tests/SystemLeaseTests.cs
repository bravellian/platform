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

[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class SystemLeaseTests : PostgresTestBase
{
    private PostgresLeaseFactory? leaseFactory;

    public SystemLeaseTests(ITestOutputHelper testOutputHelper, PostgresCollectionFixture fixture)
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

        var logger = NullLogger<PostgresLeaseFactory>.Instance;
        leaseFactory = new PostgresLeaseFactory(config, logger);

        // Ensure the distributed lock schema exists
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            ConnectionString,
            config.SchemaName).ConfigureAwait(false);
    }

    /// <summary>When acquiring a lease for a valid resource, then a lease is returned with expected metadata.</summary>
    /// <intent>Verify successful lease acquisition populates owner and fencing tokens.</intent>
    /// <scenario>Given a PostgresLeaseFactory, a new resource name, and a 30-second lease duration.</scenario>
    /// <behavior>The lease is non-null with the resource name, non-empty owner token, and active cancellation token.</behavior>
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

    /// <summary>When acquiring the same resource twice, then the second call returns null.</summary>
    /// <intent>Verify lease acquisition is exclusive per resource.</intent>
    /// <scenario>Given one resource acquired and a second acquisition attempt for the same resource.</scenario>
    /// <behavior>The second acquisition returns null.</behavior>
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

    /// <summary>When a lease is released, then acquiring the resource again succeeds.</summary>
    /// <intent>Verify release allows a new lease with a higher fencing token.</intent>
    /// <scenario>Given a resource acquired, disposed, and then acquired again.</scenario>
    /// <behavior>The second lease is non-null with a fencing token greater than the first.</behavior>
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

    /// <summary>When TryRenewNowAsync is called on a valid lease, then it succeeds and increments the fencing token.</summary>
    /// <intent>Verify renewal updates fencing token for a held lease.</intent>
    /// <scenario>Given a lease acquired for a resource with a 30-second duration.</scenario>
    /// <behavior>TryRenewNowAsync returns true and the fencing token increases.</behavior>
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

    /// <summary>When a lease is valid, then ThrowIfLost does not throw.</summary>
    /// <intent>Verify loss checks are no-ops for active leases.</intent>
    /// <scenario>Given a lease acquired for a resource.</scenario>
    /// <behavior>ThrowIfLost completes without exception.</behavior>
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

    /// <summary>When acquiring leases for different resources, then both acquisitions succeed.</summary>
    /// <intent>Verify resource isolation across independent leases.</intent>
    /// <scenario>Given two distinct resource names and one lease factory.</scenario>
    /// <behavior>Both leases are non-null with different resource names and owner tokens.</behavior>
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

    /// <summary>When acquiring with a custom owner token, then the lease uses the provided token.</summary>
    /// <intent>Verify caller-supplied owner tokens are honored.</intent>
    /// <scenario>Given a custom OwnerToken and a new resource name.</scenario>
    /// <behavior>The acquired lease reports the provided owner token.</behavior>
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

    /// <summary>When acquiring the same resource with the same owner token, then both acquisitions succeed.</summary>
    /// <intent>Verify reentrant acquisition with the same owner token is permitted.</intent>
    /// <scenario>Given two AcquireAsync calls for the same resource using the same owner token.</scenario>
    /// <behavior>Both leases are non-null and the second fencing token exceeds the first.</behavior>
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


