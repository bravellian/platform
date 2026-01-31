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

public class OutboxRouterTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public OutboxRouterTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        timeProvider = new FakeTimeProvider();
    }

    private TestLoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    /// <summary>When configured tenant keys are requested, then distinct outbox instances are returned.</summary>
    /// <intent>Verify routing by string key across multiple configured outbox stores.</intent>
    /// <scenario>Given two SqlOutboxOptions entries and a ConfiguredOutboxStoreProvider with a FakeTimeProvider.</scenario>
    /// <behavior>Then GetOutbox returns non-null, distinct outbox instances for each tenant key.</behavior>
    [Fact]
    public void GetOutbox_WithStringKey_ReturnsOutbox()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer2;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act
        var outbox1 = router.GetOutbox("Customer1");
        var outbox2 = router.GetOutbox("Customer2");

        // Assert
        outbox1.ShouldNotBeNull();
        outbox2.ShouldNotBeNull();
        outbox1.ShouldNotBe(outbox2);
    }

    /// <summary>When a Guid tenant key is requested, then the router resolves the matching outbox.</summary>
    /// <intent>Validate Guid overload routes by converting to string identifier.</intent>
    /// <scenario>Given a single SqlOutboxOptions entry keyed by a Guid string.</scenario>
    /// <behavior>Then GetOutbox(Guid) returns a non-null outbox instance.</behavior>
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
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act
        var outbox = router.GetOutbox(customerId);

        // Assert
        outbox.ShouldNotBeNull();
    }

    /// <summary>When an unknown tenant key is requested, then the router throws an InvalidOperationException.</summary>
    /// <intent>Ensure the router fails for missing outbox keys.</intent>
    /// <scenario>Given a provider with one configured tenant and a non-existent key.</scenario>
    /// <behavior>Then GetOutbox throws and the message mentions the key.</behavior>
    [Fact]
    public void GetOutbox_WithNonExistentKey_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act & Assert
        var ex = Should.Throw<InvalidOperationException>(() => router.GetOutbox("NonExistent"));
        ex.Message.ShouldContain("NonExistent");
    }

    /// <summary>When a null key is provided, then GetOutbox throws an ArgumentException.</summary>
    /// <intent>Validate input guarding for outbox routing.</intent>
    /// <scenario>Given a configured router and a null string key.</scenario>
    /// <behavior>Then GetOutbox throws ArgumentException.</behavior>
    [Fact]
    public void GetOutbox_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentException>(() => router.GetOutbox((string)null!));
    }

    /// <summary>When an empty key is provided, then GetOutbox throws an ArgumentException.</summary>
    /// <intent>Validate input guarding for outbox routing.</intent>
    /// <scenario>Given a configured router and an empty string key.</scenario>
    /// <behavior>Then GetOutbox throws ArgumentException.</behavior>
    [Fact]
    public void GetOutbox_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act & Assert
        Should.Throw<ArgumentException>(() => router.GetOutbox(string.Empty));
    }

    /// <summary>When dynamic discovery is used, then routing returns distinct outboxes per discovered tenant.</summary>
    /// <intent>Verify DynamicOutboxStoreProvider works with the router after discovery.</intent>
    /// <scenario>Given a SampleOutboxDatabaseDiscovery with two tenants and an initial provider refresh.</scenario>
    /// <behavior>Then GetOutbox returns distinct non-null outboxes for both tenant identifiers.</behavior>
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
                SchemaName = "infra",
                TableName = "Outbox",
            },
            new OutboxDatabaseConfig
            {
                Identifier = "Tenant2",
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        });

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
            discovery,
            timeProvider,
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

    /// <summary>When discovery is refreshed after adding a tenant, then the new outbox becomes available.</summary>
    /// <intent>Ensure provider refresh picks up newly discovered databases.</intent>
    /// <scenario>Given a DynamicOutboxStoreProvider that refreshes after adding Tenant2.</scenario>
    /// <behavior>Then GetOutbox returns a non-null outbox for the new tenant.</behavior>
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

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicOutboxStoreProvider>();

        var provider = new DynamicOutboxStoreProvider(
            discovery,
            timeProvider,
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

    /// <summary>When GetOutbox is called multiple times for the same key, then it returns the same instance.</summary>
    /// <intent>Confirm outbox instances are cached per key.</intent>
    /// <scenario>Given a router configured with a single tenant key.</scenario>
    /// <behavior>Then repeated GetOutbox calls return the same object instance.</behavior>
    [Fact]
    public void GetOutbox_MultipleCallsForSameKey_ReturnsSameInstance()
    {
        // Arrange
        var options = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Customer1;",
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act
        var outbox1 = router.GetOutbox("Customer1");
        var outbox2 = router.GetOutbox("Customer1");

        // Assert
        outbox1.ShouldNotBeNull();
        outbox2.ShouldNotBeNull();
        outbox1.ShouldBe(outbox2); // Same instance
    }

    /// <summary>When a Guid key does not match any configured identifier, then GetOutbox throws with the key in the message.</summary>
    /// <intent>Verify Guid-based lookup reports missing identifiers clearly.</intent>
    /// <scenario>Given a router configured for "Customer1" and a different Guid key.</scenario>
    /// <behavior>Then GetOutbox(Guid) throws InvalidOperationException containing the Guid string.</behavior>
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
                SchemaName = "infra",
                TableName = "Outbox",
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(options, timeProvider, loggerFactory);

        // Create router - but it should use Customer1 as the identifier, not the GUID
        var router = new OutboxRouter(provider);

        // Act - this should throw because the identifier is "Customer1", not the GUID
        var ex = Should.Throw<InvalidOperationException>(() => router.GetOutbox(customerId));

        // Assert
        ex.Message.ShouldContain(customerId.ToString());
    }
}



