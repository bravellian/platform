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

namespace Bravellian.Platform.Tests;

public class ConfiguredLeaseFactoryProviderTests
{
    private readonly ITestOutputHelper testOutputHelper;

    public ConfiguredLeaseFactoryProviderTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    [Fact]
    public async Task ConfiguredProvider_CreatesFactoriesFromConfigsAsync()
    {
        // Arrange
        var configs = new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
            new LeaseDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();

        // Act
        var provider = new ConfiguredLeaseFactoryProvider(configs, loggerFactory);
        var factories = await provider.GetAllFactoriesAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        factories.Count.ShouldBe(2);
        provider.GetFactoryIdentifier(factories[0]).ShouldBeOneOf("Customer1", "Customer2");
        provider.GetFactoryIdentifier(factories[1]).ShouldBeOneOf("Customer1", "Customer2");
    }

    [Fact]
    public async Task ConfiguredProvider_GetFactoryByKey_ReturnsCorrectFactoryAsync()
    {
        // Arrange
        var configs = new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
            new LeaseDatabaseConfig
            {
                Identifier = "Customer2",
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredLeaseFactoryProvider(configs, loggerFactory);

        // Act
        var factory1 = await provider.GetFactoryByKeyAsync("Customer1");
        var factory2 = await provider.GetFactoryByKeyAsync("Customer2");
        var factoryUnknown = await provider.GetFactoryByKeyAsync("UnknownCustomer");

        // Assert
        factory1.ShouldNotBeNull();
        factory2.ShouldNotBeNull();
        factoryUnknown.ShouldBeNull();
        provider.GetFactoryIdentifier(factory1).ShouldBe("Customer1");
        provider.GetFactoryIdentifier(factory2).ShouldBe("Customer2");
    }

    [Fact]
    public void ConfiguredProvider_GetFactoryIdentifier_ReturnsUnknownForInvalidFactory()
    {
        // Arrange
        var configs = new[]
        {
            new LeaseDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredLeaseFactoryProvider(configs, loggerFactory);

        // Create a factory that's not managed by this provider
        var externalFactory = new SqlLeaseFactory(
            Microsoft.Extensions.Options.Options.Create(new SystemLeaseOptions
            {
                ConnectionString = "Server=localhost;Database=External;",
                SchemaName = "dbo",
            }),
            loggerFactory.CreateLogger<SqlLeaseFactory>());

        // Act
        var identifier = provider.GetFactoryIdentifier(externalFactory);

        // Assert
        identifier.ShouldBe("Unknown");
    }
}
