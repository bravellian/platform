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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Tests;

/// <summary>
/// Tests to ensure that all customer database features use dynamic discovery
/// and only global features (Semaphores, Metrics) use hardcoded configuration.
/// </summary>
public sealed class DynamicDiscoveryEnforcementTests
{
    /// <summary>
    /// Verifies that when using platform registration with discovery,
    /// customer database features (Inbox, Outbox, Lease, Scheduler, Fanout) 
    /// do NOT have services.Configure<TOptions>() called (they should use discovery instead).
    /// Only global features (Semaphores) should have Configure<TOptions>() called.
    /// </summary>
    [Fact]
    public void PlatformWithDiscovery_ShouldNotConfigureCustomerDatabaseOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Register a test discovery implementation
        services.AddSingleton<IPlatformDatabaseDiscovery>(new ListBasedDatabaseDiscovery(new[]
        {
            new PlatformDatabase
            {
                Name = "CustomerDB1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
            },
        }));

        // Register platform with control plane
        services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            new PlatformControlPlaneOptions
            {
                ConnectionString = "Server=localhost;Database=ControlPlane;",
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            });

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Customer database features should NOT have IOptions configured with actual values
        // Note: We can't check for SystemLeaseOptions because it doesn't have a connection string property
        AssertNoOptionsConfigured<SqlOutboxOptions>(serviceProvider, "Outbox");
        AssertNoOptionsConfigured<SqlInboxOptions>(serviceProvider, "Inbox");
        AssertNoOptionsConfigured<SqlSchedulerOptions>(serviceProvider, "Scheduler");
        AssertNoOptionsConfigured<SqlFanoutOptions>(serviceProvider, "Fanout");

        // Global features SHOULD have IOptions configured
        AssertOptionsConfigured<SemaphoreOptions>(serviceProvider, "Semaphore");
    }

    /// <summary>
    /// Verifies that when using platform registration with a list,
    /// customer database features do NOT have services.Configure<TOptions>() called.
    /// </summary>
    [Fact]
    public void PlatformWithList_ShouldNotConfigureCustomerDatabaseOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "CustomerDB1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
            },
            new PlatformDatabase
            {
                Name = "CustomerDB2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
            },
        };

        // Register platform without control plane
        services.AddPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Customer database features should NOT have IOptions configured
        AssertNoOptionsConfigured<SqlOutboxOptions>(serviceProvider, "Outbox");
        AssertNoOptionsConfigured<SqlInboxOptions>(serviceProvider, "Inbox");
        AssertNoOptionsConfigured<SqlSchedulerOptions>(serviceProvider, "Scheduler");
        AssertNoOptionsConfigured<SqlFanoutOptions>(serviceProvider, "Fanout");

        // No global features configured in this scenario
    }

    /// <summary>
    /// Verifies that all customer database features have their providers registered
    /// when using platform registration.
    /// </summary>
    [Fact]
    public void PlatformWithDiscovery_ShouldRegisterAllProviders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddSingleton<IPlatformDatabaseDiscovery>(new ListBasedDatabaseDiscovery(new[]
        {
            new PlatformDatabase
            {
                Name = "CustomerDB1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
            },
        }));

        services.AddPlatformMultiDatabaseWithDiscovery(enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Act & Assert - Verify all providers are registered
        var outboxProvider = serviceProvider.GetService<IOutboxStoreProvider>();
        var inboxProvider = serviceProvider.GetService<IInboxWorkStoreProvider>();
        var schedulerProvider = serviceProvider.GetService<ISchedulerStoreProvider>();
        var fanoutProvider = serviceProvider.GetService<IFanoutRepositoryProvider>();
        var leaseProvider = serviceProvider.GetService<ILeaseFactoryProvider>();

        Assert.NotNull(outboxProvider);
        Assert.NotNull(inboxProvider);
        Assert.NotNull(schedulerProvider);
        Assert.NotNull(fanoutProvider);
        Assert.NotNull(leaseProvider);
    }

    /// <summary>
    /// Verifies that providers use the correct discovery instance.
    /// </summary>
    [Fact]
    public async Task PlatformProviders_ShouldUseDiscoveryAsync()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        var databases = new[]
        {
            new PlatformDatabase
            {
                Name = "CustomerDB1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
            },
            new PlatformDatabase
            {
                Name = "CustomerDB2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
            },
        };

        services.AddSingleton<IPlatformDatabaseDiscovery>(new ListBasedDatabaseDiscovery(databases));
        services.AddPlatformMultiDatabaseWithDiscovery(enableSchemaDeployment: false);

        var serviceProvider = services.BuildServiceProvider();

        // Act - Get providers and verify they return the correct number of stores/factories
        var outboxProvider = serviceProvider.GetRequiredService<IOutboxStoreProvider>();
        var inboxProvider = serviceProvider.GetRequiredService<IInboxWorkStoreProvider>();
        var schedulerProvider = serviceProvider.GetRequiredService<ISchedulerStoreProvider>();
        var leaseProvider = serviceProvider.GetRequiredService<ILeaseFactoryProvider>();
        var fanoutProvider = serviceProvider.GetRequiredService<IFanoutRepositoryProvider>();

        var outboxStores = await outboxProvider.GetAllStoresAsync();
        var inboxStores = await inboxProvider.GetAllStoresAsync();
        var schedulerStores = await schedulerProvider.GetAllStoresAsync();
        var leaseFactories = await leaseProvider.GetAllFactoriesAsync(Xunit.TestContext.Current.CancellationToken);
        var fanoutPolicyRepos = await fanoutProvider.GetAllPolicyRepositoriesAsync(Xunit.TestContext.Current.CancellationToken);
        var fanoutCursorRepos = await fanoutProvider.GetAllCursorRepositoriesAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert - Each provider should have discovered both databases
        Assert.Equal(2, outboxStores.Count);
        Assert.Equal(2, inboxStores.Count);
        Assert.Equal(2, schedulerStores.Count);
        Assert.Equal(2, leaseFactories.Count);
        Assert.Equal(2, fanoutPolicyRepos.Count);
        Assert.Equal(2, fanoutCursorRepos.Count);
    }

    private static void AssertNoOptionsConfigured<TOptions>(IServiceProvider serviceProvider, string featureName)
        where TOptions : class
    {
        var optionsMonitor = serviceProvider.GetService<IOptionsMonitor<TOptions>>();
        if (optionsMonitor != null)
        {
            // If IOptionsMonitor is registered, check if it has actual configured values
            // The presence of IOptions<TOptions> with non-default values indicates configuration
            var options = serviceProvider.GetService<IOptions<TOptions>>();
            if (options != null)
            {
                // Check if this was actually configured or just the default registration
                var value = options.Value;
                if (value != null && !IsDefaultOptions(value))
                {
                    Assert.Fail($"{featureName} should not have IOptions<{typeof(TOptions).Name}> configured when using discovery. " +
                               "Customer database features must use IPlatformDatabaseDiscovery instead of hardcoded configuration.");
                }
            }
        }
    }

    private static void AssertOptionsConfigured<TOptions>(IServiceProvider serviceProvider, string featureName)
        where TOptions : class
    {
        var options = serviceProvider.GetService<IOptions<TOptions>>();
        Assert.NotNull(options);

        var value = options.Value;
        Assert.NotNull(value);
        Assert.False(IsDefaultOptions(value),
            $"{featureName} (global feature) should have IOptions<{typeof(TOptions).Name}> configured.");
    }

    private static bool IsDefaultOptions(object options)
    {
        // Check if this looks like a default-constructed options object
        // by checking if key properties are null/empty
        return options switch
        {
            SqlOutboxOptions outbox => string.IsNullOrEmpty(outbox.ConnectionString),
            SqlInboxOptions inbox => string.IsNullOrEmpty(inbox.ConnectionString),
            SqlSchedulerOptions scheduler => string.IsNullOrEmpty(scheduler.ConnectionString),
            SqlFanoutOptions fanout => string.IsNullOrEmpty(fanout.ConnectionString),
            SystemLeaseOptions => false, // SystemLeaseOptions has no connection string, so we can't determine if it's configured
                                         // This is OK because leases should not have IOptions configured when using platform discovery
            SemaphoreOptions semaphore => string.IsNullOrEmpty(semaphore.ConnectionString),
            _ => true,
        };
    }
}
