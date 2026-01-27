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


using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// Extension methods for registering lease services with the service collection.
/// </summary>
internal static class LeaseServiceCollectionExtensions
{
    /// <summary>
    /// Adds system lease functionality with SQL Server backend.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="options">The configuration options.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    public static IServiceCollection AddSystemLeases(this IServiceCollection services, SystemLeaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

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

        // Require an existing lease factory provider (e.g., PlatformLeaseFactoryProvider or a configured/dynamic provider).
        if (!services.Any(d => d.ServiceType == typeof(ILeaseFactoryProvider)))
        {
            throw new InvalidOperationException(
                "No ILeaseFactoryProvider is registered. Register lease factories (e.g., AddPlatform* or AddMultiSystemLeases) before calling AddSystemLeases.");
        }

        EnsureLeaseRoutingInfrastructure(services);

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
    /// <param name="schemaName">The schema name (default: "infra").</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    [Obsolete("This method uses a hardcoded connection string and bypasses dynamic discovery. Use AddSqlPlatformMultiDatabaseWithDiscovery or AddSqlPlatformMultiDatabaseWithList instead to ensure all databases go through IPlatformDatabaseDiscovery.")]
    public static IServiceCollection AddSystemLeases(this IServiceCollection services, string connectionString, string schemaName = "infra")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Add time abstractions
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        var options = new SystemLeaseOptions
        {
            SchemaName = schemaName,
        };

        services.Configure<SystemLeaseOptions>(o =>
        {
            o.ConnectionString = connectionString;
            o.SchemaName = options.SchemaName;
            o.DefaultLeaseDuration = options.DefaultLeaseDuration;
            o.RenewPercent = options.RenewPercent;
            o.UseGate = options.UseGate;
            o.GateTimeoutMs = options.GateTimeoutMs;
            o.EnableSchemaDeployment = options.EnableSchemaDeployment;
        });

        var leaseFactoryConfig = new LeaseFactoryConfig
        {
            ConnectionString = connectionString,
            SchemaName = schemaName,
            RenewPercent = options.RenewPercent,
            GateTimeoutMs = options.GateTimeoutMs,
            UseGate = options.UseGate,
        };

        services.TryAddSingleton<ISystemLeaseFactory>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<SqlLeaseFactory>>();
            return new SqlLeaseFactory(leaseFactoryConfig, logger);
        });

        services.TryAddSingleton<ILeaseFactoryProvider>(provider =>
        {
            var identifier = ExtractIdentifier(connectionString);
            return new SingleLeaseFactoryProvider(provider.GetRequiredService<ISystemLeaseFactory>(), identifier);
        });

        EnsureLeaseRoutingInfrastructure(services);

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
    /// Adds multi-database lease functionality with support for managing leases across multiple databases.
    /// This enables lease operations across multiple customer databases.
    /// </summary>
    /// <param name="services">The IServiceCollection to add services to.</param>
    /// <param name="leaseConfigs">List of lease database configurations, one for each database.</param>
    /// <returns>The IServiceCollection so that additional calls can be chained.</returns>
    [Obsolete("This method uses hardcoded database configurations and bypasses dynamic discovery. Use AddSqlPlatformMultiDatabaseWithDiscovery or AddSqlPlatformMultiDatabaseWithList instead to ensure all databases go through IPlatformDatabaseDiscovery.")]
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

        EnsureLeaseRoutingInfrastructure(services);

        return services;
    }

    /// <summary>
    /// Adds multi-database lease functionality using a custom <see cref="ILeaseFactoryProvider"/> factory.
    /// This overload is intended for advanced scenarios where lease database sources are discovered or managed dynamically at runtime,
    /// such as integration with external configuration providers or service discovery mechanisms.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="factoryProviderFactory">
    /// A factory function that creates an <see cref="ILeaseFactoryProvider"/> instance.
    /// This allows for custom logic to determine the set of lease databases at runtime.
    /// </param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    internal static IServiceCollection AddMultiSystemLeases(
        this IServiceCollection services,
        Func<IServiceProvider, ILeaseFactoryProvider> factoryProviderFactory)
    {
        // Add time abstractions
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMonotonicClock, MonotonicClock>();

        // Register the custom factory provider
        services.AddSingleton(factoryProviderFactory);

        EnsureLeaseRoutingInfrastructure(services);

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

        EnsureLeaseRoutingInfrastructure(services);

        return services;
    }

    private static void EnsureLeaseRoutingInfrastructure(IServiceCollection services)
    {
        services.TryAddSingleton<ILeaseRouter>(provider =>
        {
            var factoryProvider = provider.GetRequiredService<ILeaseFactoryProvider>();
            var logger = provider.GetRequiredService<ILogger<LeaseRouter>>();
            return new LeaseRouter(factoryProvider, logger);
        });

        // Provide a default lease factory for legacy consumers that request ISystemLeaseFactory directly.
        services.TryAddSingleton<ISystemLeaseFactory>(provider =>
        {
            return provider.GetRequiredService<ILeaseRouter>()
                .GetDefaultLeaseFactoryAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        });
    }

    private static string ExtractIdentifier(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            if (!string.IsNullOrWhiteSpace(builder.InitialCatalog))
            {
                return builder.InitialCatalog;
            }

            if (!string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return builder.DataSource;
            }
        }
        catch
        {
            // Ignore parsing failures and fall back to default.
        }

        return "default";
    }
}
