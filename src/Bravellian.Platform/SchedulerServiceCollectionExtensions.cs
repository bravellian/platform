using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        //services.AddOptions<SqlSchedulerOptions>()
        //    .Bind(configuration.GetSection(SqlSchedulerOptions.SectionName))
        //    .ValidateDataAnnotations();

        // 2. Register the public-facing client and internal services as singletons
        services.AddSingleton<ISchedulerClient, SqlSchedulerClient>();
        services.AddSingleton<IOutbox, SqlOutboxService>();
        services.AddSingleton<ISqlDistributedLock, SqlDistributedLock>();

        // 3. Register the health check
        services.AddSingleton<SchedulerHealthCheck>();

        // 4. Conditionally register the background workers
        //var options = configuration.GetSection(SqlSchedulerOptions.SectionName).Get<SqlSchedulerOptions>() ?? new SqlSchedulerOptions();
        //if (options.EnableBackgroundWorkers)
        {
            services.AddHostedService<SqlSchedulerService>();
            services.AddHostedService<OutboxProcessor>();
        }

        return services;
    }


    /// <summary>
    // Adds the scheduler health check to the health check system.
    /// </summary>
    /// <param name="builder">The IHealthChecksBuilder to add the check to.</param>
    /// <param name="name">The name of the health check. Defaults to "sql_scheduler".</param>
    /// <param name="failureStatus">The HealthStatus that should be reported when the check fails.</param>
    /// <param name="tags">A list of tags that can be used to filter sets of health checks.</param>
    /// <returns>The IHealthChecksBuilder so that additional calls can be chained.</returns>
    //public static IHealthChecksBuilder AddSqlSchedulerHealthCheck(
    //    this IHealthChecksBuilder builder,
    //    string name = "sql_scheduler",
    //    HealthStatus? failureStatus = null,
    //    IEnumerable<string>? tags = null)
    //{
    //    // The health check system will resolve SchedulerHealthCheck from the DI container
    //    // where we registered it in AddSqlScheduler.
    //    return builder.AddCheck<SchedulerHealthCheck>(name, failureStatus, tags ?? new[] { "database", "scheduler" });
    //}
}
