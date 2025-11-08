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
/// Extension methods for registering lease services with the service collection.
/// </summary>
public static class LeaseServiceCollectionExtensions
{
    /// <summary>
    /// Adds system lease functionality with SQL Server backend.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSystemLeases(this IServiceCollection services, SystemLeaseOptions options)
    {
        // Add time abstractions
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        services.Configure<SystemLeaseOptions>(o =>
        {
            o.ConnectionString = options.ConnectionString;
            o.SchemaName = options.SchemaName;
            o.DefaultLeaseDuration = options.DefaultLeaseDuration;
            o.RenewPercent = options.RenewPercent;
            o.UseGate = options.UseGate;
            o.GateTimeoutMs = options.GateTimeoutMs;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        services.AddSingleton<ISystemLeaseFactory, SqlLeaseFactory>();

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
    /// Adds multi-database lease functionality with support for managing leases across multiple databases.
    /// This enables lease operations across multiple customer databases.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="leaseConfigs">List of lease database configurations, one for each database.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddMultiSystemLeases(
        this IServiceCollection services,
        IEnumerable<LeaseDatabaseConfig> leaseConfigs)
    {
        // Add time abstractions
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        // Register the factory provider with the list of lease configs
        services.AddSingleton<ILeaseFactoryProvider>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ConfiguredLeaseFactoryProvider(leaseConfigs, loggerFactory);
        });

        return services;
    }

    /// <summary>
    /// Adds multi-database lease functionality using a custom factory provider.
    /// This allows for dynamic discovery of lease databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="factoryProviderFactory">Factory function to create the factory provider.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    internal static IServiceCollection AddMultiSystemLeases(
        this IServiceCollection services,
        Func<IServiceProvider, ILeaseFactoryProvider> factoryProviderFactory)
    {
        // Add time abstractions
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        // Register the custom factory provider
        services.AddSingleton(factoryProviderFactory);

        return services;
    }

    /// <summary>
    /// Adds multi-database lease functionality with dynamic database discovery.
    /// This enables automatic detection of new or removed customer databases at runtime.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="refreshInterval">Optional interval for refreshing the database list. Defaults to 5 minutes.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    /// <remarks>
    /// Requires an implementation of ILeaseDatabaseDiscovery to be registered in the service collection.
    /// The discovery service is responsible for querying a registry, database, or configuration service
    /// to get the current list of customer databases.
    /// </remarks>
    public static IServiceCollection AddDynamicMultiSystemLeases(
        this IServiceCollection services,
        TimeSpan? refreshInterval = null)
    {
        // Add time abstractions
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        // Register the dynamic factory provider
        services.AddSingleton<ILeaseFactoryProvider>(provider =>
        {
            var discovery = provider.GetRequiredService<ILeaseDatabaseDiscovery>();
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = provider.GetRequiredService<ILogger<DynamicLeaseFactoryProvider>>();
            return new DynamicLeaseFactoryProvider(discovery, timeProvider, loggerFactory, logger, refreshInterval);
        });

        return services;
    }
}
