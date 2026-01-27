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


using System.Collections.Concurrent;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Tests;
/// <summary>
/// Integration tests that stand up real SQL Server databases (via Testcontainers) and wire
/// multi-database + control plane registration for both list-based and discovery-based setups.
/// These run by default; filter with Traits if needed:
/// dotnet test --filter "Category=Integration&FullyQualifiedName~MultiDatabaseControlPlaneIntegrationTests"
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public class MultiDatabaseControlPlaneIntegrationTests
{
    private readonly PostgresCollectionFixture fixture;
    private readonly ITestOutputHelper output;

    public MultiDatabaseControlPlaneIntegrationTests(ITestOutputHelper output, PostgresCollectionFixture fixture)
    {
        this.output = output;
        this.fixture = fixture;
    }

    /// <summary>
    /// Given list-based registration, then control-plane configuration and tenant discovery are wired correctly.
    /// </summary>
    /// <intent>
    /// Verify list-based registration wires control-plane settings and discovery outputs.
    /// </intent>
    /// <scenario>
    /// Given two tenant databases, a control-plane database, and a service provider built with list registration.
    /// </scenario>
    /// <behavior>
    /// Control-plane settings are populated and discovery/store providers return both tenant databases.
    /// </behavior>
    [Fact]
    public async Task ListRegistration_WiresControlPlaneAndDiscoversDatabases()
    {
        var tenants = await CreateTenantDatabasesAsync(2);
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("controlplane");

        await PrecreateSchemasAsync(tenants, controlPlaneConnection);

        using var provider = BuildServiceProvider(
            tenants,
            controlPlaneConnection,
            useDiscovery: false,
            enableSchemaDeployment: true);

        var config = provider.GetRequiredService<PlatformConfiguration>();
        config.EnvironmentStyle.ShouldBe(PlatformEnvironmentStyle.MultiDatabaseWithControl);
        config.ControlPlaneConnectionString.ShouldBe(controlPlaneConnection);
        config.ControlPlaneSchemaName.ShouldBe("control");

        var discovery = provider.GetRequiredService<IPlatformDatabaseDiscovery>();
        var discovered = await discovery.DiscoverDatabasesAsync(TestContext.Current.CancellationToken);
        discovered.Count.ShouldBe(tenants.Count);
        foreach (var db in tenants)
        {
            discovered.ShouldContain(d => string.Equals(d.Name, db.Name, StringComparison.OrdinalIgnoreCase));
        }

        var storeProvider = provider.GetRequiredService<IOutboxStoreProvider>();
        var stores = await storeProvider.GetAllStoresAsync();
        stores.Count.ShouldBe(tenants.Count);
    }

    /// <summary>
    /// When dispatching for multiple list-registered tenants, then each tenant message is processed.
    /// </summary>
    /// <intent>
    /// Verify multi-tenant outbox dispatch with list-based registration.
    /// </intent>
    /// <scenario>
    /// Given two tenant databases, list-based registration, and a capturing outbox handler.
    /// </scenario>
    /// <behavior>
    /// Each tenant payload is handled and each outbox table records one processed row.
    /// </behavior>
    [Fact]
    public async Task OutboxDispatch_List_MultipleTenants()
    {
        var tenants = await CreateTenantDatabasesAsync(2);
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("controlplane");
        await PrecreateSchemasAsync(tenants, controlPlaneConnection);

        var processed = new ConcurrentBag<string>();
        using var host = await StartHostAsync(
            tenants,
            controlPlaneConnection,
            useDiscovery: false,
            handlerSink: processed);

        await EnqueueTestMessagesAsync(host.Services, tenants, processed);
        await WaitForDispatchAsync(processed, tenants.Count, timeoutSeconds: 10);

        foreach (var db in tenants)
        {
            processed.ShouldContain($"payload-from-{db.Name}");
            var dispatchedCount = await GetIsProcessedCountAsync(db);
            dispatchedCount.ShouldBe(1, $"Expected one processed row in {db.Name}");
        }

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// When dispatching for a single list-registered tenant, then its message is processed.
    /// </summary>
    /// <intent>
    /// Verify single-tenant outbox dispatch with list-based registration.
    /// </intent>
    /// <scenario>
    /// Given one tenant database, list-based registration, and a capturing outbox handler.
    /// </scenario>
    /// <behavior>
    /// The tenant outbox table records one processed message.
    /// </behavior>
    [Fact]
    public async Task OutboxDispatch_List_SingleTenant()
    {
        var tenants = await CreateTenantDatabasesAsync(1);
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("controlplane");
        await PrecreateSchemasAsync(tenants, controlPlaneConnection);

        var processed = new ConcurrentBag<string>();
        using var host = await StartHostAsync(
            tenants,
            controlPlaneConnection,
            useDiscovery: false,
            handlerSink: processed);

        await EnqueueTestMessagesAsync(host.Services, tenants, processed);
        await WaitForDispatchAsync(processed, tenants.Count, timeoutSeconds: 10);

        var dispatchedCount = await GetIsProcessedCountAsync(tenants[0]);
        dispatchedCount.ShouldBe(1);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// When dispatching for multiple discovery-registered tenants, then each tenant message is processed.
    /// </summary>
    /// <intent>
    /// Verify multi-tenant outbox dispatch with discovery-based registration.
    /// </intent>
    /// <scenario>
    /// Given two tenant databases, discovery-based registration, and a capturing outbox handler.
    /// </scenario>
    /// <behavior>
    /// Each outbox table records one processed row after dispatch.
    /// </behavior>
    [Fact(Skip = "Needs review.")]
    public async Task OutboxDispatch_Discovery_MultipleTenants()
    {
        var tenants = await CreateTenantDatabasesAsync(2);
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("controlplane");
        await PrecreateSchemasAsync(tenants, controlPlaneConnection);

        var processed = new ConcurrentBag<string>();
        using var host = await StartHostAsync(
            tenants,
            controlPlaneConnection,
            useDiscovery: true,
            handlerSink: processed);

        await EnqueueTestMessagesAsync(host.Services, tenants, processed);
        await WaitForDispatchAsync(processed, tenants.Count, timeoutSeconds: 10);

        foreach (var db in tenants)
        {
            var dispatchedCount = await GetIsProcessedCountAsync(db);
            dispatchedCount.ShouldBe(1, $"Expected one processed row in {db.Name}");
        }

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// When dispatching for a single discovery-registered tenant, then its message is processed.
    /// </summary>
    /// <intent>
    /// Verify single-tenant outbox dispatch with discovery-based registration.
    /// </intent>
    /// <scenario>
    /// Given one tenant database, discovery-based registration, and a capturing outbox handler.
    /// </scenario>
    /// <behavior>
    /// The tenant outbox table records one processed message.
    /// </behavior>
    [Fact]
    public async Task OutboxDispatch_Discovery_SingleTenant()
    {
        var tenants = await CreateTenantDatabasesAsync(1);
        var controlPlaneConnection = await fixture.CreateTestDatabaseAsync("controlplane");
        await PrecreateSchemasAsync(tenants, controlPlaneConnection);

        var processed = new ConcurrentBag<string>();
        using var host = await StartHostAsync(
            tenants,
            controlPlaneConnection,
            useDiscovery: true,
            handlerSink: processed);

        await EnqueueTestMessagesAsync(host.Services, tenants, processed);
        await WaitForDispatchAsync(processed, tenants.Count, timeoutSeconds: 10);

        var dispatchedCount = await GetIsProcessedCountAsync(tenants[0]);
        dispatchedCount.ShouldBe(1);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    private async Task<List<PlatformDatabase>> CreateTenantDatabasesAsync(int count)
    {
        var tenants = new List<PlatformDatabase>(capacity: count);
        for (var i = 0; i < count; i++)
        {
            tenants.Add(new PlatformDatabase
            {
                Name = $"tenant-{i + 1}",
                ConnectionString = await fixture.CreateTestDatabaseAsync($"tenant{i + 1}").ConfigureAwait(false),
                SchemaName = $"app_{i + 1}",
            });
        }

        return tenants;
    }

    private async Task PrecreateSchemasAsync(
        IEnumerable<PlatformDatabase> tenants,
        string controlPlaneConnection)
    {
        // Pre-seed schemas so test failures reflect runtime behavior rather than initial deployment.
        foreach (var db in tenants)
        {
            await DatabaseSchemaManager.EnsureOutboxSchemaAsync(db.ConnectionString, db.SchemaName, "Outbox")
.ConfigureAwait(false);
            await DatabaseSchemaManager.EnsureWorkQueueSchemaAsync(db.ConnectionString, db.SchemaName)
.ConfigureAwait(false);
        }

        await DatabaseSchemaManager.EnsureSemaphoreSchemaAsync(controlPlaneConnection, "control")
.ConfigureAwait(false);
        await DatabaseSchemaManager.EnsureCentralMetricsSchemaAsync(controlPlaneConnection, "control")
.ConfigureAwait(false);
    }

    private ServiceProvider BuildServiceProvider(
        IReadOnlyCollection<PlatformDatabase> tenants,
        string controlPlaneConnection,
        bool useDiscovery,
        bool enableSchemaDeployment)
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        if (useDiscovery)
        {
            services.AddSingleton<IPlatformDatabaseDiscovery>(new StaticDiscovery(tenants));
            services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery(new PlatformControlPlaneOptions
            {
                ConnectionString = controlPlaneConnection,
                SchemaName = "control",
                EnableSchemaDeployment = enableSchemaDeployment,
            });
        }
        else
        {
            services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(
                tenants,
                new PlatformControlPlaneOptions
                {
                    ConnectionString = controlPlaneConnection,
                    SchemaName = "control",
                    EnableSchemaDeployment = enableSchemaDeployment,
                });
        }

        return services.BuildServiceProvider();
    }

    private async Task<IHost> StartHostAsync(
        IReadOnlyCollection<PlatformDatabase> tenants,
        string controlPlaneConnection,
        bool useDiscovery,
        ConcurrentBag<string> handlerSink,
        bool enableSchemaDeployment = true)
    {
        var builder = Host.CreateDefaultBuilder();

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            logging.SetMinimumLevel(LogLevel.Debug);
        });

        builder.ConfigureServices(services =>
        {
            if (useDiscovery)
            {
                services.AddSingleton<IPlatformDatabaseDiscovery>(new StaticDiscovery(tenants));
                services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery(new PlatformControlPlaneOptions
                {
                    ConnectionString = controlPlaneConnection,
                    SchemaName = "control",
                    EnableSchemaDeployment = enableSchemaDeployment,
                });
            }
            else
            {
                services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(
                    tenants,
                    new PlatformControlPlaneOptions
                    {
                        ConnectionString = controlPlaneConnection,
                        SchemaName = "control",
                        EnableSchemaDeployment = enableSchemaDeployment,
                    });
            }

            services.AddSingleton<IOutboxHandler>(
                _ => new CapturingOutboxHandler("orders.created", handlerSink, output));
        });

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return host;
    }

    private async Task EnqueueTestMessagesAsync(
        IServiceProvider services,
        IEnumerable<PlatformDatabase> tenants,
        ConcurrentBag<string> processed)
    {
        var router = services.GetRequiredService<IOutboxRouter>();
        foreach (var db in tenants)
        {
            var payload = $"payload-from-{db.Name}";
            output.WriteLine($"Enqueuing payload for {db.Name}");
            await router.GetOutbox(db.Name).EnqueueAsync(
                "orders.created",
                payload,
                TestContext.Current.CancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> GetIsProcessedCountAsync(PlatformDatabase database)
    {
        var outboxTable = PostgresSqlHelper.Qualify(database.SchemaName, "Outbox");
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = $"""
SELECT COUNT(*) FROM {outboxTable} WHERE "IsProcessed" = TRUE
""";

        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task WaitForDispatchAsync(
        ConcurrentBag<string> processedPayloads,
        int expectedCount,
        int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (processedPayloads.Count(x => x.StartsWith("payload-", StringComparison.Ordinal)) < expectedCount &&
               DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken).ConfigureAwait(false);
        }

        processedPayloads.Count(x => x.StartsWith("payload-", StringComparison.Ordinal))
            .ShouldBe(expectedCount, $"Processed payloads: {string.Join(", ", processedPayloads)}");
    }

    private sealed class StaticDiscovery : IPlatformDatabaseDiscovery
    {
        private readonly IReadOnlyCollection<PlatformDatabase> databases;

        public StaticDiscovery(IReadOnlyCollection<PlatformDatabase> databases)
        {
            this.databases = databases;
        }

        public Task<IReadOnlyCollection<PlatformDatabase>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(databases);
        }
    }

    private sealed class CapturingOutboxHandler : IOutboxHandler
    {
        private readonly string topic;
        private readonly ConcurrentBag<string> sink;
        private readonly ITestOutputHelper output;

        public CapturingOutboxHandler(string topic, ConcurrentBag<string> sink, ITestOutputHelper output)
        {
            this.topic = topic;
            this.sink = sink;
            this.output = output;
        }

        public string Topic => topic;

        public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            output.WriteLine($"Handled {message.Topic} with payload '{message.Payload}' (Id: {message.Id})");
            sink.Add(message.Payload);
            return Task.CompletedTask;
        }
    }
}


