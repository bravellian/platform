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


using Bravellian.Platform.Email;
using Bravellian.Platform.Metrics;
using Bravellian.Platform.Observability;
using Bravellian.Platform.Semaphore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;
/// <summary>
/// Extension methods for unified platform registration.
/// </summary>
public static class PostgresPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Postgres platform for a multi-database environment without control plane.
    /// Features run across the provided list of databases using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgresPlatformMultiDatabaseWithList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        bool enableSchemaDeployment = true)
    {
        return AddPlatformMultiDatabaseWithList(services, databases, enableSchemaDeployment);
    }

    /// <summary>
    /// Registers the Postgres platform for a multi-database environment without control plane.
    /// Features run across databases discovered via the provided discovery service using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    public static IServiceCollection AddPostgresPlatformMultiDatabaseWithDiscovery(
        this IServiceCollection services,
        bool enableSchemaDeployment = true)
    {
        return AddPlatformMultiDatabaseWithDiscovery(services, enableSchemaDeployment);
    }

    /// <summary>
    /// Registers the Postgres platform for a multi-database environment with control plane.
    /// Features run across the provided list of databases with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        return AddPlatformMultiDatabaseWithControlPlaneAndList(services, databases, controlPlaneOptions);
    }


    /// <summary>
    /// Registers the Postgres platform for a multi-database environment with control plane.
    /// Features run across databases discovered via the provided discovery service with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    public static IServiceCollection AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        return AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(services, controlPlaneOptions);
    }


    /// <summary>
    /// Registers the Postgres platform for a multi-database environment with control plane using a discovery factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="discoveryFactory">Factory that creates the IPlatformDatabaseDiscovery instance.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        Func<IServiceProvider, IPlatformDatabaseDiscovery> discoveryFactory,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        return AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(services, discoveryFactory, controlPlaneOptions);
    }

    /// <summary>
    /// Registers the Postgres platform for a multi-database environment with control plane using a discovery type.
    /// </summary>
    /// <typeparam name="TDiscovery">The discovery implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery<TDiscovery>(
        this IServiceCollection services,
        PlatformControlPlaneOptions controlPlaneOptions)
        where TDiscovery : class, IPlatformDatabaseDiscovery
    {
        return AddPlatformMultiDatabaseWithControlPlaneAndDiscovery<TDiscovery>(services, controlPlaneOptions);
    }
    /// <summary>
    /// Registers all Postgres-backed platform storage components using a single connection string.
    /// Includes Operations, Audit, Email (outbox + delivery), Webhooks/Observability dependencies, and shared platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">Postgres connection string.</param>
    /// <param name="configure">Optional configuration for platform options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPostgresPlatform(
        this IServiceCollection services,
        string connectionString,
        Action<PostgresPlatformOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        var options = new PostgresPlatformOptions
        {
            ConnectionString = connectionString,
        };

        configure?.Invoke(options);
        return services.AddPostgresPlatform(options);
    }

    /// <summary>
    /// Registers all Postgres-backed platform storage components using the supplied options.
    /// Includes Operations, Audit, Email (outbox + delivery), Webhooks/Observability dependencies, and shared platform services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="options">Platform options.</param>
    /// <returns>The service collection.</returns>
    public static IServiceCollection AddPostgresPlatform(
        this IServiceCollection services,
        PostgresPlatformOptions options)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString);

        services.AddPostgresPlatformMultiDatabaseWithControlPlaneAndList(
            new[]
            {
                new PlatformDatabase
                {
                    ConnectionString = options.ConnectionString,
                    SchemaName = options.SchemaName,
                    Name = "Default",
                },
            },
            new PlatformControlPlaneOptions
            {
                ConnectionString = options.ConnectionString,
                SchemaName = options.SchemaName,
                EnableSchemaDeployment = options.EnableSchemaDeployment,
            });

        return services;
    }

    /// <summary>
    /// Registers the platform for a multi-database environment without control plane.
    /// Features run across the provided list of databases using round-robin scheduling.
    /// For single database scenarios, pass a list with one database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddPlatformMultiDatabaseWithList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        bool enableSchemaDeployment = false)
    {
        ArgumentNullException.ThrowIfNull(databases);

        var databaseList = databases.ToList();
        if (databaseList.Count == 0)
        {
            throw new ArgumentException("Database list must not be empty.", nameof(databases));
        }

        // Prevent multiple registrations
        EnsureNotAlreadyRegistered(services);

        // Register configuration
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseNoControl,
            UsesDiscovery = false,
            EnableSchemaDeployment = enableSchemaDeployment,
            RequiresDatabaseAtStartup = true, // List-based: must have at least one database
        };

        services.AddSingleton(configuration);

        // Register list-based discovery
        services.AddSingleton<IPlatformDatabaseDiscovery>(
            new ListBasedDatabaseDiscovery(databaseList));

        // Register lifecycle service
        services.AddSingleton<IHostedService, PlatformLifecycleService>();

        // Register core abstractions
        RegisterCoreServices(services, enableSchemaDeployment);


        return services;
    }

    /// <summary>
    /// Registers the platform for a multi-database environment without control plane.
    /// Features run across databases discovered via the provided discovery service using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    private static IServiceCollection AddPlatformMultiDatabaseWithDiscovery(
        this IServiceCollection services,
        bool enableSchemaDeployment = false)
    {
        // Prevent multiple registrations
        EnsureNotAlreadyRegistered(services);

        // Register configuration
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseNoControl,
            UsesDiscovery = true,
            EnableSchemaDeployment = enableSchemaDeployment,
            RequiresDatabaseAtStartup = false, // Dynamic discovery: can start with zero databases
        };

        services.AddSingleton(configuration);

        // Discovery service must be registered by the caller
        // Validate it exists at runtime in lifecycle service

        // Register lifecycle service
        services.AddSingleton<IHostedService, PlatformLifecycleService>();

        // Register core abstractions
        RegisterCoreServices(services, enableSchemaDeployment);


        return services;
    }

    /// <summary>
    /// Registers the platform for a multi-database environment with control plane.
    /// Features run across the provided list of databases with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        ArgumentNullException.ThrowIfNull(databases);
        ArgumentNullException.ThrowIfNull(controlPlaneOptions);
        if (string.IsNullOrWhiteSpace(controlPlaneOptions.ConnectionString))
        {
            throw new ArgumentException("ConnectionString must be provided.", nameof(controlPlaneOptions));
        }

        if (string.IsNullOrWhiteSpace(controlPlaneOptions.SchemaName))
        {
            throw new ArgumentException("SchemaName must be provided.", nameof(controlPlaneOptions));
        }

        var databaseList = databases.ToList();
        if (databaseList.Count == 0)
        {
            throw new ArgumentException("Database list must not be empty.", nameof(databases));
        }

        // Prevent multiple registrations
        EnsureNotAlreadyRegistered(services);

        // Register configuration
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseWithControl,
            UsesDiscovery = false,
            ControlPlaneConnectionString = controlPlaneOptions.ConnectionString,
            ControlPlaneSchemaName = controlPlaneOptions.SchemaName,
            EnableSchemaDeployment = controlPlaneOptions.EnableSchemaDeployment,
            RequiresDatabaseAtStartup = true, // List-based: must have at least one database
        };

        services.AddSingleton(configuration);

        // Register list-based discovery
        services.AddSingleton<IPlatformDatabaseDiscovery>(
            new ListBasedDatabaseDiscovery(databaseList));

        // Register lifecycle service
        services.AddSingleton<IHostedService, PlatformLifecycleService>();

        // Register core abstractions
        RegisterCoreServices(services, controlPlaneOptions.EnableSchemaDeployment);

        // Register semaphore services for control plane
        services.AddSemaphoreServices(controlPlaneOptions.ConnectionString, controlPlaneOptions.SchemaName);

        return services;
    }

    /// <summary>
    /// Registers the platform for a multi-database environment with control plane.
    /// Features run across the provided list of databases with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="controlPlaneConnectionString">The connection string for the control plane database.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <summary>
    /// Registers the platform for a multi-database environment with control plane.
    /// Features run across databases discovered via the provided discovery service with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    private static IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        ArgumentNullException.ThrowIfNull(controlPlaneOptions);
        if (string.IsNullOrWhiteSpace(controlPlaneOptions.ConnectionString))
        {
            throw new ArgumentException("ConnectionString must be provided.", nameof(controlPlaneOptions));
        }

        if (string.IsNullOrWhiteSpace(controlPlaneOptions.SchemaName))
        {
            throw new ArgumentException("SchemaName must be provided.", nameof(controlPlaneOptions));
        }

        // Prevent multiple registrations
        EnsureNotAlreadyRegistered(services);

        // Register configuration
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseWithControl,
            UsesDiscovery = true,
            ControlPlaneConnectionString = controlPlaneOptions.ConnectionString,
            ControlPlaneSchemaName = controlPlaneOptions.SchemaName,
            EnableSchemaDeployment = controlPlaneOptions.EnableSchemaDeployment,
            RequiresDatabaseAtStartup = false, // Dynamic discovery: can start with zero databases
        };

        services.AddSingleton(configuration);

        // Discovery service must be registered by the caller
        EnsureSingleDiscoveryRegistered(services);

        // Register lifecycle service
        services.AddSingleton<IHostedService, PlatformLifecycleService>();

        // Register core abstractions
        RegisterCoreServices(services, controlPlaneOptions.EnableSchemaDeployment);

        // Register semaphore services for control plane
        services.AddSemaphoreServices(controlPlaneOptions.ConnectionString, controlPlaneOptions.SchemaName);

        return services;
    }

    /// <summary>
    /// Registers the platform for a multi-database environment with control plane.
    /// Features run across databases discovered via the provided discovery service with control plane coordination available for future features.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneConnectionString">The connection string for the control plane database.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <remarks>
    /// Requires an implementation of <see cref="IPlatformDatabaseDiscovery"/> to be registered in the service collection.
    /// </remarks>
    /// <summary>
    /// Registers the platform for a multi-database environment with control plane using a discovery factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="discoveryFactory">Factory that creates the IPlatformDatabaseDiscovery instance.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        Func<IServiceProvider, IPlatformDatabaseDiscovery> discoveryFactory,
        PlatformControlPlaneOptions controlPlaneOptions)
    {
        ArgumentNullException.ThrowIfNull(discoveryFactory);
        services.AddSingleton<IPlatformDatabaseDiscovery>(discoveryFactory);
        return services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(controlPlaneOptions);
    }

    /// <summary>
    /// Registers the platform for a multi-database environment with control plane using a discovery type.
    /// </summary>
    /// <typeparam name="TDiscovery">The discovery implementation.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="controlPlaneOptions">The control plane configuration options.</param>
    /// <returns>The service collection for chaining.</returns>
    private static IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndDiscovery<TDiscovery>(
        this IServiceCollection services,
        PlatformControlPlaneOptions controlPlaneOptions)
        where TDiscovery : class, IPlatformDatabaseDiscovery
    {
        services.AddSingleton<IPlatformDatabaseDiscovery, TDiscovery>();
        return services.AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(controlPlaneOptions);
    }

    private static void EnsureNotAlreadyRegistered(IServiceCollection services)
    {
        // Check if already registered by looking for PlatformConfiguration
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(PlatformConfiguration));
        if (existing != null)
        {
            throw new InvalidOperationException(
                "Platform registration has already been called. Only one of the four AddPlatform* methods can be used. " +
                "Ensure you call exactly one of: AddPostgresPlatformMultiDatabaseWithList, " +
                "AddPostgresPlatformMultiDatabaseWithDiscovery, AddPostgresPlatformMultiDatabaseWithControlPlaneAndList, or " +
                "AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery.");
        }
    }

    private static void EnsureSingleDiscoveryRegistered(IServiceCollection services)
    {
        var discoveryDescriptors = services
            .Where(d => d.ServiceType == typeof(IPlatformDatabaseDiscovery))
            .ToList();

        if (discoveryDescriptors.Count == 0)
        {
            throw new InvalidOperationException(
                "IPlatformDatabaseDiscovery is not registered. Register your discovery implementation before calling AddPostgresPlatformMultiDatabaseWithControlPlaneAndDiscovery (or use the overload that accepts a discovery factory/type).");
        }

        if (discoveryDescriptors.Count > 1)
        {
            var details = string.Join(", ", discoveryDescriptors.Select(DescribeDescriptor));
            throw new InvalidOperationException(
                $"Multiple IPlatformDatabaseDiscovery registrations were found: {details}. Only one discovery implementation is supported. Ensure exactly one is registered.");
        }
    }

    private static string DescribeDescriptor(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType != null)
        {
            return descriptor.ImplementationType.FullName ?? "UnknownType";
        }

        if (descriptor.ImplementationInstance != null)
        {
            return descriptor.ImplementationInstance.GetType().FullName ?? "UnknownInstance";
        }

        if (descriptor.ImplementationFactory != null)
        {
            return $"Factory:{descriptor.ServiceType.FullName}";
        }

        return descriptor.ServiceType.FullName ?? "Unknown";
    }

    private static void ValidateMultiDatabaseStoreRegistrations(IServiceCollection services, PlatformConfiguration config)
    {
        if (config.EnvironmentStyle is PlatformEnvironmentStyle.MultiDatabaseNoControl or PlatformEnvironmentStyle.MultiDatabaseWithControl)
        {
            var outboxStores = services.Where(d => d.ServiceType == typeof(IOutboxStore)).ToList();
            if (outboxStores.Count > 0)
            {
                var details = string.Join(", ", outboxStores.Select(DescribeDescriptor));
                throw new InvalidOperationException(
                    $"Direct IOutboxStore registrations are not supported in multi-database configurations. Remove the following registrations and rely on discovery/IOutboxStoreProvider instead: {details}.");
            }

            var inboxStores = services.Where(d => d.ServiceType == typeof(IInboxWorkStore)).ToList();
            if (inboxStores.Count > 0)
            {
                var details = string.Join(", ", inboxStores.Select(DescribeDescriptor));
                throw new InvalidOperationException(
                    $"Direct IInboxWorkStore registrations are not supported in multi-database configurations. Remove the following registrations and rely on discovery/IInboxWorkStoreProvider instead: {details}.");
            }

            var outboxes = services.Where(d => d.ServiceType == typeof(IOutbox)).ToList();
            if (outboxes.Count > 0)
            {
                var details = string.Join(", ", outboxes.Select(DescribeDescriptor));
                throw new InvalidOperationException(
                    $"Direct IOutbox registrations are not supported in multi-database configurations. Use IOutboxRouter via discovery instead. Remove: {details}.");
            }
        }
    }

    private static void RegisterCoreServices(IServiceCollection services, bool enableSchemaDeployment)
    {
        // Register Dapper type handlers for strongly-typed IDs
        PostgresDapperTypeHandlerRegistration.RegisterTypeHandlers();

        // Add time abstractions
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IMonotonicClock, MonotonicClock>();

        // Register schema deployment service if enabled
        if (enableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        // Register all platform features automatically
        // Features will be configured based on environment style (single vs multi-database)
        RegisterPlatformFeatures(services);
    }

    private static void RegisterPlatformFeatures(IServiceCollection services)
    {
        // Get the configuration to determine environment style
        var configDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PlatformConfiguration));
        if (configDescriptor?.ImplementationInstance is not PlatformConfiguration config)
        {
            throw new InvalidOperationException("PlatformConfiguration not found. This should not happen.");
        }

        // Get the discovery service
        var discoveryDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPlatformDatabaseDiscovery));
        if (discoveryDescriptor == null)
        {
            throw new InvalidOperationException("IPlatformDatabaseDiscovery not found. This should not happen.");
        }

        // All platforms use multi-database features
        ValidateMultiDatabaseStoreRegistrations(services, config);
        RegisterMultiDatabaseFeatures(services);
    }

    private static void RegisterMultiDatabaseFeatures(IServiceCollection services)
    {
        // Get configuration to check if schema deployment is enabled
        var configDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(PlatformConfiguration));
        var config = configDescriptor?.ImplementationInstance as PlatformConfiguration;
        var enableSchemaDeployment = config?.EnableSchemaDeployment ?? false;

        // For multi-database, use the multi-database registration methods with platform providers
        // These use store providers that can discover databases dynamically

        // Outbox
        services.AddMultiPostgresOutbox(
            sp => new PlatformOutboxStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                "Outbox",
                enableSchemaDeployment,
                config), // Pass configuration to filter out control plane
            new RoundRobinOutboxSelectionStrategy());

        // Register outbox join store (uses same connection strings as outbox)
        services.TryAddSingleton<IOutboxJoinStore, PostgresOutboxJoinStore>();

        // Register JoinWaitHandler for fan-in orchestration
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxHandler, JoinWaitHandler>());

        // Register multi-outbox cleanup service
        services.AddHostedService<MultiOutboxCleanupService>(sp => new MultiOutboxCleanupService(
            sp.GetRequiredService<IOutboxStoreProvider>(),
            sp.GetRequiredService<IMonotonicClock>(),
            sp.GetRequiredService<ILogger<MultiOutboxCleanupService>>(),
            retentionPeriod: TimeSpan.FromDays(7),
            cleanupInterval: TimeSpan.FromHours(1),
            schemaCompletion: sp.GetService<IDatabaseSchemaCompletion>()));

        // Inbox
        services.AddMultiPostgresInbox(
            sp => new PlatformInboxWorkStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                "Inbox",
                enableSchemaDeployment,
                config), // Pass configuration to filter out control plane
            new RoundRobinInboxSelectionStrategy());

        services.TryAddSingleton<IInboxWorkStore>(ResolveDefaultInboxWorkStore);

        // Register multi-inbox cleanup service
        services.AddHostedService<MultiInboxCleanupService>(sp => new MultiInboxCleanupService(
            sp.GetRequiredService<IInboxWorkStoreProvider>(),
            sp.GetRequiredService<IMonotonicClock>(),
            sp.GetRequiredService<ILogger<MultiInboxCleanupService>>(),
            retentionPeriod: TimeSpan.FromDays(7),
            cleanupInterval: TimeSpan.FromHours(1),
            schemaCompletion: sp.GetService<IDatabaseSchemaCompletion>()));

        // Idempotency
        services.AddPlatformIdempotency(enableSchemaDeployment: enableSchemaDeployment);

        // Scheduler (Timers + Jobs)
        services.AddMultiPostgresScheduler(
            sp => new PlatformSchedulerStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                config), // Pass configuration to filter out control plane
            new RoundRobinOutboxSelectionStrategy());

        RegisterGlobalControlPlaneScheduler(services, config);

        // Leases
        services.AddMultiSystemLeases(
            sp => new PlatformLeaseFactoryProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<ILoggerFactory>(),
                config,
                enableSchemaDeployment));

        // Fanout
        services.AddMultiPostgresFanout(
            sp => new PlatformFanoutRepositoryProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<ILoggerFactory>()));

        // Metrics
        services.AddMetricsExporter(options =>
        {
            options.ServiceName = AppDomain.CurrentDomain.FriendlyName;
            options.SchemaName = config?.ControlPlaneSchemaName ?? "infra";
            if (!string.IsNullOrWhiteSpace(config?.ControlPlaneConnectionString))
            {
                options.CentralConnectionString = config.ControlPlaneConnectionString;
            }
        });
        services.AddMetricsExporterHealthCheck();
    }

    private static void RegisterOutbox(IServiceCollection services, PostgresPlatformOptions options)
    {
        var outboxOptions = new PostgresOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureOutbox?.Invoke(outboxOptions);
        services.AddPostgresOutbox(outboxOptions);
    }

    private static void RegisterInbox(IServiceCollection services, PostgresPlatformOptions options)
    {
        var inboxOptions = new PostgresInboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureInbox?.Invoke(inboxOptions);
        services.AddPostgresInbox(inboxOptions);
    }

    private static void RegisterScheduler(IServiceCollection services, PostgresPlatformOptions options)
    {
        var schedulerOptions = new PostgresSchedulerOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
            EnableBackgroundWorkers = options.EnableSchedulerWorkers,
        };

        options.ConfigureScheduler?.Invoke(schedulerOptions);
#pragma warning disable CS0618 // Intentional: single-connection platform registration uses legacy scheduler wiring.
        services.AddPostgresScheduler(schedulerOptions);
#pragma warning restore CS0618
    }

    private static void RegisterGlobalControlPlaneScheduler(IServiceCollection services, PlatformConfiguration? config)
    {
        if (config?.EnvironmentStyle != PlatformEnvironmentStyle.MultiDatabaseWithControl)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(config.ControlPlaneConnectionString))
        {
            return;
        }

        var schemaName = string.IsNullOrWhiteSpace(config.ControlPlaneSchemaName) ? "infra" : config.ControlPlaneSchemaName;

        services.TryAddSingleton<IGlobalSchedulerStore>(sp =>
        {
            var store = new PostgresSchedulerStore(
                Options.Create(new PostgresSchedulerOptions
                {
                    ConnectionString = config.ControlPlaneConnectionString,
                    SchemaName = schemaName,
                    EnableSchemaDeployment = config.EnableSchemaDeployment,
                }),
                sp.GetRequiredService<TimeProvider>());
            return new PostgresGlobalSchedulerStore(store);
        });

        services.TryAddSingleton<IGlobalSchedulerClient>(sp =>
        {
            var client = new PostgresSchedulerClient(
                Options.Create(new PostgresSchedulerOptions
                {
                    ConnectionString = config.ControlPlaneConnectionString,
                    SchemaName = schemaName,
                    EnableSchemaDeployment = config.EnableSchemaDeployment,
                }),
                sp.GetRequiredService<TimeProvider>());
            return new PostgresGlobalSchedulerClient(client);
        });

        services.TryAddSingleton<IGlobalOutboxStore>(sp =>
        {
            var storeLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PostgresOutboxStore>();
            var store = new PostgresOutboxStore(
                Options.Create(new PostgresOutboxOptions
                {
                    ConnectionString = config.ControlPlaneConnectionString,
                    SchemaName = schemaName,
                    EnableSchemaDeployment = config.EnableSchemaDeployment,
                }),
                sp.GetRequiredService<TimeProvider>(),
                storeLogger);
            return new PostgresGlobalOutboxStore(store);
        });

        services.TryAddSingleton<IGlobalOutbox>(sp =>
        {
            var outboxLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PostgresOutboxService>();
            var outbox = new PostgresOutboxService(
                Options.Create(new PostgresOutboxOptions
                {
                    ConnectionString = config.ControlPlaneConnectionString,
                    SchemaName = schemaName,
                    EnableSchemaDeployment = config.EnableSchemaDeployment,
                }),
                outboxLogger,
                joinStore: null);
            return new PostgresGlobalOutbox(outbox);
        });

        services.TryAddSingleton<IGlobalSystemLeaseFactory>(sp =>
        {
            var leaseLogger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<PostgresLeaseFactory>();
            var leaseFactory = new PostgresLeaseFactory(
                new LeaseFactoryConfig
                {
                    ConnectionString = config.ControlPlaneConnectionString!,
                    SchemaName = schemaName,
                },
                leaseLogger);
            return new PostgresGlobalSystemLeaseFactory(leaseFactory);
        });

        services.TryAddSingleton<GlobalSchedulerDispatcher>();
        services.TryAddSingleton<GlobalOutboxDispatcher>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GlobalSchedulerPollingService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, GlobalOutboxPollingService>());
    }

    private static void RegisterFanout(IServiceCollection services, PostgresPlatformOptions options)
    {
        var fanoutOptions = new PostgresFanoutOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureFanout?.Invoke(fanoutOptions);
        services.AddPostgresFanout(fanoutOptions);
    }

    private static void RegisterIdempotency(IServiceCollection services, PostgresPlatformOptions options)
    {
        var idempotencyOptions = new PostgresIdempotencyOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureIdempotency?.Invoke(idempotencyOptions);
        services.AddPostgresIdempotency(idempotencyOptions);
    }

    private static void RegisterSemaphore(IServiceCollection services, PostgresPlatformOptions options)
    {
        services.AddSemaphoreServices(
            options.ConnectionString,
            options.SchemaName,
            options.ConfigureSemaphore);
    }

    private static void RegisterMetrics(IServiceCollection services, PostgresPlatformOptions options)
    {
        services.AddMetricsExporter(metrics =>
        {
            metrics.SchemaName = options.SchemaName;
            options.ConfigureMetrics?.Invoke(metrics);
        });
        services.AddMetricsExporterHealthCheck();
    }

    private static void RegisterAudit(IServiceCollection services, PostgresPlatformOptions options)
    {
        var auditOptions = new PostgresAuditOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureAudit?.Invoke(auditOptions);
        services.AddPostgresAudit(auditOptions);
    }

    private static void RegisterOperations(IServiceCollection services, PostgresPlatformOptions options)
    {
        var operationOptions = new PostgresOperationOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureOperations?.Invoke(operationOptions);
        services.AddPostgresOperations(operationOptions);
    }

    private static void RegisterEmailOutbox(IServiceCollection services, PostgresPlatformOptions options)
    {
        var emailOutboxOptions = new PostgresEmailOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureEmailOutbox?.Invoke(emailOutboxOptions);
        services.AddPostgresEmailOutbox(emailOutboxOptions);
    }

    private static void RegisterEmailDelivery(IServiceCollection services, PostgresPlatformOptions options)
    {
        var emailDeliveryOptions = new PostgresEmailDeliveryOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            EnableSchemaDeployment = options.EnableSchemaDeployment,
        };

        options.ConfigureEmailDelivery?.Invoke(emailDeliveryOptions);
        services.AddPostgresEmailDelivery(emailDeliveryOptions);
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

    private static IInboxWorkStore ResolveDefaultInboxWorkStore(IServiceProvider provider)
    {
        var storeProvider = provider.GetRequiredService<IInboxWorkStoreProvider>();
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No inbox work stores are configured. Configure at least one store or use IInboxRouter.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple inbox work stores are configured. Resolve IInboxRouter instead of IInboxWorkStore for multi-database setups.");
        }

        return stores[0];
    }
}





