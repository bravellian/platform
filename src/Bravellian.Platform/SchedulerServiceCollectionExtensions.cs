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

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Bravellian.Platform;

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
            services.AddHostedService<OutboxProcessor>();
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

        // Add lease system for outbox processing coordination
        services.AddSystemLeases(new SystemLeaseOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = "dbo", // Use dbo schema for distributed locks
        });

        services.Configure<SqlOutboxOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.TableName = options.TableName;
        });

        services.AddSingleton<IOutbox, SqlOutboxService>();
        services.AddHostedService<OutboxProcessor>();

        // Ensure database schema exists
        Task.Run(async () =>
        {
            try
            {
                await DatabaseSchemaManager.EnsureOutboxSchemaAsync(
                    options.ConnectionString,
                    options.SchemaName,
                    options.TableName).ConfigureAwait(false);
            }
            catch
            {
                // Schema creation errors will be handled during actual operations
                // This is just a best-effort attempt during service registration
            }
        });

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
            SchemaName = "dbo", // Use dbo schema for distributed locks
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
        });

        services.AddSingleton<ISchedulerClient, SqlSchedulerClient>();
        services.AddSingleton<SchedulerHealthCheck>();
        services.AddHostedService<SqlSchedulerService>();

        // Ensure database schema exists
        Task.Run(async () =>
        {
            try
            {
                await DatabaseSchemaManager.EnsureSchedulerSchemaAsync(
                    options.ConnectionString,
                    options.SchemaName,
                    options.JobsTableName,
                    options.JobRunsTableName,
                    options.TimersTableName).ConfigureAwait(false);
            }
            catch
            {
                // Schema creation errors will be handled during actual operations
                // This is just a best-effort attempt during service registration
            }
        });

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
    /// Adds system lease functionality with SQL Server backend.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSystemLeases(this IServiceCollection services, SystemLeaseOptions options)
    {
        // Add time abstractions
        services.AddTimeAbstractions();

        services.Configure<SystemLeaseOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.DefaultLeaseDuration = options.DefaultLeaseDuration;
            o.RenewPercent = options.RenewPercent;
            o.UseGate = options.UseGate;
            o.GateTimeoutMs = options.GateTimeoutMs;
        });

        services.AddSingleton<ISystemLeaseFactory, SqlLeaseFactory>();

        // Ensure database schema exists
        Task.Run(async () =>
        {
            try
            {
                await DatabaseSchemaManager.EnsureDistributedLockSchemaAsync(
                    options.ConnectionString,
                    options.SchemaName).ConfigureAwait(false);
            }
            catch
            {
                // Schema creation failed - this will be retried when the service actually tries to use it
            }
        });

        return services;
    }

    /// <summary>
    /// Adds system lease functionality with SQL Server backend.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="schemaName">The schema name (default: "dbo").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSystemLeases(this IServiceCollection services, string connectionString, string schemaName = "dbo")
    {
        return services.AddSystemLeases(new SystemLeaseOptions
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
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
}
