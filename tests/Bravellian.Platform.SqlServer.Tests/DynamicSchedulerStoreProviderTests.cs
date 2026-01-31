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


using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Bravellian.Platform.Tests;

public class DynamicSchedulerStoreProviderTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public DynamicSchedulerStoreProviderTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        timeProvider = new FakeTimeProvider();
    }

    private TestLoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    /// <summary>
    /// When the dynamic scheduler provider performs initial discovery, then it returns stores for all configured databases.
    /// </summary>
    /// <intent>
    /// Verify initial discovery populates scheduler stores from discovery results.
    /// </intent>
    /// <scenario>
    /// Given a SampleSchedulerDatabaseDiscovery returning Customer1 and Customer2 configs.
    /// </scenario>
    /// <behavior>
    /// Then GetAllStoresAsync returns two stores with identifiers matching the discovered customers.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_DiscoversInitialDatabases()
    {
        // Arrange
        var discovery = new SampleSchedulerDatabaseDiscovery(new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                JobsTableName = "Jobs",
                JobRunsTableName = "JobRuns",
                TimersTableName = "Timers",
            },
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
                JobsTableName = "Jobs",
                JobRunsTableName = "JobRuns",
                TimersTableName = "Timers",
            },
        });

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicSchedulerStoreProvider>();

        var provider = new DynamicSchedulerStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        // Act
        var stores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Assert
        stores.Count.ShouldBe(2);
        provider.GetStoreIdentifier(stores[0]).ShouldBeOneOf("Customer1", "Customer2");
        provider.GetStoreIdentifier(stores[1]).ShouldBeOneOf("Customer1", "Customer2");
    }

    /// <summary>
    /// When a new database appears in discovery, then RefreshAsync updates the store list.
    /// </summary>
    /// <intent>
    /// Ensure the provider detects newly added scheduler databases.
    /// </intent>
    /// <scenario>
    /// Given discovery initially returns Customer1 and later adds Customer2 before RefreshAsync.
    /// </scenario>
    /// <behavior>
    /// Then GetAllStoresAsync returns two stores with identifiers for both customers.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_DetectsNewDatabases()
    {
        // Arrange
        var discovery = new SampleSchedulerDatabaseDiscovery(new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        });

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicSchedulerStoreProvider>();

        var provider = new DynamicSchedulerStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);
        initialStores.Count.ShouldBe(1);

        // Add a new database
        discovery.AddDatabase(new SchedulerDatabaseConfig
        {
            Identifier = "Customer2",
            ConnectionString = "Server=localhost;Database=Customer2;",
        });

        // Act - Force refresh
        await provider.RefreshAsync(TestContext.Current.CancellationToken);
        var updatedStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedStores.Count.ShouldBe(2);
        provider.GetStoreIdentifier(updatedStores[0]).ShouldBeOneOf("Customer1", "Customer2");
        provider.GetStoreIdentifier(updatedStores[1]).ShouldBeOneOf("Customer1", "Customer2");
    }

    /// <summary>
    /// When a database is removed from discovery, then RefreshAsync removes its store.
    /// </summary>
    /// <intent>
    /// Ensure the provider drops stores for removed scheduler databases.
    /// </intent>
    /// <scenario>
    /// Given discovery initially returns Customer1 and Customer2, then Customer2 is removed before RefreshAsync.
    /// </scenario>
    /// <behavior>
    /// Then GetAllStoresAsync returns one store identified as Customer1.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_DetectsRemovedDatabases()
    {
        // Arrange
        var discovery = new SampleSchedulerDatabaseDiscovery(new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
            },
        });

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicSchedulerStoreProvider>();

        var provider = new DynamicSchedulerStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);
        initialStores.Count.ShouldBe(2);

        // Remove a database
        discovery.RemoveDatabase("Customer2");

        // Act - Force refresh
        await provider.RefreshAsync(TestContext.Current.CancellationToken);
        var updatedStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedStores.Count.ShouldBe(1);
        provider.GetStoreIdentifier(updatedStores[0]).ShouldBe("Customer1");
    }

    /// <summary>
    /// When a known scheduler key is requested, then GetSchedulerClientByKey returns a client instance.
    /// </summary>
    /// <intent>
    /// Verify keyed scheduler client lookup works after discovery.
    /// </intent>
    /// <scenario>
    /// Given discovery provides Customer1 and initial discovery has completed.
    /// </scenario>
    /// <behavior>
    /// Then GetSchedulerClientByKey("Customer1") returns a non-null client.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_GetSchedulerClientByKey_ReturnsCorrectClient()
    {
        // Arrange
        var discovery = new SampleSchedulerDatabaseDiscovery(new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        });

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicSchedulerStoreProvider>();

        var provider = new DynamicSchedulerStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger);

        // Force initial discovery
        await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Act
        var client = provider.GetSchedulerClientByKey("Customer1");

        // Assert
        client.ShouldNotBeNull();
    }

    /// <summary>
    /// When an unknown scheduler key is requested, then GetSchedulerClientByKey returns null.
    /// </summary>
    /// <intent>
    /// Ensure missing scheduler keys are reported as null lookups.
    /// </intent>
    /// <scenario>
    /// Given discovery provides Customer1 and initial discovery has completed.
    /// </scenario>
    /// <behavior>
    /// Then GetSchedulerClientByKey("UnknownCustomer") returns null.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_GetSchedulerClientByKey_ReturnsNullForUnknownKey()
    {
        // Arrange
        var discovery = new SampleSchedulerDatabaseDiscovery(new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        });

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicSchedulerStoreProvider>();

        var provider = new DynamicSchedulerStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger);

        // Force initial discovery
        await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Act
        var client = provider.GetSchedulerClientByKey("UnknownCustomer");

        // Assert
        client.ShouldBeNull();
    }
}


