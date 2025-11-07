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

public class OutboxRouterTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public OutboxRouterTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        this.timeProvider = new FakeTimeProvider();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(this.testOutputHelper);
    }

    [Fact]
    public void GetOutbox_WithStringKey_ReturnsOutbox()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act
        var outbox1 = router.GetOutbox("Customer1");
        var outbox2 = router.GetOutbox("Customer2");

        // Assert
        outbox1.ShouldNotBeNull();
        outbox2.ShouldNotBeNull();
        outbox1.ShouldNotBe(outbox2);
    }

    [Fact]
    public void GetOutbox_WithGuidKey_ReturnsOutbox()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = $"Server=localhost;Database={customerId};",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act
        var outbox = router.GetOutbox(customerId);

        // Assert
        outbox.ShouldNotBeNull();
    }

    [Fact]
    public void GetOutbox_WithNonExistentKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => router.GetOutbox("NonExistent"));
        ex.Message.ShouldContain("NonExistent");
    }

    [Fact]
    public void GetOutbox_WithNullKey_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => router.GetOutbox((string)null!));
    }

    [Fact]
    public void GetOutbox_WithEmptyKey_ThrowsArgumentNullException()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => router.GetOutbox(string.Empty));
    }

    [Fact]
    public async Task DynamicProvider_GetOutboxByKey_ReturnsCorrectOutbox()
    {
        // Arrange
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "Tenant1",
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
            new OutboxDatabaseConfig
            {
                Identifier = "Tenant2",
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        });

        var loggerFactory = this.CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
            discovery,
            this.timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        // Force initial discovery
        await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        var router = new OutboxRouter(provider);

        // Act
        var outbox1 = router.GetOutbox("Tenant1");
        var outbox2 = router.GetOutbox("Tenant2");

        // Assert
        outbox1.ShouldNotBeNull();
        outbox2.ShouldNotBeNull();
        outbox1.ShouldNotBe(outbox2);
    }

    [Fact]
    public async Task DynamicProvider_AfterRefresh_NewOutboxIsAvailable()
    {
        // Arrange
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "Tenant1",
                ConnectionString = "Server=localhost;Database=Tenant1;",
            },
        });

        var loggerFactory = this.CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
            discovery,
            this.timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        var router = new OutboxRouter(provider);

        // Add a new database
        discovery.AddDatabase(new OutboxDatabaseConfig
        {
            Identifier = "Tenant2",
            ConnectionString = "Server=localhost;Database=Tenant2;",
        });

        // Force refresh
        await provider.RefreshAsync(TestContext.Current.CancellationToken);

        // Act
        var outbox2 = router.GetOutbox("Tenant2");

        // Assert
        outbox2.ShouldNotBeNull();
    }

    [Fact]
    public void GetOutbox_MultipleCallsForSameKey_ReturnsSameInstance()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act
        var outbox1 = router.GetOutbox("Customer1");
        var outbox2 = router.GetOutbox("Customer1");

        // Assert
        outbox1.ShouldNotBeNull();
        outbox2.ShouldNotBeNull();
        outbox1.ShouldBe(outbox2); // Same instance
    }

    [Fact]
    public void GetOutbox_GuidKeyConvertsToString_ReturnsOutbox()
    {
        // Arrange
        var customerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "dbo",
                TableName = "Outbox",
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, this.timeProvider, loggerFactory);

        // Manually create a mapping using the GUID string
        var outbox1 = provider.GetOutboxByKey(customerId.ToString());

        // Create router - but it should use Customer1 as the identifier, not the GUID
        var router = new OutboxRouter(provider);

        // Act - this should throw because the identifier is "Customer1", not the GUID
        var ex = Should.Throw<InvalidOperationException>(() => router.GetOutbox(customerId));

        // Assert
        ex.Message.ShouldContain(customerId.ToString());
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
            return new TestLogger<OutboxRouterTests>(this.testOutputHelper);
        }

        public void Dispose()
        {
        }
    }
}
