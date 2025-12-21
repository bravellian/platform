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
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform;

/// <summary>
/// Unified feature registration helpers that wire multi-database providers through
/// <see cref="IPlatformDatabaseDiscovery"/> and <see cref="PlatformConfiguration"/>.
/// These helpers mirror the registrations used by <see cref="PlatformServiceCollectionExtensions"/>
/// so that individual features can participate in discovery-first environments without
/// re-implementing feature-specific discovery interfaces.
/// </summary>
public static class PlatformFeatureServiceCollectionExtensions
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

        return services.AddMultiSqlOutbox(
            sp => new PlatformOutboxStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                tableName,
                enableSchemaDeployment,
                sp.GetService<PlatformConfiguration>()),
            new RoundRobinOutboxSelectionStrategy());
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

        return services.AddMultiSqlInbox(
            sp => new PlatformInboxWorkStoreProvider(
                sp.GetRequiredService<IPlatformDatabaseDiscovery>(),
                sp.GetRequiredService<TimeProvider>(),
                sp.GetRequiredService<ILoggerFactory>(),
                tableName,
                enableSchemaDeployment,
                sp.GetService<PlatformConfiguration>()),
            new RoundRobinInboxSelectionStrategy());
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
                sp.GetService<PlatformConfiguration>()),
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
}
