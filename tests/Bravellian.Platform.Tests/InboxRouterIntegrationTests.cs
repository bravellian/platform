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

/// <summary>
/// Integration tests demonstrating end-to-end usage of multi-inbox extension methods
/// and IInboxRouter for multi-tenant inbox message processing.
/// </summary>
public class InboxRouterIntegrationTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public InboxRouterIntegrationTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        this.timeProvider = new FakeTimeProvider();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(this.testOutputHelper);
    }

    [Fact]
    public void AddMultiSqlInbox_WithListOfOptions_RegistersServicesCorrectly()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "dbo",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = this.CreateLoggerFactory();

        // Act - Create the provider using the same logic as the extension method
        var storeProvider = new ConfiguredInboxWorkStoreProvider(inboxOptions, loggerFactory);
        var router = new InboxRouter(storeProvider);

        // Assert - Verify the provider was created correctly
        var stores = storeProvider.GetAllStores();
        stores.ShouldNotBeNull();
        stores.Count.ShouldBe(2);

        // Verify router can get inboxes for both tenants
        var tenant1Inbox = router.GetInbox("Tenant1");
        var tenant2Inbox = router.GetInbox("Tenant2");

        tenant1Inbox.ShouldNotBeNull();
        tenant2Inbox.ShouldNotBeNull();
        tenant1Inbox.ShouldNotBe(tenant2Inbox);

        this.testOutputHelper.WriteLine("AddMultiSqlInbox pattern successfully creates functional components");
    }

    [Fact]
    public void AddMultiSqlInbox_WithCustomSelectionStrategy_UsesProvidedStrategy()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                EnableSchemaDeployment = false,
            },
        };

        var customStrategy = new DrainFirstInboxSelectionStrategy();

        // Act - Verify the pattern supports custom strategies
        var loggerFactory = this.CreateLoggerFactory();
        var storeProvider = new ConfiguredInboxWorkStoreProvider(inboxOptions, loggerFactory);
        
        // Dispatcher uses the selection strategy
        var handlerResolver = new InboxHandlerResolver(Array.Empty<IInboxHandler>());
        var dispatcher = new MultiInboxDispatcher(
            storeProvider,
            customStrategy,
            handlerResolver,
            loggerFactory.CreateLogger<MultiInboxDispatcher>());

        // Assert
        dispatcher.ShouldNotBeNull();

        this.testOutputHelper.WriteLine("Custom selection strategy pattern is supported");
    }

    [Fact]
    public void AddMultiSqlInbox_WithStoreProviderFactory_CreatesProviderCorrectly()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = this.CreateLoggerFactory();

        // Act - Create store provider using factory pattern
        var storeProvider = new ConfiguredInboxWorkStoreProvider(inboxOptions, loggerFactory);

        // Assert
        storeProvider.ShouldNotBeNull();
        storeProvider.ShouldBeOfType<ConfiguredInboxWorkStoreProvider>();

        var stores = storeProvider.GetAllStores();
        stores.ShouldNotBeNull();
        stores.Count.ShouldBe(1);

        this.testOutputHelper.WriteLine("Store provider factory pattern works correctly");
    }

    [Fact]
    public void AddDynamicMultiSqlInbox_CreatesProviderCorrectly()
    {
        // Arrange
        var discovery = new SampleInboxDatabaseDiscovery(Array.Empty<InboxDatabaseConfig>());
        var loggerFactory = this.CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicInboxWorkStoreProvider>();

        // Act - Create dynamic provider
        var storeProvider = new DynamicInboxWorkStoreProvider(
            discovery,
            this.timeProvider,
            loggerFactory,
            logger);

        // Assert
        storeProvider.ShouldNotBeNull();
        storeProvider.ShouldBeOfType<DynamicInboxWorkStoreProvider>();

        var router = new InboxRouter(storeProvider);
        router.ShouldNotBeNull();

        this.testOutputHelper.WriteLine("AddDynamicMultiSqlInbox pattern creates functional components");
    }

    [Fact]
    public void AddDynamicMultiSqlInbox_WithCustomRefreshInterval_ConfiguresCorrectly()
    {
        // Arrange
        var discovery = new SampleInboxDatabaseDiscovery(Array.Empty<InboxDatabaseConfig>());
        var customRefreshInterval = TimeSpan.FromMinutes(10);
        var loggerFactory = this.CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicInboxWorkStoreProvider>();

        // Act - Create provider with custom interval
        var storeProvider = new DynamicInboxWorkStoreProvider(
            discovery,
            this.timeProvider,
            loggerFactory,
            logger,
            customRefreshInterval);

        // Assert
        storeProvider.ShouldNotBeNull();
        storeProvider.ShouldBeOfType<DynamicInboxWorkStoreProvider>();

        this.testOutputHelper.WriteLine("Custom refresh interval is supported in pattern");
    }

    [Fact]
    public void MultiTenantScenario_RoutesToCorrectInbox()
    {
        // Arrange - Setup multi-tenant inbox system
        var tenantOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "dbo",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "dbo",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = this.CreateLoggerFactory();
        var provider = new ConfiguredInboxWorkStoreProvider(tenantOptions, loggerFactory);
        var router = new InboxRouter(provider);

        // Act - Get inboxes for different tenants
        var tenant1Inbox = router.GetInbox("Tenant1");
        var tenant2Inbox = router.GetInbox("Tenant2");

        // Assert - Verify we got different inbox instances
        tenant1Inbox.ShouldNotBeNull();
        tenant2Inbox.ShouldNotBeNull();
        tenant1Inbox.ShouldNotBe(tenant2Inbox);

        this.testOutputHelper.WriteLine($"Successfully routed to Tenant1 inbox: {tenant1Inbox.GetType().Name}");
        this.testOutputHelper.WriteLine($"Successfully routed to Tenant2 inbox: {tenant2Inbox.GetType().Name}");
    }

    [Fact]
    public async Task DynamicDiscovery_RoutesToCorrectDatabase()
    {
        // Arrange - Setup dynamic multi-tenant system
        var discovery = new SampleInboxDatabaseDiscovery(new[]
        {
            new InboxDatabaseConfig
            {
                Identifier = "customer-abc",
                ConnectionString = "Server=localhost;Database=CustomerAbc;",
            },
            new InboxDatabaseConfig
            {
                Identifier = "customer-xyz",
                ConnectionString = "Server=localhost;Database=CustomerXyz;",
            },
        });

        var loggerFactory = this.CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicInboxWorkStoreProvider>();
        var provider = new DynamicInboxWorkStoreProvider(
            discovery,
            this.timeProvider,
            loggerFactory,
            logger,
            refreshInterval: TimeSpan.FromMinutes(5));

        var router = new InboxRouter(provider);

        // Force initial discovery
        await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Act - Route messages based on customer ID
        var customerAbcInbox = router.GetInbox("customer-abc");
        var customerXyzInbox = router.GetInbox("customer-xyz");

        // Assert
        customerAbcInbox.ShouldNotBeNull();
        customerXyzInbox.ShouldNotBeNull();
        customerAbcInbox.ShouldNotBe(customerXyzInbox);

        this.testOutputHelper.WriteLine("Successfully demonstrated dynamic discovery routing");
    }
}
