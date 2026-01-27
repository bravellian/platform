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


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform;

/// <summary>
/// Service collection extensions for Postgres scheduler, outbox, and fanout services.
/// </summary>
public static class PostgresSchedulerServiceCollectionExtensions
{
    private static readonly string[] SchedulerHealthCheckTags = { "database", "scheduler" };

    /// <summary>   
    /// Adds SQL outbox functionality to the service collection using the specified options.
    /// Configures outbox options, registers multi-outbox infrastructure, cleanup and schema deployment services as needed.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration, used to set the options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostgresOutbox(this IServiceCollection services, PostgresOutboxOptions options)
    {
        var validator = new PostgresOutboxOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<PostgresOutboxOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresOutboxOptions>>(validator));

        services.Configure<PostgresOutboxOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
            o.RetentionPeriod = options.RetentionPeriod;
            o.EnableAutomaticCleanup = options.EnableAutomaticCleanup;
            o.CleanupInterval = options.CleanupInterval;
        });

        // Use the multi-outbox infrastructure even for single-database setups so DI only contains global abstractions.
        services.AddMultiPostgresOutbox(new[] { options });

        // Register cleanup service if enabled
        if (options.EnableAutomaticCleanup)
        {
            services.AddHostedService(sp => new MultiOutboxCleanupService(
                sp.GetRequiredService<IOutboxStoreProvider>(),
                sp.GetRequiredService<IMonotonicClock>(),
                sp.GetRequiredService<ILogger<MultiOutboxCleanupService>>(),
                options.RetentionPeriod,
                options.CleanupInterval,
                sp.GetService<IDatabaseSchemaCompletion>()));
        }

        // Register schema deployment service if enabled (only register once per service collection)
        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());

            // Only add hosted service if not already registered using TryAddEnumerable
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds the SQL outbox functionality to the service collection.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration, used to set the options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    [Obsolete("This method uses a hardcoded connection string and creates its own lease factories, bypassing dynamic discovery. Use AddPostgresPlatformMultiDatabaseWithDiscovery or AddPostgresPlatformMultiDatabaseWithList instead to ensure all databases go through IPlatformDatabaseDiscovery.")]
    public static IServiceCollection AddPostgresScheduler(this IServiceCollection services, PostgresSchedulerOptions options)
    {
        var validator = new PostgresSchedulerOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        // Add time abstractions
        services.AddTimeAbstractions();

        services.AddPostgresOutbox(new PostgresOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            TableName = "Outbox", // Keep Outbox table name consistent
        });

        // Add lease system for scheduler processing coordination (single database)
        services.AddSystemLeases(options.ConnectionString, options.SchemaName);

        services.Configure<PostgresSchedulerOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.JobsTableName = options.JobsTableName;
            o.JobRunsTableName = options.JobRunsTableName;
            o.TimersTableName = options.TimersTableName;
            o.MaxPollingInterval = options.MaxPollingInterval;
            o.EnableBackgroundWorkers = options.EnableBackgroundWorkers;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddOptions<PostgresSchedulerOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresSchedulerOptions>>(validator));

        // Expose the configured options instance directly for consumers that depend on the concrete type.
        services.TryAddSingleton(sp => sp.GetRequiredService<IOptions<PostgresSchedulerOptions>>().Value);

        services.AddSingleton<ISchedulerClient, PostgresSchedulerClient>();
        services.AddSingleton<SchedulerHealthCheck>();
        services.AddHostedService<PostgresSchedulerService>();

        // Register schema deployment service if enabled (only register once per service collection)
        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());

            // Only add hosted service if not already registered using TryAddEnumerable
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds the scheduler health check to the health check system.
    /// </summary>
    /// <param name="builder">The IHealthChecksBuilder to add the check to.</param>
    /// <param name="name">The name of the health check. Defaults to "sql_scheduler".</param>
    /// <param name="failureStatus">The HealthStatus that should be reported when the check fails.</param>
    /// <param name="tags">A list of tags that can be used to filter sets of health checks.</param>
    /// <returns>The IHealthChecksBuilder so that additional calls can be chained.</returns>
    public static IHealthChecksBuilder AddPostgresSchedulerHealthCheck(
       this IHealthChecksBuilder builder,
       string name = "postgres_scheduler",
       HealthStatus? failureStatus = null,
       IEnumerable<string>? tags = null)
    {
        // The health check system will resolve SchedulerHealthCheck from the DI container
        // where we registered it in AddPostgresScheduler.
        return builder.AddCheck<SchedulerHealthCheck>(name, failureStatus, tags ?? SchedulerHealthCheckTags);
    }

    /// <summary>
    /// Adds SQL outbox functionality with custom schema and table names.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The database schema name (default: "infra").</param>
    /// <param name="tableName">The outbox table name (default: "Outbox").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostgresOutbox(this IServiceCollection services, string connectionString, string schemaName = "infra", string tableName = "Outbox")
    {
        return services.AddPostgresOutbox(new PostgresOutboxOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            TableName = tableName,
        });
    }

    /// <summary>
    /// Adds time abstractions including TimeProvider and monotonic clock for the platform.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="timeProvider">Optional custom TimeProvider. If null, TimeProvider.System is used.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddTimeAbstractions(this IServiceCollection services, TimeProvider? timeProvider = null)
    {
        services.AddSingleton(timeProvider ?? TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        return services;
    }

    /// <summary>
    /// Adds SQL fanout functionality with SQL Server backend.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostgresFanout(this IServiceCollection services, PostgresFanoutOptions options)
    {
        var validator = new PostgresFanoutOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<PostgresFanoutOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresFanoutOptions>>(validator));

        // Add time abstractions
        services.AddTimeAbstractions();

        services.Configure<PostgresFanoutOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.PolicyTableName = options.PolicyTableName;
            o.CursorTableName = options.CursorTableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddSingleton<IFanoutPolicyRepository, PostgresFanoutPolicyRepository>();
        services.AddSingleton<IFanoutCursorRepository, PostgresFanoutCursorRepository>();
        services.AddSingleton<IFanoutDispatcher, FanoutDispatcher>();

        // Register the fanout job handler
        services.AddTransient<IOutboxHandler, FanoutJobHandler>();

        // Register schema deployment service if enabled (only register once per service collection)
        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());

            // Only add hosted service if not already registered using TryAddEnumerable
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds SQL fanout functionality with custom schema and table names.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The database schema name (default: "infra").</param>
    /// <param name="policyTableName">The policy table name (default: "FanoutPolicy").</param>
    /// <param name="cursorTableName">The cursor table name (default: "FanoutCursor").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostgresFanout(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "infra",
        string policyTableName = "FanoutPolicy",
        string cursorTableName = "FanoutCursor")
    {
        return services.AddPostgresFanout(new PostgresFanoutOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            PolicyTableName = policyTableName,
            CursorTableName = cursorTableName,
        });
    }

    /// <summary>
    /// Registers a fanout topic with its planner implementation and scheduling options.
    /// Creates a recurring job that coordinates fanout processing for the topic.
    /// </summary>
    /// <typeparam name="TPlanner">The fanout planner implementation type.</typeparam>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The topic configuration and scheduling options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddFanoutTopic<TPlanner>(
        this IServiceCollection services,
        FanoutTopicOptions options)
        where TPlanner : class, IFanoutPlanner
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.FanoutTopic))
        {
            throw new ArgumentException("FanoutTopic must be provided.", nameof(options));
        }

        // Register the planner for this topic (scoped to allow for stateful planners)
        services.AddScoped<TPlanner>();

        // Register a keyed scoped service for this specific topic/workkey combination
        var key = options.WorkKey is null ? options.FanoutTopic : $"{options.FanoutTopic}:{options.WorkKey}";
        services.AddKeyedScoped<IFanoutPlanner, TPlanner>(key);

        // Register the coordinator for this topic
        services.AddKeyedScoped<IFanoutCoordinator>(key, (provider, key) =>
        {
            var planner = provider.GetRequiredKeyedService<IFanoutPlanner>(key);
            var dispatcher = provider.GetRequiredService<IFanoutDispatcher>();
            var leaseFactory = provider.GetRequiredService<ISystemLeaseFactory>();
            var logger = provider.GetRequiredService<ILogger<FanoutCoordinator>>();

            return new FanoutCoordinator(planner, dispatcher, leaseFactory, logger);
        });

        // Register the recurring job with the scheduler using a hosted service
        services.AddSingleton<IHostedService>(provider => new FanoutJobRegistrationService(provider, options));

        return services;
    }

    /// <summary>
    /// Adds SQL inbox functionality for at-most-once message processing.
    /// Configures inbox options, registers multi-inbox infrastructure, cleanup and schema deployment services as needed.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration, used to set the options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostgresInbox(this IServiceCollection services, PostgresInboxOptions options)
    {
        var validator = new PostgresInboxOptionsValidator();
        OptionsValidationHelper.ValidateAndThrow(options, validator);

        services.AddOptions<PostgresInboxOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PostgresInboxOptions>>(validator));

        services.Configure<PostgresInboxOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
            o.RetentionPeriod = options.RetentionPeriod;
            o.EnableAutomaticCleanup = options.EnableAutomaticCleanup;
            o.CleanupInterval = options.CleanupInterval;
        });

        // Use the multi-inbox infrastructure even for single-database setups so DI only contains global abstractions.
        services.AddMultiPostgresInbox(new[] { options });

        // Back-compat convenience: expose the sole inbox instance when exactly one store is configured.
        services.TryAddSingleton<IInbox>(provider => ResolveDefaultInbox(provider));

        // Register cleanup service if enabled
        if (options.EnableAutomaticCleanup)
        {
            services.AddHostedService(sp => new MultiInboxCleanupService(
                sp.GetRequiredService<IInboxWorkStoreProvider>(),
                sp.GetRequiredService<IMonotonicClock>(),
                sp.GetRequiredService<ILogger<MultiInboxCleanupService>>(),
                options.RetentionPeriod,
                options.CleanupInterval,
                sp.GetService<IDatabaseSchemaCompletion>()));
        }

        // Register schema deployment service if enabled (only register once per service collection)
        if (options.EnableSchemaDeployment)
        {
            services.TryAddSingleton<DatabaseSchemaCompletion>();
            services.TryAddSingleton<IDatabaseSchemaCompletion>(provider => provider.GetRequiredService<DatabaseSchemaCompletion>());

            // Only add hosted service if not already registered using TryAddEnumerable
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseSchemaBackgroundService>());
        }

        return services;
    }

    /// <summary>
    /// Adds SQL inbox functionality with custom schema and table names.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The database schema name (default: "infra").</param>
    /// <param name="tableName">The inbox table name (default: "Inbox").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddPostgresInbox(this IServiceCollection services, string connectionString, string schemaName = "infra", string tableName = "Inbox")
    {
        return services.AddPostgresInbox(new PostgresInboxOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            TableName = tableName,
        });
    }

    /// <summary>
    /// Adds SQL multi-inbox functionality with support for processing messages across multiple databases.
    /// This enables a single worker to process inbox messages from multiple customer databases.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="inboxOptions">List of inbox options, one for each database to poll.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinInboxSelectionStrategy.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddMultiPostgresInbox(
        this IServiceCollection services,
        IEnumerable<PostgresInboxOptions> inboxOptions,
        IInboxSelectionStrategy? selectionStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(inboxOptions);

        var validator = new PostgresInboxOptionsValidator();
        foreach (var option in inboxOptions)
        {
            OptionsValidationHelper.ValidateAndThrow(option, validator);
        }

        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the store provider with the list of inbox options
        services.AddSingleton<IInboxWorkStoreProvider>(provider =>
        {
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredInboxWorkStoreProvider(inboxOptions, timeProvider, loggerFactory);
        });

        // Register the selection strategy
        services.AddSingleton<IInboxSelectionStrategy>(selectionStrategy ?? new RoundRobinInboxSelectionStrategy());

        // Register shared components
        services.AddSingleton<IInboxHandlerResolver, InboxHandlerResolver>();
        services.AddSingleton<MultiInboxDispatcher>();
        services.AddHostedService<MultiInboxPollingService>();

        // Register the inbox router for write operations
        services.AddSingleton<IInboxRouter, InboxRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-inbox functionality using a custom store provider.
    /// This allows for dynamic discovery of inbox databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="storeProviderFactory">Factory function to create the store provider.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinInboxSelectionStrategy.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    internal static IServiceCollection AddMultiPostgresInbox(
        this IServiceCollection services,
        Func<IServiceProvider, IInboxWorkStoreProvider> storeProviderFactory,
        IInboxSelectionStrategy? selectionStrategy = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the custom store provider
        services.AddSingleton(storeProviderFactory);

        // Register the selection strategy
        services.AddSingleton<IInboxSelectionStrategy>(selectionStrategy ?? new RoundRobinInboxSelectionStrategy());

        // Register shared components
        services.AddSingleton<IInboxHandlerResolver, InboxHandlerResolver>();
        services.AddSingleton<MultiInboxDispatcher>();
        services.AddHostedService<MultiInboxPollingService>();

        // Register the inbox router for write operations
        services.AddSingleton<IInboxRouter, InboxRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-inbox functionality with dynamic database discovery.
    /// This enables automatic detection of new or removed customer databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinInboxSelectionStrategy.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    /// <remarks>
    /// Requires an implementation of IInboxDatabaseDiscovery to be registered in the service collection.
    /// The discovery service is responsible for querying a registry, database, or configuration service
    /// to get the current list of customer databases.
    /// </remarks>
    public static IServiceCollection AddDynamicMultiSqlInbox(
        this IServiceCollection services,
        IInboxSelectionStrategy? selectionStrategy = null,
        TimeSpan? refreshInterval = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the dynamic store provider
        services.AddSingleton<IInboxWorkStoreProvider>(provider =>
        {
            var discovery = provider.GetRequiredService<IInboxDatabaseDiscovery>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = provider.GetRequiredService<ILogger<DynamicInboxWorkStoreProvider>>();
            return new DynamicInboxWorkStoreProvider(discovery, timeProvider, loggerFactory, logger, refreshInterval);
        });

        // Register the selection strategy
        services.AddSingleton<IInboxSelectionStrategy>(selectionStrategy ?? new RoundRobinInboxSelectionStrategy());

        // Register shared components
        services.AddSingleton<IInboxHandlerResolver, InboxHandlerResolver>();
        services.AddSingleton<MultiInboxDispatcher>();
        services.AddHostedService<MultiInboxPollingService>();

        // Register the inbox router for write operations
        services.AddSingleton<IInboxRouter, InboxRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-scheduler functionality using a custom store provider.
    /// This allows for dynamic discovery of scheduler databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="storeProviderFactory">Factory function to create the store provider.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinOutboxSelectionStrategy.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddMultiPostgresScheduler(
        this IServiceCollection services,
        Func<IServiceProvider, ISchedulerStoreProvider> storeProviderFactory,
        IOutboxSelectionStrategy? selectionStrategy = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the custom store provider
        services.AddSingleton(storeProviderFactory);

        // Register the selection strategy
        services.AddSingleton<IOutboxSelectionStrategy>(selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());

        // Note: Caller must register ILeaseFactoryProvider separately since we can't determine
        // connection strings from a factory function
        services.TryAddSingleton<ILeaseRouter>(provider =>
        {
            var factoryProvider = provider.GetRequiredService<ILeaseFactoryProvider>();
            var logger = provider.GetRequiredService<ILogger<LeaseRouter>>();
            return new LeaseRouter(factoryProvider, logger);
        });

        services.TryAddSingleton<ISystemLeaseFactory>(provider =>
            provider.GetRequiredService<ILeaseRouter>().GetDefaultLeaseFactoryAsync().ConfigureAwait(false).GetAwaiter().GetResult());

        // Register shared components
        services.AddSingleton<MultiSchedulerDispatcher>();
        services.AddHostedService<MultiSchedulerPollingService>();

        // Register the scheduler router for write operations
        services.AddSingleton<ISchedulerRouter, SchedulerRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-scheduler functionality with dynamic database discovery.
    /// This enables automatic detection of new or removed customer databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinOutboxSelectionStrategy.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    /// <remarks>
    /// Requires an implementation of ISchedulerDatabaseDiscovery to be registered in the service collection.
    /// The discovery service is responsible for querying a registry, database, or configuration service
    /// to get the current list of customer databases.
    /// </remarks>
    public static IServiceCollection AddDynamicMultiSqlScheduler(
        this IServiceCollection services,
        IOutboxSelectionStrategy? selectionStrategy = null,
        TimeSpan? refreshInterval = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the dynamic store provider
        services.AddSingleton<ISchedulerStoreProvider>(provider =>
        {
            var discovery = provider.GetRequiredService<ISchedulerDatabaseDiscovery>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = provider.GetRequiredService<ILogger<DynamicSchedulerStoreProvider>>();
            return new DynamicSchedulerStoreProvider(discovery, timeProvider, loggerFactory, logger, refreshInterval);
        });

        // Register the selection strategy
        services.AddSingleton<IOutboxSelectionStrategy>(selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());

        // Note: Caller must register ISystemLeaseFactory separately since we can't determine
        // connection string from dynamic discovery

        // Register shared components
        services.AddSingleton<MultiSchedulerDispatcher>();
        services.AddHostedService<MultiSchedulerPollingService>();

        // Register the scheduler router for write operations
        services.AddSingleton<ISchedulerRouter, SchedulerRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-outbox functionality with support for processing messages across multiple databases.
    /// This enables a single worker to process outbox messages from multiple customer databases.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="outboxOptions">List of outbox options, one for each database to poll.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinOutboxSelectionStrategy.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddMultiPostgresOutbox(
        this IServiceCollection services,
        IEnumerable<PostgresOutboxOptions> outboxOptions,
        IOutboxSelectionStrategy? selectionStrategy = null)
    {
        ArgumentNullException.ThrowIfNull(outboxOptions);

        var validator = new PostgresOutboxOptionsValidator();
        foreach (var option in outboxOptions)
        {
            OptionsValidationHelper.ValidateAndThrow(option, validator);
        }

        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the store provider with the list of outbox options
        services.AddSingleton<IOutboxStoreProvider>(provider =>
        {
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredOutboxStoreProvider(outboxOptions, timeProvider, loggerFactory);
        });

        // Register the selection strategy
        services.AddSingleton<IOutboxSelectionStrategy>(selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());

        // Register shared components
        services.AddSingleton<IOutboxHandlerResolver, OutboxHandlerResolver>();
        services.AddSingleton<MultiOutboxDispatcher>();
        services.AddHostedService<MultiOutboxPollingService>();

        // Register the outbox router for write operations
        services.AddSingleton<IOutboxRouter, OutboxRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-outbox functionality using a custom store provider.
    /// This allows for dynamic discovery of outbox databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="storeProviderFactory">Factory function to create the store provider.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinOutboxSelectionStrategy.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    internal static IServiceCollection AddMultiPostgresOutbox(
        this IServiceCollection services,
        Func<IServiceProvider, IOutboxStoreProvider> storeProviderFactory,
        IOutboxSelectionStrategy? selectionStrategy = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the custom store provider
        services.AddSingleton(storeProviderFactory);

        // Register the selection strategy
        services.AddSingleton<IOutboxSelectionStrategy>(selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());

        // Register shared components
        services.AddSingleton<IOutboxHandlerResolver, OutboxHandlerResolver>();
        services.AddSingleton<MultiOutboxDispatcher>();
        services.AddHostedService<MultiOutboxPollingService>();

        // Register the outbox router for write operations
        services.AddSingleton<IOutboxRouter, OutboxRouter>();

        return services;
    }

    /// <summary>
    /// Adds SQL multi-outbox functionality with dynamic database discovery.
    /// This enables automatic detection of new or removed customer databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinOutboxSelectionStrategy.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    /// <remarks>
    /// Requires an implementation of IOutboxDatabaseDiscovery to be registered in the service collection.
    /// The discovery service is responsible for querying a registry, database, or configuration service
    /// to get the current list of customer databases.
    /// </remarks>
    public static IServiceCollection AddDynamicMultiSqlOutbox(
        this IServiceCollection services,
        IOutboxSelectionStrategy? selectionStrategy = null,
        TimeSpan? refreshInterval = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the dynamic store provider
        services.AddSingleton<IOutboxStoreProvider>(provider =>
        {
            var discovery = provider.GetRequiredService<IOutboxDatabaseDiscovery>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = provider.GetRequiredService<ILogger<DynamicOutboxStoreProvider>>();
            return new DynamicOutboxStoreProvider(discovery, timeProvider, loggerFactory, logger, refreshInterval);
        });

        // Register the selection strategy
        services.AddSingleton<IOutboxSelectionStrategy>(selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());

        // Register shared components
        services.AddSingleton<IOutboxHandlerResolver, OutboxHandlerResolver>();
        services.AddSingleton<MultiOutboxDispatcher>();
        services.AddHostedService<MultiOutboxPollingService>();

        // Register the outbox router for write operations
        services.AddSingleton<IOutboxRouter, OutboxRouter>();

        return services;
    }

    /// <summary>
    /// Resolves the single configured outbox, throwing if multiple stores exist to force callers to use routing.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <returns>The default outbox.</returns>
    /// <exception cref="InvalidOperationException">Thrown when zero or multiple stores are registered.</exception>
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

    /// <summary>
    /// Resolves the single configured inbox, throwing if multiple stores exist to force callers to use routing.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <returns>The default inbox.</returns>
    /// <exception cref="InvalidOperationException">Thrown when zero or multiple stores are registered.</exception>
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
}





