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

public class SchedulerRouterTests
{
    private readonly ITestOutputHelper testOutputHelper;

    public SchedulerRouterTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    private TestLoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    /// <summary>When a known scheduler key is requested, then the router returns a scheduler client.</summary>
    /// <intent>Verify routing to a configured scheduler store by string key.</intent>
    /// <scenario>Given a ConfiguredSchedulerStoreProvider with two database configs and a test logger.</scenario>
    /// <behavior>Then GetSchedulerClient returns a SqlSchedulerClient for the matching identifier.</behavior>
    [Fact]
    public void SchedulerRouter_WithValidKey_ReturnsSchedulerClient()
    {
        // Arrange
        var configs = new[]
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
        };

        var provider = new ConfiguredSchedulerStoreProvider(
            configs,
            TimeProvider.System,
            CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act
        var client = router.GetSchedulerClient("Customer1");

        // Assert
        client.ShouldNotBeNull();
        client.ShouldBeOfType<SqlSchedulerClient>();
    }

    /// <summary>When an unknown scheduler key is requested, then the router throws an InvalidOperationException.</summary>
    /// <intent>Ensure invalid identifiers fail fast when no scheduler is configured.</intent>
    /// <scenario>Given a ConfiguredSchedulerStoreProvider with one configured database.</scenario>
    /// <behavior>Then GetSchedulerClient throws for an unknown key.</behavior>
    [Fact]
    public void SchedulerRouter_WithInvalidKey_ThrowsException()
    {
        // Arrange
        var configs = new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        };

        var provider = new ConfiguredSchedulerStoreProvider(
            configs,
            TimeProvider.System,
            CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => router.GetSchedulerClient("UnknownCustomer"));
    }

    /// <summary>When an empty scheduler key is requested, then the router throws an ArgumentException.</summary>
    /// <intent>Validate key input guarding for scheduler routing.</intent>
    /// <scenario>Given a router configured with one scheduler store.</scenario>
    /// <behavior>Then GetSchedulerClient throws for an empty string key.</behavior>
    [Fact]
    public void SchedulerRouter_WithNullKey_ThrowsException()
    {
        // Arrange
        var configs = new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = "Customer1",
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        };

        var provider = new ConfiguredSchedulerStoreProvider(
            configs,
            TimeProvider.System,
            CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentException>(() => router.GetSchedulerClient(string.Empty));
    }

    /// <summary>When a Guid key is requested, then the router returns a scheduler client for the matching identifier string.</summary>
    /// <intent>Verify Guid overload routes by string conversion.</intent>
    /// <scenario>Given a scheduler config with an Identifier set to a Guid string.</scenario>
    /// <behavior>Then GetSchedulerClient returns a SqlSchedulerClient for that Guid.</behavior>
    [Fact]
    public void SchedulerRouter_WithGuidKey_ReturnsSchedulerClient()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var configs = new[]
        {
            new SchedulerDatabaseConfig
            {
                Identifier = customerId.ToString(),
                ConnectionString = "Server=localhost;Database=Customer1;",
            },
        };

        var provider = new ConfiguredSchedulerStoreProvider(
            configs,
            TimeProvider.System,
            CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act
        var client = router.GetSchedulerClient(customerId);

        // Assert
        client.ShouldNotBeNull();
        client.ShouldBeOfType<SqlSchedulerClient>();
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
            return new TestLogger<SchedulerRouter>(testOutputHelper);
        }

        public void Dispose()
        {
        }
    }
}


