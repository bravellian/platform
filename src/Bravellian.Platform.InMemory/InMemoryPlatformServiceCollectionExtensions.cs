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

using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform;

/// <summary>
/// Extension methods for registering the in-memory platform stack.
/// </summary>
public static class InMemoryPlatformServiceCollectionExtensions
{
    /// <summary>
    /// Registers the in-memory platform for a multi-database environment.
    /// Features run across the provided list of in-memory stores using round-robin scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databases">The list of in-memory databases.</param>
    /// <param name="configure">Optional configuration for platform options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryPlatformMultiDatabaseWithList(
        this IServiceCollection services,
        IEnumerable<InMemoryPlatformDatabase> databases,
        Action<InMemoryPlatformOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(databases);

        var databaseList = databases.ToList();
        if (databaseList.Count == 0)
        {
            throw new ArgumentException("Database list must not be empty.", nameof(databases));
        }

        var platformOptions = new InMemoryPlatformOptions();
        configure?.Invoke(platformOptions);

        var outboxOptions = new List<InMemoryOutboxOptions>();
        var inboxOptions = new List<InMemoryInboxOptions>();
        var schedulerOptions = new List<InMemorySchedulerOptions>();
        var fanoutOptions = new List<InMemoryFanoutOptions>();

        foreach (var database in databaseList)
        {
            var outbox = new InMemoryOutboxOptions { StoreKey = database.Name };
            var inbox = new InMemoryInboxOptions { StoreKey = database.Name };
            var scheduler = new InMemorySchedulerOptions { StoreKey = database.Name };
            var fanout = new InMemoryFanoutOptions { StoreKey = database.Name };

            platformOptions.ConfigureOutbox?.Invoke(outbox);
            platformOptions.ConfigureInbox?.Invoke(inbox);
            platformOptions.ConfigureScheduler?.Invoke(scheduler);
            platformOptions.ConfigureFanout?.Invoke(fanout);

            outboxOptions.Add(outbox);
            inboxOptions.Add(inbox);
            schedulerOptions.Add(scheduler);
            fanoutOptions.Add(fanout);
        }

        var globalOutboxOptions = new InMemoryOutboxOptions { StoreKey = "global" };
        var globalSchedulerOptions = new InMemorySchedulerOptions { StoreKey = "global" };
        platformOptions.ConfigureOutbox?.Invoke(globalOutboxOptions);
        platformOptions.ConfigureScheduler?.Invoke(globalSchedulerOptions);
        globalOutboxOptions.StoreKey = "global";
        globalSchedulerOptions.StoreKey = "global";

        services.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddTimeAbstractions();

        services.AddSingleton(sp => new InMemoryPlatformRegistry(
            outboxOptions,
            inboxOptions,
            schedulerOptions,
            fanoutOptions,
            globalOutboxOptions,
            globalSchedulerOptions,
            sp.GetRequiredService<TimeProvider>()));

        RegisterOutbox(services);
        RegisterInbox(services);
        RegisterScheduler(services, platformOptions.EnableSchedulerWorkers);
        RegisterGlobalScheduler(services, platformOptions.EnableSchedulerWorkers);
        RegisterGlobalOutbox(services);
        RegisterFanout(services);
        RegisterLeases(services);
        RegisterGlobalLeases(services);
        RegisterCleanupServices(services, outboxOptions, globalOutboxOptions, inboxOptions);

        return services;
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

        var key = options.WorkKey is null ? options.FanoutTopic : $"{options.FanoutTopic}:{options.WorkKey}";
        services.AddKeyedScoped<IFanoutPlanner, TPlanner>(key);

        services.AddKeyedScoped<IFanoutCoordinator>(key, (provider, coordinatorKey) =>
        {
            var planner = provider.GetRequiredKeyedService<IFanoutPlanner>(coordinatorKey);
            var dispatcher = provider.GetRequiredService<IFanoutDispatcher>();
            var leaseFactory = provider.GetRequiredService<ISystemLeaseFactory>();
            var logger = provider.GetRequiredService<ILogger<FanoutCoordinator>>();

            return new FanoutCoordinator(planner, dispatcher, leaseFactory, logger);
        });

        services.AddSingleton<IHostedService>(provider => new FanoutJobRegistrationService(provider, options));

        return services;
    }

    private static void RegisterOutbox(IServiceCollection services)
    {
        services.AddSingleton<IOutboxStoreProvider, InMemoryOutboxStoreProvider>();
        services.AddSingleton<IOutboxSelectionStrategy, RoundRobinOutboxSelectionStrategy>();
        services.AddSingleton<IOutboxHandlerResolver, OutboxHandlerResolver>();
        services.AddSingleton<MultiOutboxDispatcher>();
        services.AddHostedService<MultiOutboxPollingService>();
        services.AddSingleton<IOutboxRouter, OutboxRouter>();
        services.TryAddSingleton<IOutbox>(ResolveDefaultOutbox);
        services.TryAddSingleton<IOutboxJoinStore>(sp => sp.GetRequiredService<InMemoryPlatformRegistry>().Stores[0].OutboxJoinStore);
    }

    private static void RegisterInbox(IServiceCollection services)
    {
        services.AddSingleton<IInboxWorkStoreProvider, InMemoryInboxWorkStoreProvider>();
        services.AddSingleton<IInboxSelectionStrategy, RoundRobinInboxSelectionStrategy>();
        services.AddSingleton<IInboxHandlerResolver, InboxHandlerResolver>();
        services.AddSingleton<MultiInboxDispatcher>();
        services.AddHostedService<MultiInboxPollingService>();
        services.AddSingleton<IInboxRouter, InboxRouter>();
        services.TryAddSingleton<IInbox>(ResolveDefaultInbox);
        services.TryAddSingleton<IInboxWorkStore>(ResolveDefaultInboxWorkStore);
    }

    private static void RegisterScheduler(IServiceCollection services, bool enableSchedulerWorkers)
    {
        services.AddSingleton<ISchedulerStoreProvider, InMemorySchedulerStoreProvider>();
        services.AddSingleton<MultiSchedulerDispatcher>();
        if (enableSchedulerWorkers)
        {
            services.AddHostedService<MultiSchedulerPollingService>();
        }

        services.AddSingleton<ISchedulerRouter, SchedulerRouter>();
        services.TryAddSingleton<ISchedulerClient>(ResolveDefaultSchedulerClient);
    }

    private static void RegisterGlobalScheduler(IServiceCollection services, bool enableSchedulerWorkers)
    {
        services.AddSingleton<IGlobalSchedulerClient>(sp => new InMemoryGlobalSchedulerClient(
            sp.GetRequiredService<InMemoryPlatformRegistry>().GlobalSchedulerClient));
        services.AddSingleton<IGlobalSchedulerStore>(sp => new InMemoryGlobalSchedulerStore(
            sp.GetRequiredService<InMemoryPlatformRegistry>().GlobalSchedulerStore));
        services.AddSingleton<GlobalSchedulerDispatcher>();
        if (enableSchedulerWorkers)
        {
            services.AddHostedService<GlobalSchedulerPollingService>();
        }
    }

    private static void RegisterGlobalOutbox(IServiceCollection services)
    {
        services.AddSingleton<IGlobalOutbox>(sp => new InMemoryGlobalOutbox(
            sp.GetRequiredService<InMemoryPlatformRegistry>().GlobalOutboxService));
        services.AddSingleton<IGlobalOutboxStore>(sp => new InMemoryGlobalOutboxStore(
            sp.GetRequiredService<InMemoryPlatformRegistry>().GlobalOutboxStore));
        services.AddSingleton<GlobalOutboxDispatcher>();
        services.AddHostedService<GlobalOutboxPollingService>();
    }

    private static void RegisterFanout(IServiceCollection services)
    {
        services.AddSingleton<IFanoutRepositoryProvider, InMemoryFanoutRepositoryProvider>();
        services.AddSingleton<IFanoutRouter, FanoutRouter>();
        services.AddSingleton<IFanoutPolicyRepository>(sp => sp.GetRequiredService<InMemoryPlatformRegistry>().Stores[0].FanoutPolicyRepository);
        services.AddSingleton<IFanoutCursorRepository>(sp => sp.GetRequiredService<InMemoryPlatformRegistry>().Stores[0].FanoutCursorRepository);
        services.AddSingleton<IFanoutDispatcher, FanoutDispatcher>();
        services.AddTransient<IOutboxHandler, FanoutJobHandler>();
    }

    private static void RegisterLeases(IServiceCollection services)
    {
        services.AddSingleton<ILeaseFactoryProvider, InMemoryLeaseFactoryProvider>();
        services.TryAddSingleton<ILeaseRouter>(provider =>
        {
            var factoryProvider = provider.GetRequiredService<ILeaseFactoryProvider>();
            var logger = provider.GetRequiredService<ILogger<LeaseRouter>>();
            return new LeaseRouter(factoryProvider, logger);
        });

        services.TryAddSingleton<ISystemLeaseFactory>(provider =>
        {
            return provider.GetRequiredService<ILeaseRouter>()
                .GetDefaultLeaseFactoryAsync()
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
        });
    }

    private static void RegisterGlobalLeases(IServiceCollection services)
    {
        services.AddSingleton<IGlobalSystemLeaseFactory>(sp => new InMemoryGlobalSystemLeaseFactory(
            sp.GetRequiredService<InMemoryPlatformRegistry>().GlobalLeaseFactory));
    }

    private static void RegisterCleanupServices(
        IServiceCollection services,
        IEnumerable<InMemoryOutboxOptions> outboxOptions,
        InMemoryOutboxOptions globalOutboxOptions,
        IEnumerable<InMemoryInboxOptions> inboxOptions)
    {
        var outboxCleanup = outboxOptions.Where(o => o.EnableAutomaticCleanup).ToList();
        if (globalOutboxOptions.EnableAutomaticCleanup)
        {
            outboxCleanup.Add(globalOutboxOptions);
        }

        if (outboxCleanup.Count > 0)
        {
            var interval = outboxCleanup.Min(o => o.CleanupInterval);
            services.AddHostedService(sp => new InMemoryOutboxCleanupService(
                sp.GetRequiredService<InMemoryPlatformRegistry>(),
                sp.GetRequiredService<IMonotonicClock>(),
                sp.GetRequiredService<ILogger<InMemoryOutboxCleanupService>>(),
                cleanupInterval: interval));
        }

        var inboxCleanup = inboxOptions.Where(o => o.EnableAutomaticCleanup).ToList();
        if (inboxCleanup.Count > 0)
        {
            var interval = inboxCleanup.Min(o => o.CleanupInterval);
            services.AddHostedService(sp => new InMemoryInboxCleanupService(
                sp.GetRequiredService<InMemoryPlatformRegistry>(),
                sp.GetRequiredService<IMonotonicClock>(),
                sp.GetRequiredService<ILogger<InMemoryInboxCleanupService>>(),
                cleanupInterval: interval));
        }
    }

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
            throw new InvalidOperationException("Multiple inbox work stores are configured. Resolve IInboxRouter instead of IInbox for multi-database setups.");
        }

        var router = provider.GetRequiredService<IInboxRouter>();
        var key = storeProvider.GetStoreIdentifier(stores[0]);
        return router.GetInbox(key);
    }

    private static IInboxWorkStore ResolveDefaultInboxWorkStore(IServiceProvider provider)
    {
        var storeProvider = provider.GetRequiredService<IInboxWorkStoreProvider>();
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No inbox work stores are configured. Configure at least one store or use IInboxRouter.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple inbox work stores are configured. Resolve IInboxRouter instead of IInboxWorkStore for multi-database setups.");
        }

        return stores[0];
    }

    private static ISchedulerClient ResolveDefaultSchedulerClient(IServiceProvider provider)
    {
        var storeProvider = provider.GetRequiredService<ISchedulerStoreProvider>();
        var stores = storeProvider.GetAllStoresAsync().GetAwaiter().GetResult();

        if (stores.Count == 0)
        {
            throw new InvalidOperationException("No scheduler stores are configured. Configure at least one store or use ISchedulerRouter.");
        }

        if (stores.Count > 1)
        {
            throw new InvalidOperationException("Multiple scheduler stores are configured. Resolve ISchedulerRouter instead of ISchedulerClient for multi-database setups.");
        }

        var key = storeProvider.GetStoreIdentifier(stores[0]);
        var router = provider.GetRequiredService<ISchedulerRouter>();
        return router.GetSchedulerClient(key);
    }
}
