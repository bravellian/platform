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

using Bravellian.Platform.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

public class SchedulerRouterTests
{
    private readonly ITestOutputHelper testOutputHelper;

    public SchedulerRouterTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(this.testOutputHelper);
    }

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
            this.CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act
        var client = router.GetSchedulerClient("Customer1");

        // Assert
        client.ShouldNotBeNull();
        client.ShouldBeOfType<SqlSchedulerClient>();
    }

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
            this.CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act & Assert
        Should.Throw<InvalidOperationException>(() => router.GetSchedulerClient("UnknownCustomer"));
    }

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
            this.CreateLoggerFactory());

        var router = new SchedulerRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentException>(() => router.GetSchedulerClient(string.Empty));
    }

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
            this.CreateLoggerFactory());

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
            return new TestLogger<SchedulerRouter>(this.testOutputHelper);
        }

        public void Dispose()
        {
        }
    }
}
