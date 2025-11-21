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
            return new TestLogger<DynamicSchedulerStoreProvider>(testOutputHelper);
        }

        public void Dispose()
        {
        }
    }

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
                SchemaName = "dbo",
                JobsTableName = "Jobs",
                JobRunsTableName = "JobRuns",
                TimersTableName = "Timers",
            },
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "dbo",
                JobsTableName = "Jobs",
                JobRunsTableName = "JobRuns",
                TimersTableName = "Timers",
            },
        });

        var loggerFactory = CreateLoggerFactory();
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

        var loggerFactory = CreateLoggerFactory();
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

        var loggerFactory = CreateLoggerFactory();
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

        var loggerFactory = CreateLoggerFactory();
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

        var loggerFactory = CreateLoggerFactory();
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
