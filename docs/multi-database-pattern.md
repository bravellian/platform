# Multi-Database Pattern for Platform Primitives

## Overview

This document describes the architectural pattern used to enable multi-database support for the outbox primitive and provides guidance for applying this pattern to other platform primitives like inbox and distributed locks (system leases).

## Pattern Architecture

The multi-database pattern is based on four core abstractions that work together to enable processing across multiple databases:

### 1. Store Provider Interface

**Purpose:** Provides access to all store instances across multiple databases.

**Pattern:**
```csharp
public interface I{Primitive}StoreProvider
{
    /// <summary>
    /// Gets all available stores that should be processed.
    /// Each store represents a separate database/tenant.
    /// </summary>
    IReadOnlyList<I{Primitive}Store> GetAllStores();

    /// <summary>
    /// Gets a unique identifier for a store (e.g., database name, tenant ID).
    /// Used for logging and diagnostics.
    /// </summary>
    string GetStoreIdentifier(I{Primitive}Store store);
}
```

**Example from Outbox:**
```csharp
public interface IOutboxStoreProvider
{
    IReadOnlyList<IOutboxStore> GetAllStores();
    string GetStoreIdentifier(IOutboxStore store);
}
```

### 2. Selection Strategy Interface

**Purpose:** Determines which database/store to process next.

**Pattern:**
```csharp
public interface I{Primitive}SelectionStrategy
{
    /// <summary>
    /// Selects the next store to process.
    /// </summary>
    /// <param name="stores">All available stores.</param>
    /// <param name="lastProcessedStore">The store processed in the last iteration.</param>
    /// <param name="lastProcessedCount">Number of items processed from the last store.</param>
    /// <returns>The next store to process, or null if none should be processed.</returns>
    I{Primitive}Store? SelectNext(
        IReadOnlyList<I{Primitive}Store> stores,
        I{Primitive}Store? lastProcessedStore,
        int lastProcessedCount);

    /// <summary>
    /// Resets the strategy state.
    /// </summary>
    void Reset();
}
```

**Example from Outbox:**
```csharp
public interface IOutboxSelectionStrategy
{
    IOutboxStore? SelectNext(
        IReadOnlyList<IOutboxStore> stores,
        IOutboxStore? lastProcessedStore,
        int lastProcessedCount);

    void Reset();
}
```

### 3. Multi-Database Dispatcher

**Purpose:** Coordinates processing across multiple stores using the selection strategy.

**Pattern:**
```csharp
public sealed class Multi{Primitive}Dispatcher
{
    private readonly I{Primitive}StoreProvider storeProvider;
    private readonly I{Primitive}SelectionStrategy selectionStrategy;
    private readonly ILogger<Multi{Primitive}Dispatcher> logger;
    
    private I{Primitive}Store? lastProcessedStore;
    private int lastProcessedCount;

    public Multi{Primitive}Dispatcher(
        I{Primitive}StoreProvider storeProvider,
        I{Primitive}SelectionStrategy selectionStrategy,
        ILogger<Multi{Primitive}Dispatcher> logger)
    {
        this.storeProvider = storeProvider;
        this.selectionStrategy = selectionStrategy;
        this.logger = logger;
    }

    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        var stores = this.storeProvider.GetAllStores();
        
        if (stores.Count == 0)
        {
            return 0;
        }

        // Use selection strategy to pick next store
        var selectedStore = this.selectionStrategy.SelectNext(
            stores,
            this.lastProcessedStore,
            this.lastProcessedCount);

        if (selectedStore == null)
        {
            return 0;
        }

        var storeIdentifier = this.storeProvider.GetStoreIdentifier(selectedStore);
        
        // Process items from the selected store
        var processedCount = await this.ProcessFromStoreAsync(
            selectedStore, 
            storeIdentifier, 
            batchSize, 
            cancellationToken);

        // Track for next iteration
        this.lastProcessedStore = selectedStore;
        this.lastProcessedCount = processedCount;

        return processedCount;
    }

    private async Task<int> ProcessFromStoreAsync(
        I{Primitive}Store store,
        string storeIdentifier,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Primitive-specific processing logic
        throw new NotImplementedException();
    }
}
```

### 4. Multi-Database Polling Service

**Purpose:** Background service that continuously polls and processes using the multi-database dispatcher.

**Pattern:**
```csharp
public sealed class Multi{Primitive}PollingService : BackgroundService
{
    private readonly Multi{Primitive}Dispatcher dispatcher;
    private readonly IMonotonicClock mono;
    private readonly double intervalSeconds;
    private readonly int batchSize;
    private readonly ILogger logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this.logger.LogInformation(
            "Starting multi-{primitive} polling service with {IntervalMs}ms interval",
            this.intervalSeconds * 1000);

        while (!stoppingToken.IsCancellationRequested)
        {
            var next = this.mono.Seconds + this.intervalSeconds;

            try
            {
                var processedCount = await this.dispatcher.RunOnceAsync(
                    this.batchSize, 
                    stoppingToken);
                    
                if (processedCount > 0)
                {
                    this.logger.LogDebug(
                        "Multi-{primitive} polling iteration: {Count} items processed",
                        processedCount);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Error in multi-{primitive} polling iteration");
            }

            // Sleep until next interval
            var sleep = Math.Max(0, next - this.mono.Seconds);
            if (sleep > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(sleep), stoppingToken);
            }
        }
    }
}
```

## Standard Selection Strategies

### Round-Robin Strategy

Cycles through all stores in order, providing fair distribution.

```csharp
public sealed class RoundRobin{Primitive}SelectionStrategy : I{Primitive}SelectionStrategy
{
    private int currentIndex = 0;

    public I{Primitive}Store? SelectNext(
        IReadOnlyList<I{Primitive}Store> stores,
        I{Primitive}Store? lastProcessedStore,
        int lastProcessedCount)
    {
        if (stores.Count == 0) return null;

        if (lastProcessedStore != null)
        {
            var lastIndex = FindStoreIndex(stores, lastProcessedStore);
            if (lastIndex >= 0)
            {
                this.currentIndex = (lastIndex + 1) % stores.Count;
            }
        }

        var selected = stores[this.currentIndex];
        this.currentIndex = (this.currentIndex + 1) % stores.Count;
        return selected;
    }

    public void Reset() => this.currentIndex = 0;

    private static int FindStoreIndex(IReadOnlyList<I{Primitive}Store> stores, I{Primitive}Store store)
    {
        for (int i = 0; i < stores.Count; i++)
        {
            if (ReferenceEquals(stores[i], store))
            {
                return i;
            }
        }
        return -1;
    }
}
```

### Drain-First Strategy

Continues processing the same store until empty, then moves to the next.

```csharp
public sealed class DrainFirst{Primitive}SelectionStrategy : I{Primitive}SelectionStrategy
{
    private int currentIndex = 0;

    public I{Primitive}Store? SelectNext(
        IReadOnlyList<I{Primitive}Store> stores,
        I{Primitive}Store? lastProcessedStore,
        int lastProcessedCount)
    {
        if (stores.Count == 0) return null;

        // Keep processing same store if it had messages
        if (lastProcessedStore != null && lastProcessedCount > 0)
        {
            var lastIndex = FindStoreIndex(stores, lastProcessedStore);
            if (lastIndex >= 0)
            {
                this.currentIndex = lastIndex;
                return stores[this.currentIndex];
            }
        }

        // Move to next store if last was empty or null
        if (lastProcessedStore != null)
        {
            var lastIndex = FindStoreIndex(stores, lastProcessedStore);
            if (lastIndex >= 0)
            {
                this.currentIndex = (lastIndex + 1) % stores.Count;
            }
        }

        return stores[this.currentIndex];
    }

    public void Reset() => this.currentIndex = 0;

    private static int FindStoreIndex(IReadOnlyList<I{Primitive}Store> stores, I{Primitive}Store store)
    {
        for (int i = 0; i < stores.Count; i++)
        {
            if (ReferenceEquals(stores[i], store))
            {
                return i;
            }
        }
        return -1;
    }
}
```

## Application to Inbox

### Required Components

1. **`IInboxStoreProvider`** - Already have `IInboxWorkStore`, create provider interface
2. **`IInboxSelectionStrategy`** - Selection strategy for inbox stores
3. **`MultiInboxDispatcher`** - Dispatcher that works with inbox stores
4. **`MultiInboxPollingService`** - Background service for polling

### Implementation Steps

```csharp
// 1. Create the store provider interface
public interface IInboxWorkStoreProvider
{
    IReadOnlyList<IInboxWorkStore> GetAllStores();
    string GetStoreIdentifier(IInboxWorkStore store);
}

// 2. Create selection strategy interface
public interface IInboxSelectionStrategy
{
    IInboxWorkStore? SelectNext(
        IReadOnlyList<IInboxWorkStore> stores,
        IInboxWorkStore? lastProcessedStore,
        int lastProcessedCount);
    
    void Reset();
}

// 3. Implement selection strategies (RoundRobin, DrainFirst)
public sealed class RoundRobinInboxSelectionStrategy : IInboxSelectionStrategy
{
    // Same pattern as outbox
}

// 4. Create MultiInboxDispatcher
public sealed class MultiInboxDispatcher
{
    private readonly IInboxWorkStoreProvider storeProvider;
    private readonly IInboxSelectionStrategy selectionStrategy;
    private readonly IInboxHandlerResolver resolver;
    private readonly ILogger<MultiInboxDispatcher> logger;

    public async Task<int> RunOnceAsync(int batchSize, CancellationToken cancellationToken)
    {
        // Select store using strategy
        // Claim items from selected store
        // Process items
        // Track for next iteration
    }
}

// 5. Create MultiInboxPollingService
public sealed class MultiInboxPollingService : BackgroundService
{
    // Same pattern as MultiOutboxPollingService
}

// 6. Add extension methods
public static class InboxServiceCollectionExtensions
{
    public static IServiceCollection AddMultiSqlInbox(
        this IServiceCollection services,
        IEnumerable<SqlInboxOptions> inboxOptions,
        IInboxSelectionStrategy? selectionStrategy = null)
    {
        // Register store provider
        // Register selection strategy
        // Register dispatcher
        // Register polling service
        return services;
    }
}
```

## Application to System Leases (Distributed Locks)

System leases are slightly different since they're typically not continuously polled like outbox/inbox. However, the pattern can still apply for scenarios where you need to manage leases across multiple databases.

### Key Differences

1. **No polling service** - Leases are acquired on-demand, not continuously polled
2. **Store provider still useful** - For scenarios where you need to acquire/release leases across multiple databases
3. **Selection strategy less relevant** - Typically you acquire a lease in a specific database, not cycle through them

### Implementation Approach

```csharp
// 1. Store provider for managing leases across databases
public interface ISystemLeaseFactoryProvider
{
    IReadOnlyList<ISystemLeaseFactory> GetAllFactories();
    string GetFactoryIdentifier(ISystemLeaseFactory factory);
}

// 2. Use case: Acquire a lease in any available database
public class MultiDatabaseLeaseAcquisition
{
    private readonly ISystemLeaseFactoryProvider factoryProvider;

    public async Task<ISystemLease?> TryAcquireLeaseInAnyDatabaseAsync(
        string leaseName,
        CancellationToken cancellationToken)
    {
        var factories = this.factoryProvider.GetAllFactories();

        foreach (var factory in factories)
        {
            try
            {
                var lease = await factory.AcquireLeaseAsync(
                    leaseName,
                    cancellationToken);
                    
                if (lease != null)
                {
                    return lease;
                }
            }
            catch (Exception ex)
            {
                // Log and continue to next database
            }
        }

        return null; // Could not acquire lease in any database
    }
}
```

## DI Registration Pattern

Use extension methods to make registration simple:

```csharp
public static class SchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddMultiSql{Primitive}(
        this IServiceCollection services,
        IEnumerable<Sql{Primitive}Options> options,
        I{Primitive}SelectionStrategy? selectionStrategy = null)
    {
        // Add time abstractions if needed
        services.AddTimeAbstractions();

        // Register store provider
        services.AddSingleton<I{Primitive}StoreProvider>(provider =>
        {
            var timeProvider = provider.GetRequiredService<TimeProvider>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new Configured{Primitive}StoreProvider(options, timeProvider, loggerFactory);
        });

        // Register selection strategy
        services.AddSingleton<I{Primitive}SelectionStrategy>(
            selectionStrategy ?? new RoundRobin{Primitive}SelectionStrategy());

        // Register dispatcher
        services.AddSingleton<Multi{Primitive}Dispatcher>();

        // Register polling service (if applicable)
        services.AddHostedService<Multi{Primitive}PollingService>();

        return services;
    }

    // Overload for custom store provider
    public static IServiceCollection AddMultiSql{Primitive}(
        this IServiceCollection services,
        Func<IServiceProvider, I{Primitive}StoreProvider> storeProviderFactory,
        I{Primitive}SelectionStrategy? selectionStrategy = null)
    {
        services.AddTimeAbstractions();
        services.AddSingleton(storeProviderFactory);
        services.AddSingleton<I{Primitive}SelectionStrategy>(
            selectionStrategy ?? new RoundRobin{Primitive}SelectionStrategy());
        services.AddSingleton<Multi{Primitive}Dispatcher>();
        services.AddHostedService<Multi{Primitive}PollingService>();
        return services;
    }
}
```

## Testing Pattern

Follow the same testing approach for all primitives:

### 1. Selection Strategy Tests

```csharp
public class {Primitive}SelectionStrategyTests
{
    [Fact]
    public void SelectNext_WithNoStores_ReturnsNull() { }

    [Fact]
    public void RoundRobin_CyclesThroughStores() { }

    [Fact]
    public void DrainFirst_SticksToSameStoreWhenItemsProcessed() { }

    [Fact]
    public void DrainFirst_MovesToNextStoreWhenNoItemsProcessed() { }

    [Fact]
    public void Reset_ResetsToFirstStore() { }
}
```

### 2. Integration Tests

```csharp
public class Multi{Primitive}DispatcherTests : SqlServerTestBase
{
    [Fact]
    public async Task ProcessesItemsFromMultipleStores() { }

    [Fact]
    public async Task WithDrainFirstStrategy_DrainsOneStoreBeforeMoving() { }

    [Fact]
    public async Task HandlesErrorsGracefully() { }
}
```

## Summary

The multi-database pattern provides a consistent, proven approach for extending platform primitives to work across multiple databases:

1. **Store Provider** - Abstraction for accessing multiple stores
2. **Selection Strategy** - Pluggable logic for choosing which store to process
3. **Multi-Database Dispatcher** - Coordinates processing using the strategy
4. **Polling Service** - Background service (when continuous processing is needed)

This pattern has been successfully applied to the outbox primitive and can be applied to inbox and system leases with minimal modifications.
