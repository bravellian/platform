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

namespace Bravellian.Platform;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Extension methods for unified platform registration.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    private const string PlatformConfigurationKey = "Bravellian.Platform.Configuration.Registered";

    /// <summary>
    /// Registers the platform for a single database environment.
    /// All features (Outbox, Inbox, Timers, Jobs, Fan-out) run against this one database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="databaseName">The logical name for this database (default: "default").</param>
    /// <param name="schemaName">The schema name for platform tables (default: "dbo").</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlatformSingleDatabase(
        this IServiceCollection services,
        string connectionString,
        string databaseName = "default",
        string schemaName = "dbo",
        bool enableSchemaDeployment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);

        // Prevent multiple registrations
        EnsureNotAlreadyRegistered(services);

        var database = new PlatformDatabase
        {
            Name = databaseName,
            ConnectionString = connectionString,
            SchemaName = schemaName,
        };

        // Register configuration
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.SingleDatabase,
            UsesDiscovery = false,
            EnableSchemaDeployment = enableSchemaDeployment,
        };

        services.AddSingleton(configuration);

        // Register list-based discovery with single database
        services.AddSingleton<IPlatformDatabaseDiscovery>(
            new ListBasedDatabaseDiscovery(new[] { database }));

        // Register lifecycle service
        services.AddSingleton<IHostedService, PlatformLifecycleService>();

        // Register core abstractions
        RegisterCoreServices(services, enableSchemaDeployment);


        return services;
    }

    /// <summary>
    /// Registers the platform for a multi-database environment without control plane.
    /// Features run across the provided list of databases using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of application databases.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlatformMultiDatabaseWithList(
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
    public static IServiceCollection AddPlatformMultiDatabaseWithDiscovery(
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
    /// <param name="controlPlaneConnectionString">The connection string for the control plane database.</param>
    /// <param name="enableSchemaDeployment">Whether to automatically create platform tables and procedures at startup.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndList(
        this IServiceCollection services,
        IEnumerable<PlatformDatabase> databases,
        string controlPlaneConnectionString,
        bool enableSchemaDeployment = false)
    {
        ArgumentNullException.ThrowIfNull(databases);
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneConnectionString);

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
            ControlPlaneConnectionString = controlPlaneConnectionString,
            EnableSchemaDeployment = enableSchemaDeployment,
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
    public static IServiceCollection AddPlatformMultiDatabaseWithControlPlaneAndDiscovery(
        this IServiceCollection services,
        string controlPlaneConnectionString,
        bool enableSchemaDeployment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlPlaneConnectionString);

        // Prevent multiple registrations
        EnsureNotAlreadyRegistered(services);

        // Register configuration
        var configuration = new PlatformConfiguration
        {
            EnvironmentStyle = PlatformEnvironmentStyle.MultiDatabaseWithControl,
            UsesDiscovery = true,
            ControlPlaneConnectionString = controlPlaneConnectionString,
            EnableSchemaDeployment = enableSchemaDeployment,
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

    private static void EnsureNotAlreadyRegistered(IServiceCollection services)
    {
        // Check if already registered by looking for PlatformConfiguration
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(PlatformConfiguration));
        if (existing != null)
        {
            throw new InvalidOperationException(
                "Platform registration has already been called. Only one of the five AddPlatform* methods can be used. " +
                "Ensure you call exactly one of: AddPlatformSingleDatabase, AddPlatformMultiDatabaseWithList, " +
                "AddPlatformMultiDatabaseWithDiscovery, AddPlatformMultiDatabaseWithControlPlaneAndList, or " +
                "AddPlatformMultiDatabaseWithControlPlaneAndDiscovery.");
        }
    }

    private static void RegisterCoreServices(IServiceCollection services, bool enableSchemaDeployment)
    {
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

        if (config.EnvironmentStyle == PlatformEnvironmentStyle.SingleDatabase)
        {
            RegisterSingleDatabaseFeatures(services, config, discoveryDescriptor.ImplementationInstance as IPlatformDatabaseDiscovery);
        }
        else
        {
            RegisterMultiDatabaseFeatures(services);
        }
    }

    private static void RegisterSingleDatabaseFeatures(IServiceCollection services, PlatformConfiguration config, IPlatformDatabaseDiscovery? discovery)
    {
        // For single database, get the database info and register features
        if (discovery == null)
        {
            throw new InvalidOperationException("Discovery not available for single database registration.");
        }

        var database = discovery.DiscoverDatabasesAsync().GetAwaiter().GetResult().Single();
        
        // Outbox
        services.AddSqlOutbox(new SqlOutboxOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaName = database.SchemaName,
            TableName = "Outbox",
            EnableSchemaDeployment = config.EnableSchemaDeployment,
        });
        
        // Inbox
        services.AddSqlInbox(new SqlInboxOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaName = database.SchemaName,
            TableName = "Inbox",
            EnableSchemaDeployment = config.EnableSchemaDeployment,
        });
        
        // Scheduler (includes Timers + Jobs + Outbox + Leases)
        services.AddSqlScheduler(new SqlSchedulerOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaName = database.SchemaName,
            EnableSchemaDeployment = config.EnableSchemaDeployment,
        });
        
        // Fanout
        services.AddSqlFanout(new SqlFanoutOptions
        {
            ConnectionString = database.ConnectionString,
            SchemaName = database.SchemaName,
            EnableSchemaDeployment = config.EnableSchemaDeployment,
        });
    }

    private static void RegisterMultiDatabaseFeatures(IServiceCollection services)
    {
        // For multi-database, use the multi-database registration methods with platform providers
        // These use store providers that can discover databases dynamically
        
        // Outbox
        services.AddMultiSqlOutbox(
            sp => new PlatformOutboxStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                "Outbox"),
            new RoundRobinOutboxSelectionStrategy());
        
        // Inbox
        services.AddMultiSqlInbox(
            sp => new PlatformInboxWorkStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                "Inbox"),
            new RoundRobinInboxSelectionStrategy());
        
        // Scheduler (Timers + Jobs)
        services.AddMultiSqlScheduler(
            sp => new PlatformSchedulerStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>()),
            new RoundRobinOutboxSelectionStrategy());
        
        // Leases
        services.AddMultiSystemLeases(
            sp => new PlatformLeaseFactoryProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<ILoggerFactory>()));
        
        // Fanout
        services.AddMultiSqlFanout(
            sp => new PlatformFanoutRepositoryProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<ILoggerFactory>()));
    }
}
