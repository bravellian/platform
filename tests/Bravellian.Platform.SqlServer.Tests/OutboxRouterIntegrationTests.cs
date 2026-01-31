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
/// Integration tests demonstrating end-to-end usage of IOutboxRouter
/// for multi-tenant outbox message creation.
/// </summary>
public class OutboxRouterIntegrationTests
{
    private readonly ITestOutputHelper testOutputHelper;
    private readonly FakeTimeProvider timeProvider;

    public OutboxRouterIntegrationTests(ITestOutputHelper testOutputHelper)
    {
        this.testOutputHelper = testOutputHelper;
        timeProvider = new FakeTimeProvider();
    }

    private TestLoggerFactory CreateLoggerFactory()
    {
        return new TestLoggerFactory(testOutputHelper);
    }

    /// <summary>When tenant keys are routed, then each key resolves to a distinct outbox instance.</summary>
    /// <intent>Demonstrate multi-tenant routing for configured outbox stores.</intent>
    /// <scenario>Given two SqlOutboxOptions entries and a ConfiguredOutboxStoreProvider with FakeTimeProvider.</scenario>
    /// <behavior>Then the router returns non-null, distinct outboxes for Tenant1 and Tenant2.</behavior>
    [Fact]
    public void MultiTenantScenario_CreateMessagesInDifferentDatabases()
    {
        // Arrange - Setup multi-tenant outbox system
        var tenantOptions = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant1;",
                SchemaName = "infra",
                TableName = "Outbox",
                EnableSchemaDeployment = false,
            },
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=Tenant2;",
                SchemaName = "infra",
                TableName = "Outbox",
                EnableSchemaDeployment = false,
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(tenantOptions, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        // Act - Get outboxes for different tenants
        var tenant1Outbox = router.GetOutbox("Tenant1");
        var tenant2Outbox = router.GetOutbox("Tenant2");

        // Assert - Verify we got different outbox instances
        tenant1Outbox.ShouldNotBeNull();
        tenant2Outbox.ShouldNotBeNull();
        tenant1Outbox.ShouldNotBe(tenant2Outbox);

        // Verify both outboxes are functional (would require database in real scenario)
        // This demonstrates the API works correctly
        testOutputHelper.WriteLine($"Successfully routed to Tenant1 outbox: {tenant1Outbox.GetType().Name}");
        testOutputHelper.WriteLine($"Successfully routed to Tenant2 outbox: {tenant2Outbox.GetType().Name}");
    }

    /// <summary>When dynamic discovery loads tenants, then the router resolves distinct outboxes per tenant.</summary>
    /// <intent>Validate dynamic discovery works with outbox routing.</intent>
    /// <scenario>Given a SampleOutboxDatabaseDiscovery with two tenants and an initial provider refresh.</scenario>
    /// <behavior>Then GetOutbox returns non-null, distinct outboxes for both tenant identifiers.</behavior>
    [Fact]
    public async Task DynamicDiscovery_RoutesToCorrectDatabase()
    {
        // Arrange - Setup dynamic multi-tenant system
        var discovery = new SampleOutboxDatabaseDiscovery(new[]
        {
            new OutboxDatabaseConfig
            {
                Identifier = "customer-abc",
                ConnectionString = "Server=localhost;Database=CustomerAbc;",
            },
            new OutboxDatabaseConfig
            {
                Identifier = "customer-xyz",
                ConnectionString = "Server=localhost;Database=CustomerXyz;",
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

        var router = new OutboxRouter(provider);

        // Force initial discovery
        await provider.GetAllStoresAsync(TestContext.Current.CancellationToken);

        // Act - Route messages based on customer ID
        var customerAbcOutbox = router.GetOutbox("customer-abc");
        var customerXyzOutbox = router.GetOutbox("customer-xyz");

        // Assert
        customerAbcOutbox.ShouldNotBeNull();
        customerXyzOutbox.ShouldNotBeNull();
        customerAbcOutbox.ShouldNotBe(customerXyzOutbox);

        testOutputHelper.WriteLine("Successfully demonstrated dynamic discovery routing");
    }

    /// <summary>When a typical application requests outboxes by tenant, then it receives distinct tenant outboxes.</summary>
    /// <intent>Illustrate the routing pattern used by application services.</intent>
    /// <scenario>Given an OrderService using an OutboxRouter backed by configured tenant options.</scenario>
    /// <behavior>Then TenantA and TenantB resolve to different outbox instances.</behavior>
    [Fact]
    public void TypicalApplicationUsage_DemonstratesPattern()
    {
        // This test demonstrates how a typical application would use the router
        // Arrange - Simulated service that creates orders
        var tenantOptions = new[]
        {
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=TenantA;",
                EnableSchemaDeployment = false,
            },
            new SqlOutboxOptions
            {
                ConnectionString = "Server=localhost;Database=TenantB;",
                EnableSchemaDeployment = false,
            },
        };

        ILoggerFactory loggerFactory = CreateLoggerFactory();
        var provider = new ConfiguredOutboxStoreProvider(tenantOptions, timeProvider, loggerFactory);
        var router = new OutboxRouter(provider);

        var orderService = new OrderService(router, loggerFactory.CreateLogger<OrderService>());

        // Act - Create orders for different tenants
        var tenantAOutbox = orderService.GetOutboxForTenant("TenantA");
        var tenantBOutbox = orderService.GetOutboxForTenant("TenantB");

        // Assert
        tenantAOutbox.ShouldNotBeNull();
        tenantBOutbox.ShouldNotBeNull();
        tenantAOutbox.ShouldNotBe(tenantBOutbox);

        testOutputHelper.WriteLine("Demonstrated typical application pattern");
    }

    // Example application service showing the pattern
    private class OrderService
    {
        private readonly IOutboxRouter outboxRouter;
        private readonly ILogger<OrderService> logger;

        public OrderService(IOutboxRouter outboxRouter, ILogger<OrderService> logger)
        {
            this.outboxRouter = outboxRouter;
            this.logger = logger;
        }

        public IOutbox GetOutboxForTenant(string tenantId)
        {
            return outboxRouter.GetOutbox(tenantId);
        }

        public async Task CreateOrderAsync(string tenantId, Order order)
        {
            // Get the outbox for this tenant's database
            var outbox = outboxRouter.GetOutbox(tenantId);

            // Create the message payload
            var payload = JsonSerializer.Serialize(order);

            // Enqueue to the tenant's outbox
            await outbox.EnqueueAsync(topic: "order.created", payload: payload, correlationId: order.OrderId, cancellationToken: CancellationToken.None).ConfigureAwait(false);

            logger.LogInformation(
                "Enqueued order {OrderId} to outbox for tenant {TenantId}",
                order.OrderId,
                tenantId);
        }
    }

    private class Order
    {
        public required string OrderId { get; set; }

        public required string CustomerId { get; set; }

        public decimal TotalAmount { get; set; }
    }
}



