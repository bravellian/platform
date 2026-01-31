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

    private TestLoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    /// <summary>
    /// When lease factory configs are provided, then the provider creates a factory for each configuration.
    /// </summary>
    /// <intent>
    /// Verify configured provider materializes lease factories from config entries.</intent>
    /// <scenario>
    /// Given two LeaseDatabaseConfig entries and a test logger factory.</scenario>
    /// <behavior>
    /// Then GetAllFactoriesAsync returns two factories with identifiers matching the configs.</behavior>
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
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();

        // Act
        var provider = new ConfiguredLeaseFactoryProvider(configs, loggerFactory);
        var factories = await provider.GetAllFactoriesAsync(Xunit.TestContext.Current.CancellationToken);

        // Assert
        factories.Count.ShouldBe(2);
        provider.GetFactoryIdentifier(factories[0]).ShouldBeOneOf("Customer1", "Customer2");
        provider.GetFactoryIdentifier(factories[1]).ShouldBeOneOf("Customer1", "Customer2");
    }

    /// <summary>
    /// When a factory is requested by key, then the provider returns the matching factory or null.</summary>
    /// <intent>Ensure key-based lookup resolves known factories and rejects unknown keys.</intent>
    /// <scenario>Given two LeaseDatabaseConfig entries and a ConfiguredLeaseFactoryProvider instance.</scenario>
    /// <behavior>Then known keys return factories with matching identifiers and an unknown key returns null.</behavior>
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
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredLeaseFactoryProvider(configs, loggerFactory);

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

    /// <summary>
    /// When a factory not created by the provider is inspected, then its identifier is reported as "Unknown".</summary>
    /// <intent>Confirm identifier lookup only recognizes provider-managed factories.</intent>
    /// <scenario>Given an external SqlLeaseFactory not created by ConfiguredLeaseFactoryProvider.</scenario>
    /// <behavior>Then GetFactoryIdentifier returns "Unknown".</behavior>
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
                SchemaName = "infra",
                EnableSchemaDeployment = false,
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredLeaseFactoryProvider(configs, loggerFactory);

        // Create a factory that's not managed by this provider
        var externalFactory = new SqlLeaseFactory(
            new LeaseFactoryConfig
            {
                ConnectionString = "Server=localhost;Database=External;",
                SchemaName = "infra",
                RenewPercent = 0.6,
                GateTimeoutMs = 200,
                UseGate = false,
            },
            loggerFactory.CreateLogger<SqlLeaseFactory>());

        // Act
        var identifier = provider.GetFactoryIdentifier(externalFactory);

        // Assert
        identifier.ShouldBe("Unknown");
    }
}



