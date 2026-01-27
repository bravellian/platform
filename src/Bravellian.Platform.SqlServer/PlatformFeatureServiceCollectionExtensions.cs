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

using Bravellian.Platform.Idempotency;
using Bravellian.Platform.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Unified feature registration helpers that wire multi-database providers through
/// <see cref="IPlatformDatabaseDiscovery"/> and <see cref="PlatformConfiguration"/>.
/// These helpers mirror the registrations used by <see cref="PlatformServiceCollectionExtensions"/>
/// so that individual features can participate in discovery-first environments without
/// re-implementing feature-specific discovery interfaces.
/// </summary>
internal static class PlatformFeatureServiceCollectionExtensions
{
    /// <summary>
    /// Registers multi-database Outbox services backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tableName">Optional table name override. Defaults to "Outbox".</param>
    /// <param name="enableSchemaDeployment">Whether to deploy schemas for discovered databases.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformOutbox(
        this IServiceCollection services,
        string tableName = "Outbox",
        bool enableSchemaDeployment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        services.AddMultiSqlOutbox(
            sp => new PlatformOutboxStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                tableName,
                enableSchemaDeployment,
                sp.GetService<PlatformConfiguration>(),
                sp.GetService<IPlatformEventEmitter>()),
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
            cleanupInterval: TimeSpan.FromHours(1),
            schemaCompletion: sp.GetService<IDatabaseSchemaCompletion>()));

        return services;
    }

    /// <summary>
    /// Registers multi-database Inbox services backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tableName">Optional table name override. Defaults to "Inbox".</param>
    /// <param name="enableSchemaDeployment">Whether to deploy schemas for discovered databases.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformInbox(
        this IServiceCollection services,
        string tableName = "Inbox",
        bool enableSchemaDeployment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        services.AddMultiSqlInbox(
            sp => new PlatformInboxWorkStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                tableName,
                enableSchemaDeployment,
                sp.GetService<PlatformConfiguration>()),
            new RoundRobinInboxSelectionStrategy());

        // Register multi-inbox cleanup service
        services.AddHostedService<MultiInboxCleanupService>(sp => new MultiInboxCleanupService(
            sp.GetRequiredService<IInboxWorkStoreProvider>(),
            sp.GetRequiredService<IMonotonicClock>(),
            sp.GetRequiredService<ILogger<MultiInboxCleanupService>>(),
            retentionPeriod: TimeSpan.FromDays(7),
            cleanupInterval: TimeSpan.FromHours(1),
            schemaCompletion: sp.GetService<IDatabaseSchemaCompletion>()));

        return services;
    }

    /// <summary>
    /// Registers multi-database Scheduler services (timers + jobs) backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="selectionStrategy">Optional selection strategy for polling. Defaults to round robin.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformScheduler(
        this IServiceCollection services,
        IOutboxSelectionStrategy? selectionStrategy = null)
    {
        return services.AddMultiSqlScheduler(
            sp => new PlatformSchedulerStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetService<PlatformConfiguration>(),
                sp.GetService<IPlatformEventEmitter>()),
            selectionStrategy ?? new RoundRobinOutboxSelectionStrategy());
    }

    /// <summary>
    /// Registers multi-database Fanout services backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformFanout(this IServiceCollection services)
    {
        return services.AddMultiSqlFanout(sp => new PlatformFanoutRepositoryProvider(
            sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
            sp.GetRequiredService<ILoggerFactory>()));
    }

    /// <summary>
    /// Registers multi-database lease services backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="enableSchemaDeployment">Whether to deploy schemas for discovered databases.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformLeases(
        this IServiceCollection services,
        bool enableSchemaDeployment = false)
    {
        return services.AddMultiSystemLeases(sp => new PlatformLeaseFactoryProvider(
            sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetService<PlatformConfiguration>(),
            enableSchemaDeployment));
    }

    /// <summary>
    /// Registers multi-database external side-effect tracking backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tableName">Optional table name override. Defaults to "ExternalSideEffect".</param>
    /// <param name="enableSchemaDeployment">Whether to deploy schemas for discovered databases.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformExternalSideEffects(
        this IServiceCollection services,
        string tableName = "ExternalSideEffect",
        bool enableSchemaDeployment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        services.TryAddSingleton<IExternalSideEffectStoreProvider>(sp => new PlatformExternalSideEffectStoreProvider(
            sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>(),
            tableName,
            enableSchemaDeployment,
            sp.GetService<PlatformConfiguration>()));

        services.AddOptions<ExternalSideEffectCoordinatorOptions>();
        services.TryAddSingleton<IExternalSideEffectCoordinator, ExternalSideEffectCoordinator>();

        return services;
    }

    /// <summary>
    /// Registers multi-database idempotency tracking backed by <see cref="IPlatformDatabaseDiscovery"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="tableName">Optional table name override. Defaults to "Idempotency".</param>
    /// <param name="lockDuration">Lock duration for in-progress keys.</param>
    /// <param name="lockDurationProvider">Optional per-key lock duration provider.</param>
    /// <param name="enableSchemaDeployment">Whether to deploy schemas for discovered databases.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddPlatformIdempotency(
        this IServiceCollection services,
        string tableName = "Idempotency",
        TimeSpan? lockDuration = null,
        Func<string, TimeSpan>? lockDurationProvider = null,
        bool enableSchemaDeployment = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var duration = lockDuration ?? TimeSpan.FromMinutes(5);

        services.TryAddSingleton<IIdempotencyStoreProvider>(sp => new PlatformIdempotencyStoreProvider(
            sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILoggerFactory>(),
            tableName,
            duration,
            lockDurationProvider,
            enableSchemaDeployment,
            sp.GetService<PlatformConfiguration>()));

        services.TryAddSingleton<IIdempotencyStoreRouter, IdempotencyStoreRouter>();
        services.TryAddSingleton<IIdempotencyStore>(provider =>
        {
            var storeProvider = provider.GetRequiredService<IIdempotencyStoreProvider>();
            var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();
            if (stores.Count == 0)
            {
                throw new InvalidOperationException(
                    "No idempotency stores are configured. Configure at least one store or use IIdempotencyStoreRouter.");
            }

            if (stores.Count > 1)
            {
                throw new InvalidOperationException(
                    "Multiple idempotency stores are configured. Resolve IIdempotencyStoreRouter instead of IIdempotencyStore for multi-database setups.");
            }

            var key = storeProvider.GetStoreIdentifier(stores[0]);
            return provider.GetRequiredService<IIdempotencyStoreRouter>().GetStore(key);
        });

        return services;
    }
}
