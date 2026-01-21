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

public class DynamicLeaseFactoryProviderTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public DynamicLeaseFactoryProviderTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        timeProvider = new FakeTimeProvider();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    [Fact]
    public async Task DynamicProvider_DiscoversInitialDatabases()
    {
        // Arrange
        var discovery = new SampleLeaseDatabaseDiscovery(new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            },
            new LeaseDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicLeaseFactoryProvider>();

        var provider = new DynamicLeaseFactoryProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        // Act
        var factories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);

        // Assert
        factories.Count.ShouldBe(2);
        provider.GetFactoryIdentifier(factories[0]).ShouldBeOneOf("Customer1", "Customer2");
        provider.GetFactoryIdentifier(factories[1]).ShouldBeOneOf("Customer1", "Customer2");
    }

    [Fact]
    public async Task DynamicProvider_DetectsNewDatabases()
    {
        // Arrange
        var discovery = new SampleLeaseDatabaseDiscovery(new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                EnableSchemaDeployment = false,
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicLeaseFactoryProvider>();

        var provider = new DynamicLeaseFactoryProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);
        initialFactories.Count.ShouldBe(1);

        // Add a new database
        discovery.AddDatabase(new LeaseDatabaseConfig
        {
            Identifier = "Customer2",
            ConnectionString = "Server=localhost;Database=Customer2;",
            EnableSchemaDeployment = false,
        });

        // Act - Force refresh
        await provider.RefreshAsync(TestContext.Current.CancellationToken);
        var updatedFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedFactories.Count.ShouldBe(2);
        provider.GetFactoryIdentifier(updatedFactories[0]).ShouldBeOneOf("Customer1", "Customer2");
        provider.GetFactoryIdentifier(updatedFactories[1]).ShouldBeOneOf("Customer1", "Customer2");
    }

    [Fact]
    public async Task DynamicProvider_DetectsRemovedDatabases()
    {
        // Arrange
        var discovery = new SampleLeaseDatabaseDiscovery(new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                EnableSchemaDeployment = false,
            },
            new LeaseDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                EnableSchemaDeployment = false,
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicLeaseFactoryProvider>();

        var provider = new DynamicLeaseFactoryProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);
        initialFactories.Count.ShouldBe(2);

        // Remove a database
        discovery.RemoveDatabase("Customer2");

        // Act - Force refresh
        await provider.RefreshAsync(TestContext.Current.CancellationToken);
        var updatedFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedFactories.Count.ShouldBe(1);
        provider.GetFactoryIdentifier(updatedFactories[0]).ShouldBe("Customer1");
    }

    [Fact]
    public async Task DynamicProvider_RefreshesAutomaticallyAfterInterval()
    {
        // Arrange
        var discovery = new SampleLeaseDatabaseDiscovery(new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                EnableSchemaDeployment = false,
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicLeaseFactoryProvider>();

        var provider = new DynamicLeaseFactoryProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);
        initialFactories.Count.ShouldBe(1);

        // Add a new database
        discovery.AddDatabase(new LeaseDatabaseConfig
        {
            Identifier = "Customer2",
            ConnectionString = "Server=localhost;Database=Customer2;",
            EnableSchemaDeployment = false,
        });

        // Act - Advance time past refresh interval
        timeProvider.Advance(TimeSpan.FromMinutes(6));
        var updatedFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);

        // Assert - Should automatically refresh
        updatedFactories.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DynamicProvider_GetFactoryByKey_ReturnsCorrectFactory()
    {
        // Arrange
        var discovery = new SampleLeaseDatabaseDiscovery(new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            },
            new LeaseDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicLeaseFactoryProvider>();

        var provider = new DynamicLeaseFactoryProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        // Force initial discovery
        await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);

        // Act
        var factory1 = await provider.GetFactoryByKeyAsync("Customer1", Xunit.TestContext.Current.CancellationToken);
        var factory2 = await provider.GetFactoryByKeyAsync("Customer2", Xunit.TestContext.Current.CancellationToken);
        var factoryUnknown = await provider.GetFactoryByKeyAsync("UnknownCustomer", Xunit.TestContext.Current.CancellationToken);

        // Assert
        factory1.ShouldNotBeNull();
        factory2.ShouldNotBeNull();
        factoryUnknown.ShouldBeNull();
        provider.GetFactoryIdentifier(factory1).ShouldBe("Customer1");
        provider.GetFactoryIdentifier(factory2).ShouldBe("Customer2");
    }

    [Fact]
    public async Task DynamicProvider_DetectsConnectionStringChanges()
    {
        // Arrange
        var discovery = new SampleLeaseDatabaseDiscovery(new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            },
        });

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicLeaseFactoryProvider>();

        var provider = new DynamicLeaseFactoryProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var initialFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);
        var initialFactory = initialFactories[0];

        // Change connection string
        discovery.RemoveDatabase("Customer1");
        discovery.AddDatabase(new LeaseDatabaseConfig
        {
            Identifier = "Customer1",
            ConnectionString = "Server=localhost;Database=Customer1_New;",
            SchemaName = "infra",
            EnableSchemaDeployment = false,
        });

        // Act - Force refresh
        await provider.RefreshAsync(TestContext.Current.CancellationToken);
        var updatedFactories = await provider.GetAllFactoriesAsync(TestContext.Current.CancellationToken);

        // Assert
        updatedFactories.Count.ShouldBe(1);
        provider.GetFactoryIdentifier(updatedFactories[0]).ShouldBe("Customer1");

        // The factory instance should be different (recreated)
        ReferenceEquals(initialFactory, updatedFactories[0]).ShouldBeFalse();
    }
}
