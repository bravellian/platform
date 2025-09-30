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

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

public class SystemLeaseTests : SqlServerTestBase
{
    private SystemLeaseOptions? options;
    private IOptions<SystemLeaseOptions>? mockOptions;
    private SqlLeaseFactory? leaseFactory;

    public SystemLeaseTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
    {
    }

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);

        this.options = new SystemLeaseOptions
        {
            ConnectionString = this.ConnectionString,
            SchemaName = "dbo",
            DefaultLeaseDuration = TimeSpan.FromSeconds(30),
            RenewPercent = 0.6,
            UseGate = false,
            GateTimeoutMs = 200,
        };

        this.mockOptions = Options.Create(this.options);

        var logger = NullLogger<SqlLeaseFactory>.Instance;
        this.leaseFactory = new SqlLeaseFactory(this.mockOptions, logger);

        // Ensure the distributed lock schema exists
        await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
            this.ConnectionString, 
            this.options.SchemaName).ConfigureAwait(false);
    }

    [Fact]
    public async Task AcquireAsync_WithValidResource_CanAcquireLease()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act
        var lease = await this.leaseFactory!.AcquireAsync(resourceName, leaseDuration);

        // Assert
        lease.ShouldNotBeNull();
        lease.ResourceName.ShouldBe(resourceName);
        lease.OwnerToken.ShouldNotBe(Guid.Empty);
        lease.FencingToken.ShouldBeGreaterThan(0);
        lease.CancellationToken.IsCancellationRequested.ShouldBeFalse();

        // Cleanup
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_SameResourceTwice_SecondCallReturnsNull()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act
        var firstLease = await this.leaseFactory!.AcquireAsync(resourceName, leaseDuration);
        var secondLease = await this.leaseFactory.AcquireAsync(resourceName, leaseDuration);

        // Assert
        firstLease.ShouldNotBeNull();
        secondLease.ShouldBeNull();

        // Cleanup
        await firstLease.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_AfterLeaseReleased_CanAcquireAgain()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act & Assert - First acquisition
        var firstLease = await this.leaseFactory!.AcquireAsync(resourceName, leaseDuration);
        firstLease.ShouldNotBeNull();

        var firstFencingToken = firstLease.FencingToken;

        // Release the first lease
        await firstLease.DisposeAsync();

        // Second acquisition should succeed with higher fencing token
        var secondLease = await this.leaseFactory.AcquireAsync(resourceName, leaseDuration);
        secondLease.ShouldNotBeNull();
        secondLease.FencingToken.ShouldBeGreaterThan(firstFencingToken);

        // Cleanup
        await secondLease.DisposeAsync();
    }

    [Fact]
    public async Task TryRenewNowAsync_WithValidLease_SucceedsAndIncrementsFencingToken()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        var lease = await this.leaseFactory!.AcquireAsync(resourceName, leaseDuration);
        lease.ShouldNotBeNull();

        var originalFencingToken = lease.FencingToken;

        // Act
        var renewed = await lease.TryRenewNowAsync();

        // Assert
        renewed.ShouldBeTrue();
        lease.FencingToken.ShouldBeGreaterThan(originalFencingToken);

        // Cleanup
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task ThrowIfLost_WhenLeaseIsValid_DoesNotThrow()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        var lease = await this.leaseFactory!.AcquireAsync(resourceName, leaseDuration);
        lease.ShouldNotBeNull();

        // Act & Assert
        Should.NotThrow(() => lease.ThrowIfLost());

        // Cleanup
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WithDifferentResources_BothSucceed()
    {
        // Arrange
        var resource1 = $"test-resource-1-{Guid.NewGuid():N}";
        var resource2 = $"test-resource-2-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);

        // Act
        var lease1 = await this.leaseFactory!.AcquireAsync(resource1, leaseDuration);
        var lease2 = await this.leaseFactory.AcquireAsync(resource2, leaseDuration);

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

    [Fact]
    public async Task AcquireAsync_WithCustomOwnerToken_UsesProvidedToken()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var customOwnerToken = Guid.NewGuid();

        // Act
        var lease = await this.leaseFactory!.AcquireAsync(
            resourceName, 
            leaseDuration, 
            ownerToken: customOwnerToken);

        // Assert
        lease.ShouldNotBeNull();
        lease.OwnerToken.ShouldBe(customOwnerToken);

        // Cleanup
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_ReentrantWithSameOwnerToken_Succeeds()
    {
        // Arrange
        var resourceName = $"test-resource-{Guid.NewGuid():N}";
        var leaseDuration = TimeSpan.FromSeconds(30);
        var ownerToken = Guid.NewGuid();

        // Act
        var firstLease = await this.leaseFactory!.AcquireAsync(
            resourceName, 
            leaseDuration, 
            ownerToken: ownerToken);
        
        var secondLease = await this.leaseFactory.AcquireAsync(
            resourceName, 
            leaseDuration, 
            ownerToken: ownerToken);

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