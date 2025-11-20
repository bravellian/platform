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

using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public static class SchedulerServiceCollectionExtensions
{
    /// <summary>
    /// Registers all required services for the SQL-based scheduler, outbox, and timer system.
    /// This includes the client, background workers, and distributed lock implementation.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="configuration">The application configuration, used to bind SqlSchedulerOptions.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlScheduler(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure and validate the options class
        // services.AddOptions<SqlSchedulerOptions>()
        //    .Bind(configuration.GetSection(SqlSchedulerOptions.SectionName))
        //    .ValidateDataAnnotations();

        // 2. Register the public-facing client and internal services as singletons
        services.AddSingleton<ISchedulerClient, SqlSchedulerClient>();
        services.AddSingleton<IOutbox, SqlOutboxService>();

        // 3. Register the health check
        // Note: This method requires configuration to be properly set up
        // Use AddSqlScheduler(SqlSchedulerOptions) overload instead
        // services.AddSingleton<SchedulerHealthCheck>();

        // 4. Conditionally register the background workers
        // var options = configuration.GetSection(SqlSchedulerOptions.SectionName).Get<SqlSchedulerOptions>() ?? new SqlSchedulerOptions();
        // if (options.EnableBackgroundWorkers)
        {
            services.AddHostedService<SqlSchedulerService>();
        }

        return services;
    }

    /// <summary>
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration, used to set the options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlOutbox(this IServiceCollection services, SqlOutboxOptions options)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        services.Configure<SqlOutboxOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
            o.RetentionPeriod = options.RetentionPeriod;
            o.EnableAutomaticCleanup = options.EnableAutomaticCleanup;
            o.CleanupInterval = options.CleanupInterval;
        });

        services.AddSingleton<IOutbox, SqlOutboxService>();
        services.AddSingleton<IOutboxStore, SqlOutboxStore>();
        services.AddSingleton<IOutboxHandlerResolver, OutboxHandlerResolver>();
        services.AddSingleton<OutboxDispatcher>();
        services.AddHostedService<OutboxPollingService>();

        // Register cleanup service if enabled
        if (options.EnableAutomaticCleanup)
        {
            services.AddHostedService<OutboxCleanupService>();
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
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration, used to set the options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlScheduler(this IServiceCollection services, SqlSchedulerOptions options)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        services.AddSqlOutbox(new SqlOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            TableName = "Outbox", // Keep Outbox table name consistent
        });

        // Add lease system for scheduler processing coordination
        services.AddSystemLeases(new SystemLeaseOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
        });

        services.Configure<SqlSchedulerOptions>(o =>
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

        services.AddSingleton<ISchedulerClient, SqlSchedulerClient>();
        services.AddSingleton<SchedulerHealthCheck>();
        services.AddHostedService<SqlSchedulerService>();

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
    public static IHealthChecksBuilder AddSqlSchedulerHealthCheck(
       this IHealthChecksBuilder builder,
       string name = "sql_scheduler",
       HealthStatus? failureStatus = null,
       IEnumerable<string>? tags = null)
    {
        // The health check system will resolve SchedulerHealthCheck from the DI container
        // where we registered it in AddSqlScheduler.
        return builder.AddCheck<SchedulerHealthCheck>(name, failureStatus, tags ?? new[] { "database", "scheduler" });
    }

    /// <summary>
    /// Adds SQL outbox functionality with custom schema and table names.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The database schema name (default: "dbo").</param>
    /// <param name="tableName">The outbox table name (default: "Outbox").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlOutbox(this IServiceCollection services, string connectionString, string schemaName = "dbo", string tableName = "Outbox")
    {
        return services.AddSqlOutbox(new SqlOutboxOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            TableName = tableName,
        });
    }

    /// <summary>
    /// Adds SQL scheduler functionality with custom schema and table names.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The database schema name (default: "dbo").</param>
    /// <param name="jobsTableName">The jobs table name (default: "Jobs").</param>
    /// <param name="jobRunsTableName">The job runs table name (default: "JobRuns").</param>
    /// <param name="timersTableName">The timers table name (default: "Timers").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlScheduler(this IServiceCollection services, string connectionString, string schemaName = "dbo", string jobsTableName = "Jobs", string jobRunsTableName = "JobRuns", string timersTableName = "Timers")
    {
        return services.AddSqlScheduler(new SqlSchedulerOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            JobsTableName = jobsTableName,
            JobRunsTableName = jobRunsTableName,
            TimersTableName = timersTableName,
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
    /// Registers an outbox handler for a specific topic.
    /// </summary>
    /// <typeparam name="THandler">The outbox handler implementation type.</typeparam>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services)
        where THandler : class, IOutboxHandler
    {
        services.AddSingleton<IOutboxHandler, THandler>();
        return services;
    }

    /// <summary>
    /// Registers an outbox handler using a factory function.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="factory">Factory function to create the handler instance.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddOutboxHandler(this IServiceCollection services, Func<IServiceProvider, IOutboxHandler> factory)
    {
        services.AddSingleton(factory);
        return services;
    }

    /// <summary>
    /// Registers an inbox handler for a specific topic.
    /// </summary>
    /// <typeparam name="THandler">The inbox handler implementation type.</typeparam>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddInboxHandler<THandler>(this IServiceCollection services)
        where THandler : class, IInboxHandler
    {
        services.AddSingleton<IInboxHandler, THandler>();
        return services;
    }

    /// <summary>
    /// Registers an inbox handler using a factory function.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="factory">Factory function to create the handler instance.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddInboxHandler(this IServiceCollection services, Func<IServiceProvider, IInboxHandler> factory)
    {
        services.AddSingleton(factory);
        return services;
    }

    /// <summary>
    /// Adds SQL fanout functionality with SQL Server backend.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlFanout(this IServiceCollection services, SqlFanoutOptions options)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        services.Configure<SqlFanoutOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.PolicyTableName = options.PolicyTableName;
            o.CursorTableName = options.CursorTableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddSingleton<IFanoutPolicyRepository, SqlFanoutPolicyRepository>();
        services.AddSingleton<IFanoutCursorRepository, SqlFanoutCursorRepository>();
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
    /// <param name="schemaName">The database schema name (default: "dbo").</param>
    /// <param name="policyTableName">The policy table name (default: "FanoutPolicy").</param>
    /// <param name="cursorTableName">The cursor table name (default: "FanoutCursor").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlFanout(
        this IServiceCollection services,
        string connectionString,
        string schemaName = "dbo",
        string policyTableName = "FanoutPolicy",
        string cursorTableName = "FanoutCursor")
    {
        return services.AddSqlFanout(new SqlFanoutOptions
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
        ArgumentException.ThrowIfNullOrWhiteSpace(options.FanoutTopic);

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

    // Adds SQL inbox functionality for at-most-once message processing.
    // </summary>
    // <param name="services">The IServiceCollection to add services to.</param>
    // <param name="options">The configuration, used to set the options.</param>
    // <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlInbox(this IServiceCollection services, SqlInboxOptions options)
    {
        services.Configure<SqlInboxOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
            o.RetentionPeriod = options.RetentionPeriod;
            o.EnableAutomaticCleanup = options.EnableAutomaticCleanup;
            o.CleanupInterval = options.CleanupInterval;
        });

        services.AddSingleton<IInbox, SqlInboxService>();
        services.AddSingleton<IInboxWorkStore, SqlInboxWorkStore>();
        services.AddSingleton<IInboxHandlerResolver, InboxHandlerResolver>();
        services.AddSingleton<InboxDispatcher>();
        services.AddHostedService<InboxPollingService>();

        // Register cleanup service if enabled
        if (options.EnableAutomaticCleanup)
        {
            services.AddHostedService<InboxCleanupService>();
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
    /// <param name="schemaName">The database schema name (default: "dbo").</param>
    /// <param name="tableName">The inbox table name (default: "Inbox").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlInbox(this IServiceCollection services, string connectionString, string schemaName = "dbo", string tableName = "Inbox")
    {
        return services.AddSqlInbox(new SqlInboxOptions
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
    public static IServiceCollection AddMultiSqlInbox(
        this IServiceCollection services,
        IEnumerable<SqlInboxOptions> inboxOptions,
        IInboxSelectionStrategy? selectionStrategy = null)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the store provider with the list of inbox options
        services.AddSingleton<IInboxWorkStoreProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredInboxWorkStoreProvider(inboxOptions, loggerFactory);
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
    internal static IServiceCollection AddMultiSqlInbox(
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
    /// Adds SQL multi-scheduler functionality with support for processing scheduler work across multiple databases.
    /// This enables a single worker to process scheduler work from multiple customer databases.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="schedulerOptions">List of scheduler options, one for each database to poll.</param>
    /// <param name="selectionStrategy">Optional selection strategy. Defaults to RoundRobinOutboxSelectionStrategy.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddMultiSqlScheduler(
        this IServiceCollection services,
        IEnumerable<SchedulerDatabaseConfig> schedulerOptions,
        IOutboxSelectionStrategy? selectionStrategy = null)
    {
        var schedulerOptionsList = schedulerOptions.ToList();

        // Add time abstractions
        services.AddTimeAbstractions();

        // Register the store provider with the list of scheduler options
        services.AddSingleton<ISchedulerStoreProvider>(provider =>
        {
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredSchedulerStoreProvider(schedulerOptionsList, timeProvider, loggerFactory);
        });

        // Register the selection strategy
        services.AddSingleton<IOutboxSelectionStrategy>(selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());

        // Register lease factories per scheduler database so work runs against the correct tenant database
        var leaseConfigs = schedulerOptionsList.Select(o => new LeaseDatabaseConfig
        {
            Identifier = o.Identifier,
            ConnectionString = o.ConnectionString,
            SchemaName = o.SchemaName,
        }).ToList();

        services.TryAddSingleton<ILeaseFactoryProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredLeaseFactoryProvider(leaseConfigs, loggerFactory);
        });

        // Add system leases - we need at least one lease factory for multi-scheduler
        // Use the first scheduler's connection for the lease system
        var firstScheduler = schedulerOptionsList.FirstOrDefault();
        if (firstScheduler == null)
        {
            throw new InvalidOperationException("At least one scheduler must be configured. The schedulerOptions collection is empty.");
        }

        services.AddSystemLeases(new SystemLeaseOptions
        {
            ConnectionString = firstScheduler.ConnectionString,
            SchemaName = firstScheduler.SchemaName,
        });

        // Register shared components
        services.AddSingleton<MultiSchedulerDispatcher>();
        services.AddHostedService<MultiSchedulerPollingService>();

        // Register the scheduler router for write operations
        services.AddSingleton<ISchedulerRouter, SchedulerRouter>();

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
    public static IServiceCollection AddMultiSqlScheduler(
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
    public static IServiceCollection AddMultiSqlOutbox(
        this IServiceCollection services,
        IEnumerable<SqlOutboxOptions> outboxOptions,
        IOutboxSelectionStrategy? selectionStrategy = null)
    {
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
    internal static IServiceCollection AddMultiSqlOutbox(
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
}
