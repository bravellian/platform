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

using Bravellian.Platform.Audit;
using Bravellian.Platform.Email;
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Operations;
using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// SQL Server provider registration helpers for the full platform stack.
/// </summary>
public static class SqlPlatformServiceCollectionExtensions
{
#pragma warning disable CS0618 // Compatibility wrappers forward to legacy names.
    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment without control plane.
    /// Features run across the provided list of databases using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        bool enableSchemaDeployment = true)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithList(
            services,
            databases,
            enableSchemaDeployment);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment without control plane.
    /// Features run across databases discovered via the provided discovery service using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithDiscovery(
        this IServiceCollection services,
        bool enableSchemaDeployment = true)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithDiscovery(
            services,
            enableSchemaDeployment);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment with control plane.
    /// Features run across the provided list of databases with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithControlPlaneAndList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithControlPlaneAndList(
            services,
            databases,
            controlPlaneOptions);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment with control plane.
    /// Features run across the provided list of databases with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="controlPlaneConnectionString">The connection string for the control plane database.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    [Obsolete("Use the overload that accepts PlatformControlPlaneOptions for more configuration options.")]
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithControlPlaneAndList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        string controlPlaneConnectionString,
        bool enableSchemaDeployment = true)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithControlPlaneAndList(
            services,
            databases,
            controlPlaneConnectionString,
            enableSchemaDeployment);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment with control plane.
    /// Features run across databases discovered via the provided discovery service with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            services,
            controlPlaneOptions);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment with control plane.
    /// Features run across databases discovered via the provided discovery service with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneConnectionString">The connection string for the control plane database.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    [Obsolete("Use the overload that accepts PlatformControlPlaneOptions for more configuration options.")]
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        string controlPlaneConnectionString,
        bool enableSchemaDeployment = true)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            services,
            controlPlaneConnectionString,
            enableSchemaDeployment);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment with control plane using a discovery factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="discoveryFactory">Factory that creates the IPlatformDatabaseDiscovery instance.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        Func<IServiceProvider, IPlatformDatabaseDiscovery> discoveryFactory,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
            services,
            discoveryFactory,
            controlPlaneOptions);
    }

    /// <summary>
    /// Registers the SQL Server platform for a multi-database environment with control plane using a discovery type.
    /// </summary>
    /// <typeparam name="TDiscovery">The discovery implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSqlPlatformMultiDatabaseWithControlPlaneAndDiscovery<TDiscovery>(
        this IServiceCollection services,
        PlatformControlPlaneOptions controlPlaneOptions)
        where TDiscovery : class, IPlatformDatabaseDiscovery
    {
        return PlatformServiceCollectionExtensions.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery<TDiscovery>(
            services,
            controlPlaneOptions);
    }
#pragma warning restore CS0618
    /// <summary>
    /// Registers all SQL Server-backed platform storage components using a single connection string.
    /// Includes Operations, Audit, Email outbox, Webhooks/Observability dependencies, and shared platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQL Server connection string.</param>
    /// <param name="configure">Optional configuration for platform options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSqlPlatform(
        this IServiceCollection services,
        string connectionString,
        Action<SqlPlatformOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        var options = new SqlPlatformOptions
        {
            ConnectionString = connectionString,
        };

        configure?.Invoke(options);
        return services.AddSqlPlatform(options);
    }

    /// <summary>
    /// Registers all SQL Server-backed platform storage components using the supplied options.
    /// Includes Operations, Audit, Email outbox, Webhooks/Observability dependencies, and shared platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Platform options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddSqlPlatform(
        this IServiceCollection services,
        SqlPlatformOptions options)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        services.AddTimeAbstractions();

        RegisterOutbox(services, options);
        RegisterInbox(services, options);
        RegisterScheduler(services, options);
        RegisterFanout(services, options);
        RegisterIdempotency(services, options);
        RegisterSemaphore(services, options);
        RegisterExternalSideEffects(services, options);
        RegisterMetrics(services, options);
        RegisterAudit(services, options);
        RegisterOperations(services, options);
        RegisterEmailOutbox(services, options);

        services.TryAddSingleton<IOutbox>(ResolveDefaultOutbox);
        services.TryAddSingleton<IInbox>(ResolveDefaultInbox);

        return services;
    }

    private static void RegisterOutbox(IServiceCollection services, SqlPlatformOptions options)
    {
        var outboxOptions = new SqlOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureOutbox?.Invoke(outboxOptions);
        services.AddSqlOutbox(outboxOptions);
    }

    private static void RegisterInbox(IServiceCollection services, SqlPlatformOptions options)
    {
        var inboxOptions = new SqlInboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureInbox?.Invoke(inboxOptions);
        services.AddSqlInbox(inboxOptions);
    }

    private static void RegisterScheduler(IServiceCollection services, SqlPlatformOptions options)
    {
        var schedulerOptions = new SqlSchedulerOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
            EnableBackgroundWorkers = options.EnableSchedulerWorkers,
        };

        options.ConfigureScheduler?.Invoke(schedulerOptions);

        var validator = new SqlSchedulerOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(schedulerOptions, validator);

        services.AddOptions<SqlSchedulerOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SqlSchedulerOptions>>(validator));

        services.Configure<SqlSchedulerOptions>(o =>
        {
            o.ConnectionString = schedulerOptions.ConnectionString;
            o.SchemaName = schedulerOptions.SchemaName;
            o.JobsTableName = schedulerOptions.JobsTableName;
            o.JobRunsTableName = schedulerOptions.JobRunsTableName;
            o.TimersTableName = schedulerOptions.TimersTableName;
            o.MaxPollingInterval = schedulerOptions.MaxPollingInterval;
            o.EnableBackgroundWorkers = schedulerOptions.EnableBackgroundWorkers;
            o.EnableSchemaDeployment = schedulerOptions.EnableSchemaDeployment;
        });

        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<SqlSchedulerOptions>>().Value);

#pragma warning disable CS0618 // Legacy single-database leases registration
        services.AddSystemLeases(schedulerOptions.ConnectionString, schedulerOptions.SchemaName);
#pragma warning restore CS0618

        services.AddSingleton<ISchedulerClient, SqlSchedulerClient>();
        services.AddSingleton<SchedulerHealthCheck>();
        services.AddHostedService<SqlSchedulerService>();

        if (schedulerOptions.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }
    }

    private static void RegisterFanout(IServiceCollection services, SqlPlatformOptions options)
    {
        var fanoutOptions = new SqlFanoutOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureFanout?.Invoke(fanoutOptions);
        services.AddSqlFanout(fanoutOptions);
    }

    private static void RegisterIdempotency(IServiceCollection services, SqlPlatformOptions options)
    {
        var idempotencyOptions = new SqlIdempotencyOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureIdempotency?.Invoke(idempotencyOptions);
        services.AddSqlIdempotency(idempotencyOptions);
    }

    private static void RegisterSemaphore(IServiceCollection services, SqlPlatformOptions options)
    {
        services.AddSemaphoreServices(
            options.ConnectionString,
            options.SchemaName,
            options.ConfigureSemaphore);
    }

    private static void RegisterExternalSideEffects(IServiceCollection services, SqlPlatformOptions options)
    {
        var sideEffectOptions = new SqlExternalSideEffectOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
        };

        options.ConfigureExternalSideEffects?.Invoke(sideEffectOptions);

        services.AddOptions<SqlExternalSideEffectOptions>()
            .Configure(o =>
            {
                o.ConnectionString = sideEffectOptions.ConnectionString;
                o.SchemaName = sideEffectOptions.SchemaName;
                o.TableName = sideEffectOptions.TableName;
            });

        services.AddOptions<ExternalSideEffectCoordinatorOptions>();
        services.TryAddSingleton<IExternalSideEffectCoordinator, ExternalSideEffectCoordinator>();
        services.TryAddSingleton<IExternalSideEffectStore, SqlExternalSideEffectStore>();
        services.TryAddSingleton<IExternalSideEffectStoreProvider, SingleExternalSideEffectStoreProvider>();
    }

    private static void RegisterMetrics(IServiceCollection services, SqlPlatformOptions options)
    {
        services.TryAddSingleton<IPlatformDatabaseDiscovery>(new ListBasedDatabaseDiscovery(new[]
        {
            new PlatformDatabase
            {
                Name = "default",
                ConnectionString = options.ConnectionString,
                SchemaName = options.SchemaName,
            },
        }));

        services.AddMetricsExporter(metrics =>
        {
            metrics.SchemaName = options.SchemaName;
            options.ConfigureMetrics?.Invoke(metrics);
        });
        services.AddMetricsExporterHealthCheck();
    }

    private static void RegisterAudit(IServiceCollection services, SqlPlatformOptions options)
    {
        var auditOptions = new SqlAuditOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
        };

        options.ConfigureAudit?.Invoke(auditOptions);

        services.AddOptions<SqlAuditOptions>()
            .Configure(o =>
            {
                o.ConnectionString = auditOptions.ConnectionString;
                o.SchemaName = auditOptions.SchemaName;
                o.AuditEventsTable = auditOptions.AuditEventsTable;
                o.AuditAnchorsTable = auditOptions.AuditAnchorsTable;
                o.ValidationOptions = auditOptions.ValidationOptions;
            });

        services.TryAddSingleton<IAuditEventWriter, SqlAuditEventWriter>();
        services.TryAddSingleton<IAuditEventReader, SqlAuditEventReader>();
    }

    private static void RegisterOperations(IServiceCollection services, SqlPlatformOptions options)
    {
        var operationOptions = new SqlOperationOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
        };

        options.ConfigureOperations?.Invoke(operationOptions);

        services.AddOptions<SqlOperationOptions>()
            .Configure(o =>
            {
                o.ConnectionString = operationOptions.ConnectionString;
                o.SchemaName = operationOptions.SchemaName;
                o.OperationsTable = operationOptions.OperationsTable;
                o.OperationEventsTable = operationOptions.OperationEventsTable;
            });

        services.TryAddSingleton<IOperationTracker, SqlOperationTracker>();
        services.TryAddSingleton<IOperationWatcher, SqlOperationWatcher>();
    }

    private static void RegisterEmailOutbox(IServiceCollection services, SqlPlatformOptions options)
    {
        var emailOutboxOptions = new SqlEmailOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureEmailOutbox?.Invoke(emailOutboxOptions);
        services.AddSqlEmailOutbox(emailOutboxOptions);
    }

    private static IOutbox ResolveDefaultOutbox(IServiceProvider provider)
    {
        var storeProvider = provider.GetRequiredService<IOutboxStoreProvider>();
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No outbox stores are configured. Configure at least one store or use IOutboxRouter.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple outbox stores are configured. Resolve IOutboxRouter instead of IOutbox for multi-database setups.");
        }

        var router = provider.GetRequiredService<IOutboxRouter>();
        var key = storeProvider.GetStoreIdentifier(stores[0]);
        return router.GetOutbox(key);
    }

    private static IInbox ResolveDefaultInbox(IServiceProvider provider)
    {
        var storeProvider = provider.GetRequiredService<IInboxWorkStoreProvider>();
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No inbox work stores are configured. Configure at least one store or use IInboxRouter.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple inbox stores are configured. Resolve IInboxRouter instead of IInbox for multi-database setups.");
        }

        var router = provider.GetRequiredService<IInboxRouter>();
        var key = storeProvider.GetStoreIdentifier(stores[0]);
        return router.GetInbox(key);
    }

    private sealed class SingleExternalSideEffectStoreProvider : IExternalSideEffectStoreProvider
    {
        private readonly IExternalSideEffectStore store;

        public SingleExternalSideEffectStoreProvider(IExternalSideEffectStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public Task<IReadOnlyList<IExternalSideEffectStore>> GetAllStoresAsync()
        {
            return Task.FromResult<IReadOnlyList<IExternalSideEffectStore>>(new[] { store });
        }

        public string GetStoreIdentifier(IExternalSideEffectStore store)
        {
            return ReferenceEquals(this.store, store) ? "default" : "unknown";
        }

        public IExternalSideEffectStore? GetStoreByKey(string key)
        {
            return string.Equals(key, "default", StringComparison.OrdinalIgnoreCase) ? store : null;
        }
    }
}
