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

public class DynamicOutboxStoreProviderTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public DynamicOutboxStoreProviderTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        timeProvider = new FakeTimeProvider();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    private class TestLoggerFactory : ILoggerFactory
    {
        private readonly ITestOutputHelper testOutputHelper;

        public TestLoggerFactory(ITestOutputHelper testOutputHelper)
        {
            this.testOutputHelper = testOutputHelper;
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new TestLogger<DynamicOutboxStoreProvider>(testOutputHelper);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// When the dynamic outbox provider performs initial discovery, then it returns stores for all configured databases.
    /// </summary>
    /// <intent>
    /// Verify initial discovery populates outbox stores from discovery results.
    /// </intent>
    /// <scenario>
    /// Given a SampleOutboxDatabaseDiscovery returning Customer1 and Customer2 configs.
    /// </scenario>
    /// <behavior>
    /// Then GetAllStoresAsync returns two stores with identifiers matching the discovered customers.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_DiscoversInitialDatabases()
    {
        // Arrange
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
            new OutboxDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
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
    /// When a new outbox database is added to discovery, then RefreshAsync updates the store list.
    /// </summary>
    /// <intent>
    /// Ensure the provider detects newly added outbox databases.
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
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        });

        var loggerFactory = CreateLoggerFactory();

        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);
        initialStores.Count.ShouldBe(1);

        // Add a new database
        discovery.AddDatabase(new OutboxDatabaseConfig
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
    /// When an outbox database is removed from discovery, then RefreshAsync removes its store.
    /// </summary>
    /// <intent>
    /// Ensure the provider drops stores for removed outbox databases.
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
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
            new OutboxDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
            },
        });

        var loggerFactory = CreateLoggerFactory();

        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
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
    /// When the refresh interval elapses, then the provider automatically refreshes discovery results.
    /// </summary>
    /// <intent>
    /// Validate time-based automatic refresh behavior.
    /// </intent>
    /// <scenario>
    /// Given a FakeTimeProvider, one initial database, and a second database added before time advances.
    /// </scenario>
    /// <behavior>
    /// Then advancing time past the interval causes GetAllStoresAsync to return two stores.
    /// </behavior>
    [Fact]
    public async Task DynamicProvider_RefreshesAutomaticallyAfterInterval()
    {
        // Arrange
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        });

        var loggerFactory = CreateLoggerFactory();

        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);
        initialStores.Count.ShouldBe(1);

        // Add a new database
        discovery.AddDatabase(new OutboxDatabaseConfig
        {
            Identifier = "Customer2",
            ConnectionString = "Server=localhost;Database=Customer2;",
        });

        // Act - Advance time past refresh interval
        timeProvider.Advance(TimeSpan.FromMinutes(6));
        var updatedStores = await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Assert - Should automatically refresh
        updatedStores.Count.ShouldBe(2);
    }
}

