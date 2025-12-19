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
        await DatabaseSchemaManager.EnsureLeaseSchemaAsync(ConnectionString, "dbo").ConfigureAwait(false);

        leaseApi = new LeaseApi(ConnectionString, "dbo");
    }

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
