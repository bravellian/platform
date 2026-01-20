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


using Bravellian.Platform.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;
/// <summary>
/// Extension methods for unified platform registration.
/// </summary>
public static class OutboxServiceCollectionExtensions
{
    private const string PlatformConfigurationKey = "Bravellian.Platform.Configuration.Registered";

    /// <summary>
    /// Registers the platform for a multi-database environment without control plane.
    /// Features run across the provided list of databases using round-robin scheduling.
    /// For single database scenarios, pass a list with one database.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register Dapper type handlers for strongly-typed IDs
        DapperTypeHandlerRegistration.RegisterTypeHandlers();

        // Add time abstractions
        services.AddTimeAbstractions();

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


    private static void EnsureNotAlreadyRegistered(IServiceCollection services)
    {
        // Check if already registered by looking for PlatformConfiguration
        var existing = services.FirstOrDefault(d => d.ServiceType == typeof(PlatformConfiguration));
        if (existing != null)
        {
            throw new InvalidOperationException(
                "Platform registration has already been called. Only one of the four AddPlatform* methods can be used. " +
                "Ensure you call exactly one of: AddPlatformMultiDatabaseWithList, " +
                "AddPlatformMultiDatabaseWithDiscovery, AddPlatformMultiDatabaseWithControlPlaneAndList, or " +
                "AddPlatformMultiDatabaseWithControlPlaneAndDiscovery.");
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
                "IPlatformDatabaseDiscovery is not registered. Register your discovery implementation before calling AddPlatformMultiDatabaseWithControlPlaneAndDiscovery (or use the overload that accepts a discovery factory/type).");
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

            var outboxes = services.Where(d => d.ServiceType == typeof(IOutbox)).ToList();
            if (outboxes.Count > 0)
            {
                var details = string.Join(", ", outboxes.Select(DescribeDescriptor));
                throw new InvalidOperationException(
                    $"Direct IOutbox registrations are not supported in multi-database configurations. Use IOutboxRouter via discovery instead. Remove: {details}.");
            }
        }
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
        services.AddMultiSqlOutbox(
            sp => new PlatformOutboxStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                "Outbox",
                enableSchemaDeployment,
                config), // Pass configuration to filter out control plane
            new RoundRobinOutboxSelectionStrategy());

        // Register outbox join store (uses same connection strings as outbox)
        services.TryAddSingleton<IOutboxJoinStore, SqlOutboxJoinStore>();

        // Register JoinWaitHandler for fan-in orchestration
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOutboxHandler, JoinWaitHandler>());

        // Register multi-outbox cleanup service
        services.AddHostedService<MultiOutboxCleanupService>(sp => new MultiOutboxCleanupService(
            sp.GetRequiredService<IOutboxStoreProvider>(),
            sp.GetRequiredService<IMonotonicClock>(),
            sp.GetRequiredService<ILogger<MultiOutboxCleanupService>>(),
            retentionPeriod: TimeSpan.FromDays(7),
            cleanupInterval: TimeSpan.FromHours(1)));
    }
}
