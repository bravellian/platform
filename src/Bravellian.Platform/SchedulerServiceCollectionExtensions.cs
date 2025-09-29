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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
        services.AddSingleton<ISqlDistributedLock, SqlDistributedLock>();

        // 3. Register the health check
        services.AddSingleton<SchedulerHealthCheck>();

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
    public static IServiceCollection AddSqlDistributedLock(this IServiceCollection services, SqlDistributedLockOptions options)
    {
        services.Configure<SqlDistributedLockOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
        });

        services.AddSingleton<ISqlDistributedLock, SqlDistributedLock>();

        return services;
    }

    /// <summary>
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration, used to set the options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlOutbox(this IServiceCollection services, SqlOutboxOptions options)
    {
        services.AddSqlDistributedLock(new SqlDistributedLockOptions
        {
            ConnectionString = options.ConnectionString,
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
        services.AddSqlOutbox(new SqlOutboxOptions
        {
            ConnectionString = options.ConnectionString,
            SchemaName = options.SchemaName,
            TableName = "Outbox" // Keep Outbox table name consistent
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
            TableName = tableName
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
            TimersTableName = timersTableName
        });
    }

    /// <summary>
    /// Adds SQL distributed lock functionality.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSqlDistributedLock(this IServiceCollection services, string connectionString)
    {
        return services.AddSqlDistributedLock(new SqlDistributedLockOptions
        {
            ConnectionString = connectionString
        });
    }
}
