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
        timeProvider = new FakeTimeProvider();
    }

    private ILoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    /// <summary>When a list of inbox options is used, then the provider and router resolve inboxes for each tenant.</summary>
    /// <intent>Validate configured multi-inbox wiring produces usable stores and router outputs.</intent>
    /// <scenario>Given two SqlInboxOptions and a ConfiguredInboxWorkStoreProvider with a test logger.</scenario>
    /// <behavior>Then GetAllStoresAsync returns two stores and the router returns distinct inboxes.</behavior>
    [Fact]
    public async Task AddMultiSqlInbox_WithListOfOptions_RegistersServicesCorrectlyAsync()
    {
        // Arrange
        var inboxOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "infra",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "infra",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();

        // Act - Create the provider using the same logic as the extension method
        var storeProvider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(storeProvider);

        // Assert - Verify the provider was created correctly
        var stores = await storeProvider.GetAllStoresAsync();
        stores.ShouldNotBeNull();
        stores.Count.ShouldBe(2);

        // Verify router can get inboxes for both tenants
        var tenant1Inbox = router.GetInbox("Tenant1");
        var tenant2Inbox = router.GetInbox("Tenant2");

        tenant1Inbox.ShouldNotBeNull();
        tenant2Inbox.ShouldNotBeNull();
        tenant1Inbox.ShouldNotBe(tenant2Inbox);

        testOutputHelper.WriteLine("AddMultiSqlInbox pattern successfully creates functional components");
    }

    /// <summary>When a custom selection strategy is supplied, then a dispatcher can be built with it.</summary>
    /// <intent>Ensure the multi-inbox pattern supports replacing the selection strategy.</intent>
    /// <scenario>Given a DrainFirstInboxSelectionStrategy and a configured inbox provider.</scenario>
    /// <behavior>Then a MultiInboxDispatcher is constructed successfully with the custom strategy.</behavior>
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
        var loggerFactory = CreateLoggerFactory();
        var storeProvider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);

        // Dispatcher uses the selection strategy
        var handlerResolver = new InboxHandlerResolver(Array.Empty<IInboxHandler>());
        var dispatcher = new MultiInboxDispatcher(
            storeProvider,
            customStrategy,
            handlerResolver,
            loggerFactory.CreateLogger<MultiInboxDispatcher>());

        // Assert
        dispatcher.ShouldNotBeNull();

        testOutputHelper.WriteLine("Custom selection strategy pattern is supported");
    }

    /// <summary>When the provider factory pattern is used, then a configured inbox store provider is created.</summary>
    /// <intent>Verify the factory produces a ConfiguredInboxWorkStoreProvider with expected stores.</intent>
    /// <scenario>Given a single SqlInboxOptions entry and a test logger factory.</scenario>
    /// <behavior>Then GetAllStoresAsync returns one store from the configured provider.</behavior>
    [Fact]
    public async Task AddMultiSqlInbox_WithStoreProviderFactory_CreatesProviderCorrectlyAsync()
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

        var loggerFactory = CreateLoggerFactory();

        // Act - Create store provider using factory pattern
        var storeProvider = new ConfiguredInboxWorkStoreProvider(inboxOptions, TimeProvider.System, loggerFactory);

        // Assert
        storeProvider.ShouldNotBeNull();
        storeProvider.ShouldBeOfType<ConfiguredInboxWorkStoreProvider>();

        var stores = await storeProvider.GetAllStoresAsync();
        stores.ShouldNotBeNull();
        stores.Count.ShouldBe(1);

        testOutputHelper.WriteLine("Store provider factory pattern works correctly");
    }

    /// <summary>When dynamic discovery is used, then a dynamic inbox provider and router can be created.</summary>
    /// <intent>Confirm the dynamic provider pattern constructs functional components.</intent>
    /// <scenario>Given a SampleInboxDatabaseDiscovery and a FakeTimeProvider with test logging.</scenario>
    /// <behavior>Then a DynamicInboxWorkStoreProvider and InboxRouter are constructed successfully.</behavior>
    [Fact]
    public void AddDynamicMultiSqlInbox_CreatesProviderCorrectly()
    {
        // Arrange
        var discovery = new SampleInboxDatabaseDiscovery(Array.Empty<InboxDatabaseConfig>());
        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicInboxWorkStoreProvider>();

        // Act - Create dynamic provider
        var storeProvider = new DynamicInboxWorkStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger);

        // Assert
        storeProvider.ShouldNotBeNull();
        storeProvider.ShouldBeOfType<DynamicInboxWorkStoreProvider>();

        var router = new InboxRouter(storeProvider);
        router.ShouldNotBeNull();

        testOutputHelper.WriteLine("AddDynamicMultiSqlInbox pattern creates functional components");
    }

    /// <summary>When a custom refresh interval is supplied, then the dynamic provider is created with that setting.</summary>
    /// <intent>Ensure dynamic inbox discovery supports custom refresh intervals.</intent>
    /// <scenario>Given a SampleInboxDatabaseDiscovery and a custom refresh interval value.</scenario>
    /// <behavior>Then a DynamicInboxWorkStoreProvider is constructed successfully using that interval.</behavior>
    [Fact]
    public void AddDynamicMultiSqlInbox_WithCustomRefreshInterval_ConfiguresCorrectly()
    {
        // Arrange
        var discovery = new SampleInboxDatabaseDiscovery(Array.Empty<InboxDatabaseConfig>());
        var customRefreshInterval = TimeSpan.FromMinutes(10);
        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicInboxWorkStoreProvider>();

        // Act - Create provider with custom interval
        var storeProvider = new DynamicInboxWorkStoreProvider(
            discovery,
            timeProvider,
            loggerFactory,
            logger,
            customRefreshInterval);

        // Assert
        storeProvider.ShouldNotBeNull();
        storeProvider.ShouldBeOfType<DynamicInboxWorkStoreProvider>();

        testOutputHelper.WriteLine("Custom refresh interval is supported in pattern");
    }

    /// <summary>When tenant keys are routed, then each key maps to a distinct inbox instance.</summary>
    /// <intent>Demonstrate multi-tenant routing using configured inbox options.</intent>
    /// <scenario>Given two SqlInboxOptions entries for Tenant1 and Tenant2.</scenario>
    /// <behavior>Then the router returns two different inbox instances for each tenant.</behavior>
    [Fact]
    public void MultiTenantScenario_RoutesToCorrectInbox()
    {
        // Arrange - Setup multi-tenant inbox system
        var tenantOptions = new[]
        {
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "infra",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
            new SqlInboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "infra",
                TableName = "Inbox",
                EnableSchemaDeployment = false,
            },
        };

        var loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredInboxWorkStoreProvider(tenantOptions, TimeProvider.System, loggerFactory);
        var router = new InboxRouter(provider);

        // Act - Get inboxes for different tenants
        var tenant1Inbox = router.GetInbox("Tenant1");
        var tenant2Inbox = router.GetInbox("Tenant2");

        // Assert - Verify we got different inbox instances
        tenant1Inbox.ShouldNotBeNull();
        tenant2Inbox.ShouldNotBeNull();
        tenant1Inbox.ShouldNotBe(tenant2Inbox);

        testOutputHelper.WriteLine($"Successfully routed to Tenant1 inbox: {tenant1Inbox.GetType().Name}");
        testOutputHelper.WriteLine($"Successfully routed to Tenant2 inbox: {tenant2Inbox.GetType().Name}");
    }

    /// <summary>When dynamic discovery loads tenant configurations, then routing returns distinct inboxes per tenant.</summary>
    /// <intent>Validate router behavior with dynamically discovered tenants.</intent>
    /// <scenario>Given a SampleInboxDatabaseDiscovery returning two tenant configs and an initial provider refresh.</scenario>
    /// <behavior>Then GetInbox returns non-null, distinct inboxes for both tenant identifiers.</behavior>
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

        var loggerFactory = CreateLoggerFactory();
        var logger = loggerFactory.CreateLogger<DynamicInboxWorkStoreProvider>();
        var provider = new DynamicInboxWorkStoreProvider(
            discovery,
            timeProvider,
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

        testOutputHelper.WriteLine("Successfully demonstrated dynamic discovery routing");
    }
}
