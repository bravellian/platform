# Bravellian Platform API Reference

This document lists all public types across the Bravellian Platform packages, including their signatures and XML summary semantics.

## Bravellian.Platform

When to use: Core platform abstractions and orchestration for outbox/inbox/scheduler/fanout/leases and shared primitives.
How it works: Defines contracts and DI entry points; provider packages (SqlServer/Postgres) supply storage implementations and optional schema deployment.


### ActiveAlert (record)
Namespace: `Bravellian.Platform.Observability`

Represents an active alert in the watchdog system.

Members:
- (No public members found.)

### BaseFanoutPlanner (class)
Namespace: `Bravellian.Platform`

Base implementation of IFanoutPlanner that provides common cadence and cursor-aware logic. Application code only needs to implement the candidate enumeration logic.

Members:
- `public async Task<IReadOnlyList<FanoutSlice>> GetDueSlicesAsync(string fanoutTopic, string? workKey, CancellationToken ct)` — <inheritdoc/>

### CachedHealthCheck (class)
Namespace: `Bravellian.Platform.HealthChecks`

Wraps an existing health check with caching behavior that respects status-specific durations.

Members:
- `public CachedHealthCheck(IHealthCheck innerHealthCheck, CachedHealthCheckOptions options, TimeProvider? timeProvider = null)` — Initializes a new instance of the <see cref="CachedHealthCheck"/> class.

### CachedHealthCheckOptions (class)
Namespace: `Bravellian.Platform.HealthChecks`

Configuration options for <see cref="CachedHealthCheck"/>.

Members:
- `public TimeSpan HealthyCacheDuration` — Gets or sets how long healthy results are cached. Defaults to 1 minute.
- `public TimeSpan DegradedCacheDuration` — Gets or sets how long degraded results are cached. Defaults to 30 seconds.
- `public TimeSpan UnhealthyCacheDuration` — Gets or sets how long unhealthy results are cached. Defaults to 0 (no caching).

### DrainFirstInboxSelectionStrategy (class)
Namespace: `Bravellian.Platform`

Drain-first selection strategy that continues to poll the same inbox work store until it returns no messages, then moves to the next store. This is useful for prioritizing complete processing of one database before moving to others. This class is thread-safe.

Members:
- `public IInboxWorkStore? SelectNext( IReadOnlyList<IInboxWorkStore> stores, IInboxWorkStore? lastProcessedStore, int lastProcessedCount)` — <inheritdoc/>

### DrainFirstOutboxSelectionStrategy (class)
Namespace: `Bravellian.Platform`

Drain-first selection strategy that continues to poll the same outbox store until it returns no messages, then moves to the next store. This is useful for prioritizing complete processing of one database before moving to others. This class is thread-safe.

Members:
- `public IOutboxStore? SelectNext( IReadOnlyList<IOutboxStore> stores, IOutboxStore? lastProcessedStore, int lastProcessedCount)` — <inheritdoc/>

### ExceptionFilter (class)
Namespace: `Bravellian.Platform`

Provides utility methods for filtering exceptions in catch blocks. This helper is designed to prevent catching critical exceptions that should terminate the application, such as <see cref="OutOfMemoryException"/> and <see cref="StackOverflowException"/>.

Members:
- `public static bool IsCatchable(Exception exception)` — Determines whether an exception should be caught by generic exception handlers. Returns <c>false</c> for critical exceptions that should not be caught and should instead terminate the application.

### ExternalSideEffectAttempt (record)
Namespace: `Bravellian.Platform`

Represents the outcome of attempting to begin an external side-effect execution.

Members:
- `public ExternalSideEffectAttempt( ExternalSideEffectAttemptDecision decision, ExternalSideEffectRecord record, string? reason = null)` — Initializes a new instance of the <see cref="ExternalSideEffectAttempt"/> class.

### ExternalSideEffectAttemptDecision (enum)
Namespace: `Bravellian.Platform`

Describes the decision for starting an external side-effect attempt.

Members:
- `Ready = 0` — The attempt may proceed.
- `Locked = 1` — The attempt is locked by another worker.
- `AlreadyCompleted = 2` — The side effect is already completed.

### ExternalSideEffectCheckBehavior (enum)
Namespace: `Bravellian.Platform`

Specifies how to handle unknown external check results.

Members:
- `RetryLater = 0` — Schedule a retry when the check is inconclusive.
- `ExecuteAnyway = 1` — Continue execution even when the check is inconclusive.

### ExternalSideEffectCheckResult (record)
Namespace: `Bravellian.Platform`

Represents the result of an external side-effect check.

Members:
- `public ExternalSideEffectCheckResult(ExternalSideEffectCheckStatus status)` — Initializes a new instance of the <see cref="ExternalSideEffectCheckResult"/> class.

### ExternalSideEffectCheckStatus (enum)
Namespace: `Bravellian.Platform`

Describes the result status of an external check.

Members:
- `Confirmed = 0` — The external side effect is confirmed.
- `NotFound = 1` — The external side effect is not found.
- `Unknown = 2` — The external side effect state is unknown.

### ExternalSideEffectContext (record)
Namespace: `Bravellian.Platform`

Encapsulates context for executing an external side effect.

Members:
- (No public members found.)

### ExternalSideEffectCoordinator (class)
Namespace: `Bravellian.Platform`

Coordinates external side-effect execution with idempotency and retries.

Members:
- `public ExternalSideEffectCoordinator( IExternalSideEffectStoreProvider storeProvider, TimeProvider timeProvider, IOptions<ExternalSideEffectCoordinatorOptions> options, ILogger<ExternalSideEffectCoordinator> logger)` — Initializes a new instance of the <see cref="ExternalSideEffectCoordinator"/> class.

### ExternalSideEffectCoordinatorOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for coordinating external side effects.

Members:
- `public TimeSpan AttemptLockDuration` — Gets or sets the lock duration for an execution attempt.
- `public TimeSpan MinimumCheckInterval` — Gets or sets the minimum interval between external checks.
- `public ExternalSideEffectCheckBehavior UnknownCheckBehavior` — Gets or sets the behavior when external checks are inconclusive.

### ExternalSideEffectExecutionResult (record)
Namespace: `Bravellian.Platform`

Represents the result of executing an external side effect.

Members:
- `public ExternalSideEffectExecutionResult(ExternalSideEffectExecutionStatus status)` — Initializes a new instance of the <see cref="ExternalSideEffectExecutionResult"/> class.

### ExternalSideEffectExecutionStatus (enum)
Namespace: `Bravellian.Platform`

Describes the execution status of an external side effect.

Members:
- `Succeeded = 0` — The execution succeeded.
- `RetryableFailure = 1` — The execution failed but may be retried.
- `PermanentFailure = 2` — The execution failed permanently.

### ExternalSideEffectKey (record)
Namespace: `Bravellian.Platform`

Identifies an external side-effect operation and idempotency key.

Members:
- `public ExternalSideEffectKey(string operationName, string idempotencyKey)` — Initializes a new instance of the <see cref="ExternalSideEffectKey"/> class.

### ExternalSideEffectOutboxHandler (class)
Namespace: `Bravellian.Platform`

Base outbox handler for coordinating external side effects.

Members:
- `public abstract string Topic` — Gets the outbox topic handled by this handler.
- `public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)` — Handles an outbox message by coordinating the external side effect.

### ExternalSideEffectOutcome (record)
Namespace: `Bravellian.Platform`

Represents the outcome of executing an external side effect.

Members:
- `public ExternalSideEffectOutcome(ExternalSideEffectOutcomeStatus status, ExternalSideEffectRecord record, string? message = null)` — Initializes a new instance of the <see cref="ExternalSideEffectOutcome"/> class.

### ExternalSideEffectOutcomeStatus (enum)
Namespace: `Bravellian.Platform`

Describes the outcome status of an external side effect.

Members:
- `Completed = 0` — The external side effect completed successfully.
- `AlreadyCompleted = 1` — The external side effect was already completed.
- `RetryScheduled = 2` — A retry has been scheduled.
- `PermanentFailure = 3` — The external side effect failed permanently.

### ExternalSideEffectPermanentFailureException (class)
Namespace: `Bravellian.Platform`

Exception thrown when an external side effect fails permanently.

Members:
- `public ExternalSideEffectPermanentFailureException(string message) : base(message)` — Initializes a new instance of the <see cref="ExternalSideEffectPermanentFailureException"/> class.

### ExternalSideEffectRecord (record)
Namespace: `Bravellian.Platform`

Represents the persisted state of an external side effect.

Members:
- `public Guid Id` — Gets the record identifier.
- `public required string OperationName` — Gets the operation name.
- `public required string IdempotencyKey` — Gets the idempotency key.
- `public ExternalSideEffectStatus Status` — Gets the current status.
- `public int AttemptCount` — Gets the number of execution attempts.
- `public DateTimeOffset CreatedAt` — Gets the creation timestamp.
- `public DateTimeOffset LastUpdatedAt` — Gets the last updated timestamp.
- `public DateTimeOffset? LastAttemptAt` — Gets the last attempt timestamp.
- `public DateTimeOffset? LastExternalCheckAt` — Gets the last external check timestamp.
- `public DateTimeOffset? LockedUntil` — Gets the lock expiration timestamp.
- `public Guid? LockedBy` — Gets the identifier of the locker.
- `public string? CorrelationId` — Gets the correlation identifier.
- `public Guid? OutboxMessageId` — Gets the outbox message identifier.
- `public string? ExternalReferenceId` — Gets the external reference identifier.
- `public string? ExternalStatus` — Gets the external status value.
- `public string? LastError` — Gets the last error message.
- `public string? PayloadHash` — Gets the payload hash used for idempotency.

### ExternalSideEffectRequest (record)
Namespace: `Bravellian.Platform`

Describes an external side-effect request.

Members:
- `public ExternalSideEffectRequest(string storeKey, ExternalSideEffectKey key)` — Initializes a new instance of the <see cref="ExternalSideEffectRequest"/> class.

### ExternalSideEffectRetryableException (class)
Namespace: `Bravellian.Platform`

Exception thrown when an external side effect should be retried.

Members:
- `public ExternalSideEffectRetryableException(string message) : base(message)` — Initializes a new instance of the <see cref="ExternalSideEffectRetryableException"/> class.

### ExternalSideEffectStatus (enum)
Namespace: `Bravellian.Platform`

Members:
- `Pending = 0` — The side effect is pending execution.
- `InFlight = 1` — The side effect is currently in flight.
- `Succeeded = 2` — The side effect completed successfully.
- `Failed = 3` — The side effect failed.

### FanoutDatabaseConfig (class)
Namespace: `Bravellian.Platform`

Configuration for a single fanout database.

Members:
- `public required string Identifier` — Gets or sets a unique identifier for this database (e.g., customer ID, tenant ID).
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for the fanout tables. Defaults to "infra".
- `public string PolicyTableName` — Gets or sets the table name for the fanout policy. Defaults to "FanoutPolicy".
- `public string CursorTableName` — Gets or sets the table name for the fanout cursor. Defaults to "FanoutCursor".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated when the database is first discovered. Defaults to true.

### FanoutJobPayload (record)
Namespace: `Bravellian.Platform`

Payload for fanout coordination jobs.

Members:
- (No public members found.)

### FanoutSlice (record)
Namespace: `Bravellian.Platform`

Represents a single fan-out unit that identifies a specific piece of work to be processed. The unit combines a fanout topic, shard key, work key, and optional window information.

Members:
- (No public members found.)

### FanoutTopicOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for a fanout topic that define its schedule and behavior. Each topic/work key combination gets its own recurring job with these settings.

Members:
- `public required string FanoutTopic` — Gets the fanout topic name (e.g., "etl", "reports").
- `public string? WorkKey` — Gets the optional work key to filter planning (e.g., "payments", "vendors").
- `public string Cron` — Gets the cron schedule for the fanout (e.g., "*/5 * * * *" for every 5 minutes).
- `public int DefaultEverySeconds` — Gets the default cadence in seconds if cron is not used.
- `public int JitterSeconds` — Gets the jitter in seconds to prevent thundering herd problems.
- `public TimeSpan LeaseDuration` — Gets the duration to hold the coordination lease.

### HealthChecksBuilderExtensions (class)
Namespace: `Bravellian.Platform.HealthChecks`

Extension methods for adding platform-specific health check utilities.

Members:
- `public static IHealthChecksBuilder AddCachedCheck<THealthCheck>( this IHealthChecksBuilder builder, string name, Action<CachedHealthCheckOptions>? configure = null, HealthStatus? failureStatus = null, IEnumerable<string>? tags = null) where THealthCheck : class, IHealthCheck` — Adds a cached health check, allowing status-aware cache durations.

### HeartbeatContext (record)
Namespace: `Bravellian.Platform.Observability`

Represents the context of a heartbeat event.

Members:
- (No public members found.)

### IDatabaseSchemaCompletion (interface)
Namespace: `Bravellian.Platform`

Provides coordination for database schema deployment completion.

Members:
- (No public members found.)

### IExternalSideEffectCoordinator (interface)
Namespace: `Bravellian.Platform`

Coordinates external side effects with idempotency guarantees.

Members:
- (No public members found.)

### IExternalSideEffectStore (interface)
Namespace: `Bravellian.Platform`

Persists external side-effect state for idempotent execution.

Members:
- (No public members found.)

### IExternalSideEffectStoreProvider (interface)
Namespace: `Bravellian.Platform`

Provides external side-effect stores.

Members:
- (No public members found.)

### IFanoutCoordinator (interface)
Namespace: `Bravellian.Platform`

Coordinates the fanout process by acquiring a lease, running the planner, and dispatching slices. This is the main orchestration component that ties together all fanout operations.

Members:
- (No public members found.)

### IFanoutCursorRepository (interface)
Namespace: `Bravellian.Platform`

Repository for tracking the last completion timestamp for each fanout slice. This enables resumable processing and prevents duplicate work by tracking progress per shard.

Members:
- (No public members found.)

### IFanoutDatabaseDiscovery (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism for discovering fanout database configurations dynamically. Implementations can query a registry, database, or configuration service to get the current list of customer databases.

Members:
- (No public members found.)

### IFanoutDispatcher (interface)
Namespace: `Bravellian.Platform`

Handles the dispatching of fanout slices to the underlying messaging system. Default implementation uses Outbox, but this interface allows for custom dispatch strategies.

Members:
- (No public members found.)

### IFanoutPlanner (interface)
Namespace: `Bravellian.Platform`

Implemented by application code to decide which slices are due for processing now. This interface provides the domain-specific logic for determining when work needs to be scheduled.

Members:
- (No public members found.)

### IFanoutPolicyRepository (interface)
Namespace: `Bravellian.Platform`

Repository for managing fanout policies that define cadence and jitter settings. These policies determine how frequently each fanout topic/work key combination should run.

Members:
- (No public members found.)

### IFanoutRouter (interface)
Namespace: `Bravellian.Platform`

Routes fanout operations to the appropriate database based on a routing key. This enables multi-tenant fanout processing where each tenant has their own database.

Members:
- (No public members found.)

### IHeartbeatSink (interface)
Namespace: `Bravellian.Platform.Observability`

Defines a sink for processing heartbeat events.

Members:
- (No public members found.)

### IInbox (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism to track processed inbound messages for at-most-once processing guarantees. Implements the Inbox pattern to prevent duplicate message processing.

Members:
- (No public members found.)

### IInboxDatabaseDiscovery (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism for discovering inbox database configurations dynamically. Implementations can query a registry, database, or configuration service to get the current list of customer databases.

Members:
- (No public members found.)

### IInboxHandler (interface)
Namespace: `Bravellian.Platform`

Handles inbound messages for a specific topic. Implementations can perform local work or transform/forward messages.

Members:
- (No public members found.)

### IInboxRouter (interface)
Namespace: `Bravellian.Platform`

Routes inbox write operations to the appropriate inbox database based on a routing key. This enables multi-tenant scenarios where messages need to be written to different database instances based on tenant ID or other routing criteria.

Members:
- (No public members found.)

### IInboxSelectionStrategy (interface)
Namespace: `Bravellian.Platform`

Defines a strategy for selecting which inbox work store to poll next when processing messages across multiple databases/tenants.

Members:
- (No public members found.)

### IInboxState (interface)
Namespace: `Bravellian.Platform.Observability`

Provides state information about the inbox for monitoring.

Members:
- (No public members found.)

### IInboxWorkStore (interface)
Namespace: `Bravellian.Platform`

Provides work-queue style operations for the inbox store. Mirrors the work queue pattern used by Outbox, Timers, and JobRuns.

Members:
- (No public members found.)

### ILeaseDatabaseDiscovery (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism for discovering lease database configurations dynamically. Implementations can query a registry, database, or configuration service to get the current list of customer databases.

Members:
- (No public members found.)

### ILeaseFactoryProvider (interface)
Namespace: `Bravellian.Platform`

Provides access to multiple lease factories, enabling lease management across multiple databases. This abstraction allows the system to acquire and manage leases in multiple customer databases, each with their own lease table.

Members:
- (No public members found.)

### ILeaseRouter (interface)
Namespace: `Bravellian.Platform`

Routes lease requests to the correct tenant-specific lease factory.

Members:
- (No public members found.)

### IMetricRegistrar (interface)
Namespace: `Bravellian.Platform.Metrics`

Service for registering custom metrics with tag whitelists.

Members:
- (No public members found.)

### IMonotonicClock (interface)
Namespace: `Bravellian.Platform`

Provides monotonic time measurements for durations and timeouts. Monotonic time is not affected by system clock adjustments and should be used for measuring elapsed time, timeouts, and relative timing.

Members:
- (No public members found.)

### InboxDatabaseConfig (class)
Namespace: `Bravellian.Platform`

Configuration for a single inbox database.

Members:
- `public required string Identifier` — Gets or sets a unique identifier for this database (e.g., customer ID, tenant ID).
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for the inbox table. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for the inbox. Defaults to "Inbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated when the database is first discovered. Defaults to true.

### InboxMessage (record)
Namespace: `Bravellian.Platform`

Represents an inbound message for processing through the Inbox Handler system.

Members:
- `public string MessageId` — Gets the message identifier.
- `public string Source` — Gets the source system for the message.
- `public string Topic` — Gets the message topic.
- `public string Payload` — Gets the message payload.
- `public byte[]? Hash` — Gets the payload hash when available.
- `public int Attempt` — Gets the processing attempt count.
- `public DateTimeOffset FirstSeenUtc` — Gets the first seen timestamp in UTC.
- `public DateTimeOffset LastSeenUtc` — Gets the last seen timestamp in UTC.
- `public DateTimeOffset? DueTimeUtc` — Gets the due time in UTC, when scheduled.
- `public string? LastError` — Gets the last error message, if any.

### IOutbox (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism to enqueue messages for later processing as part of a transactional operation, and to claim and process messages using a reliable work queue pattern.

Members:
- (No public members found.)

### IOutboxDatabaseDiscovery (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism for discovering outbox database configurations dynamically. Implementations can query a registry, database, or configuration service to get the current list of customer databases.

Members:
- (No public members found.)

### IOutboxHandler (interface)
Namespace: `Bravellian.Platform`

Handles outbox messages for a specific topic. Implementations can perform local work (email, reports) or forward to brokers.

Members:
- (No public members found.)

### IOutboxJoinStore (interface)
Namespace: `Bravellian.Platform`

Provides data access operations for the outbox join store, enabling fan-in/join semantics for outbox messages.

Members:
- (No public members found.)

### IOutboxRouter (interface)
Namespace: `Bravellian.Platform`

Routes outbox write operations to the appropriate outbox database based on a routing key. This enables multi-tenant scenarios where messages need to be written to different database instances based on tenant ID or other routing criteria.

Members:
- (No public members found.)

### IOutboxSelectionStrategy (interface)
Namespace: `Bravellian.Platform`

Defines a strategy for selecting which outbox store to poll next when processing messages across multiple databases/tenants.

Members:
- (No public members found.)

### IOutboxState (interface)
Namespace: `Bravellian.Platform.Observability`

Provides state information about the outbox for monitoring.

Members:
- (No public members found.)

### IOutboxStore (interface)
Namespace: `Bravellian.Platform`

Provides data access operations for the outbox store. This is a thin, SQL-backed interface that the dispatcher uses.

Members:
- (No public members found.)

### IPlatformDatabaseDiscovery (interface)
Namespace: `Bravellian.Platform`

Platform-level database discovery interface used by all features (Outbox, Inbox, Timers, Jobs, Fan-out). Responsible for returning the set of application databases to work with. Implementations must be read-only, idempotent, and must not perform schema changes or connect to control plane.

Members:
- (No public members found.)

### IProcessingState (interface)
Namespace: `Bravellian.Platform.Observability`

Provides state information about processors for monitoring.

Members:
- (No public members found.)

### ISchedulerClient (interface)
Namespace: `Bravellian.Platform`

A client for scheduling and managing durable timers and recurring jobs, with support for claiming and processing scheduled work items.

Members:
- (No public members found.)

### ISchedulerDatabaseDiscovery (interface)
Namespace: `Bravellian.Platform`

Provides a mechanism for discovering scheduler database configurations dynamically. Implementations can query a registry, database, or configuration service to get the current list of customer databases.

Members:
- (No public members found.)

### ISchedulerRouter (interface)
Namespace: `Bravellian.Platform`

Routes scheduler write operations to the appropriate scheduler database based on a routing key. This enables multi-tenant scenarios where scheduler operations need to be written to different database instances based on tenant ID or other routing criteria.

Members:
- (No public members found.)

### ISchedulerState (interface)
Namespace: `Bravellian.Platform.Observability`

Provides state information about the scheduler for monitoring.

Members:
- (No public members found.)

### ISchedulerStore (interface)
Namespace: `Bravellian.Platform`

Represents scheduler operations for a specific database instance. This abstraction enables the scheduler to work with multiple databases.

Members:
- (No public members found.)

### ISchedulerStoreProvider (interface)
Namespace: `Bravellian.Platform`

Provides access to multiple scheduler stores, enabling cross-database scheduler processing. This abstraction allows the system to poll and process scheduler work from multiple customer databases, each with their own scheduler tables.

Members:
- (No public members found.)

### ISystemLease (interface)
Namespace: `Bravellian.Platform`

Represents a distributed system lease with fencing token support.

Members:
- (No public members found.)

### ISystemLeaseFactory (interface)
Namespace: `Bravellian.Platform`

Factory for creating system leases for distributed coordination.

Members:
- (No public members found.)

### IWatchdog (interface)
Namespace: `Bravellian.Platform.Observability`

Provides access to the watchdog state for interrogation.

Members:
- (No public members found.)

### IWatchdogAlertSink (interface)
Namespace: `Bravellian.Platform.Observability`

Defines a sink for processing watchdog alerts.

Members:
- (No public members found.)

### JoinNotReadyException (class)
Namespace: `Bravellian.Platform`

Exception thrown when a join is not ready to be completed (not all steps finished). This causes the message to be abandoned and retried later.

Members:
- `public JoinNotReadyException(string message) : base(message)` — Initializes a new instance of the <see cref="JoinNotReadyException"/> class.

### JoinWaitHandler (class)
Namespace: `Bravellian.Platform`

Handles join.wait messages to implement fan-in orchestration. This handler waits for all steps in a join to complete, then executes follow-up actions.

Members:
- `public JoinWaitHandler( IOutboxJoinStore joinStore, ILogger<JoinWaitHandler> logger, IOutbox? outbox = null)` — Initializes a new instance of the <see cref="JoinWaitHandler"/> class.

### LeaseAcquireResult (record struct)
Namespace: `Bravellian.Platform`

Members:
- (No public members found.)

### LeaseDatabaseConfig (class)
Namespace: `Bravellian.Platform`

Configuration for a single lease database.

Members:
- `public required string Identifier` — Gets or sets a unique identifier for this database (e.g., customer ID, tenant ID).
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for the lease table. Defaults to "infra".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated when the database is first discovered. Defaults to true.

### LeaseRenewResult (record struct)
Namespace: `Bravellian.Platform`

Members:
- (No public members found.)

### LostLeaseException (class)
Namespace: `Bravellian.Platform`

Members:
- `public LostLeaseException()` — Initializes a new instance of the <see cref="LostLeaseException"/> class.

### MetricAggregationKind (enum)
Namespace: `Bravellian.Platform.Metrics`

Specifies the type of metric aggregation.

Members:
- `Counter` — Counter metric (monotonically increasing).
- `Gauge` — Gauge metric (sampled value).
- `Histogram` — Histogram metric (distribution of values).

### MetricRegistration (record)
Namespace: `Bravellian.Platform.Metrics`

Represents a metric registration with allowed tags.

Members:
- `public MetricRegistration( string name, string unit, MetricAggregationKind aggKind, string description, string[] allowedTags)` — Initializes a new instance of the <see cref="MetricRegistration"/> record.

### MetricUnit (class)
Namespace: `Bravellian.Platform.Metrics`

Standard metric units.

Members:
- `public const string Count = "count";` — Dimensionless count.
- `public const string Milliseconds = "ms";` — Milliseconds.
- `public const string Seconds = "seconds";` — Seconds.
- `public const string Bytes = "bytes";` — Bytes.
- `public const string Percent = "percent";` — Percentage (0-100).

### MonoDeadline (record struct)
Namespace: `Bravellian.Platform`

Represents a monotonic deadline that can be used to check if a certain point in time has been reached. This is useful for timeouts and scheduling that should not be affected by system clock adjustments.

Members:
- `public bool Expired(IMonotonicClock clock)` — Checks if this deadline has expired based on the current monotonic clock time.

### MultiSchedulerDispatcher (class)
Namespace: `Bravellian.Platform`

Dispatches scheduler work across multiple databases/tenants using a pluggable selection strategy to determine which scheduler to process next. This enables processing scheduler work from multiple customer databases in a single worker.

Members:
- `public MultiSchedulerDispatcher( ISchedulerStoreProvider storeProvider, IOutboxSelectionStrategy selectionStrategy, ILeaseFactoryProvider leaseFactoryProvider, TimeProvider timeProvider, ILogger<MultiSchedulerDispatcher> logger)` — Initializes a new instance of the <see cref="MultiSchedulerDispatcher"/> class.

### MultiSchedulerPollingService (class)
Namespace: `Bravellian.Platform`

Background service that periodically processes scheduler work from multiple databases. Each database has its own lease, so multiple instances can run concurrently, each processing different databases.

Members:
- `public MultiSchedulerPollingService( MultiSchedulerDispatcher dispatcher, ILogger<MultiSchedulerPollingService> logger, TimeSpan? pollingInterval = null, IDatabaseSchemaCompletion? schemaCompletion = null)` — Initializes a new instance of the <see cref="MultiSchedulerPollingService"/> class.

### ObservabilityBuilder (class)
Namespace: `Bravellian.Platform.Observability`

Builder for configuring platform observability.

Members:
- `public ObservabilityBuilder(IServiceCollection services)` — Initializes a new instance of the <see cref="ObservabilityBuilder"/> class.

### ObservabilityOptions (class)
Namespace: `Bravellian.Platform.Observability`

Configuration options for platform observability.

Members:
- `public bool EnableMetrics` — Gets or sets a value indicating whether metrics are enabled. Default: true.
- `public bool EnableLogging` — Gets or sets a value indicating whether logging is enabled. Default: false.
- `public string MetricsPrefix` — Gets or sets the metrics prefix. Default: "bravellian.platform".
- `public WatchdogOptions Watchdog` — Gets or sets the watchdog options.

### ObservabilityServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Observability`

Extension methods for registering platform observability services.

Members:
- `public static ObservabilityBuilder AddPlatformObservability( this IServiceCollection services, Action<ObservabilityOptions>? configure = null)` — Adds platform observability services.

### OnceExecutionRegistry (class)
Namespace: `Bravellian.Platform`

Provides a thread-safe registry for one-time operations keyed by string.

Members:
- `public bool HasRun(string key)` — Determines whether the specified key has been marked as executed.

### OutboxDatabaseConfig (class)
Namespace: `Bravellian.Platform`

Configuration for a single outbox database.

Members:
- `public required string Identifier` — Gets or sets a unique identifier for this database (e.g., customer ID, tenant ID).
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for the outbox table. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for the outbox. Defaults to "Outbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated when the database is first discovered. Defaults to true.

### OutboxExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for <see cref="IOutbox"/> to simplify common operations.

Members:
- `public static async Task EnqueueJoinWaitAsync( this IOutbox outbox, Bravellian.Platform.Outbox.JoinIdentifier joinId, bool failIfAnyStepFailed = true, string? onCompleteTopic = null, string? onCompletePayload = null, string? onFailTopic = null, string? onFailPayload = null, CancellationToken cancellationToken = default)` — Enqueues a join.wait message to orchestrate fan-in behavior for the specified join. This is a convenience method that creates and serializes the JoinWaitPayload automatically.

### OutboxHandlerServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for registering outbox and inbox handlers.

Members:
- `public static IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services) where THandler : class, IOutboxHandler` — Registers an outbox handler for a specific topic.

### OutboxJoinMember (record)
Namespace: `Bravellian.Platform`

Represents the association between an outbox join and an outbox message. This many-to-many relationship allows: - One join to track multiple messages - One message to participate in multiple joins

Members:
- `public JoinIdentifier JoinId` — Gets the join identifier.
- `public OutboxMessageIdentifier OutboxMessageId` — Gets the outbox message identifier.
- `public DateTimeOffset CreatedUtc` — Gets the timestamp when this association was created.
- `public DateTimeOffset? CompletedAt` — Gets the timestamp when this member was marked as completed, or null if not completed.
- `public DateTimeOffset? FailedAt` — Gets the timestamp when this member was marked as failed, or null if not failed.

### OutboxMessage (record)
Namespace: `Bravellian.Platform`

Represents an outbox message awaiting dispatch.

Members:
- `public OutboxWorkItemIdentifier Id` — Gets the outbox work item identifier.
- `public required string Payload` — Gets the message payload.
- `public required string Topic` — Gets the message topic.
- `public DateTimeOffset CreatedAt` — Gets the creation timestamp.
- `public bool IsProcessed` — Gets a value indicating whether the message has been processed.
- `public DateTimeOffset? ProcessedAt` — Gets the processed timestamp, if any.
- `public string? ProcessedBy` — Gets the processor identifier, if any.
- `public int RetryCount` — Gets the retry count.
- `public string? LastError` — Gets the last error message, if any.
- `public OutboxMessageIdentifier MessageId` — Gets the message identifier.
- `public string? CorrelationId` — Gets the correlation identifier.
- `public DateTimeOffset? DueTimeUtc` — Gets the due time in UTC, when scheduled.

### OutboxPermanentFailureException (class)
Namespace: `Bravellian.Platform`

Exception thrown when an outbox handler encounters a permanent failure.

Members:
- `public OutboxPermanentFailureException(string message) : base(message)` — Initializes a new instance of the <see cref="OutboxPermanentFailureException"/> class.

### PlatformControlPlaneOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the platform control plane database.

Members:
- `public required string ConnectionString` — Gets or initializes the connection string for the control plane database.
- `public string SchemaName` — Gets or initializes the schema name for platform tables in the control plane database (default: "infra").
- `public bool EnableSchemaDeployment` — Gets or initializes whether to automatically create platform tables and procedures at startup.

### PlatformDatabase (class)
Namespace: `Bravellian.Platform`

Represents a single application database in the platform.

Members:
- `public required string Name` — Gets or initializes the unique name identifier for this database.
- `public required string ConnectionString` — Gets or initializes the connection string for this database.
- `public string SchemaName` — Gets or initializes the schema name to use for platform tables (default: "infra").

### PlatformEnvironmentStyle (enum)
Namespace: `Bravellian.Platform`

Defines the two environment styles supported by the platform.

Members:
- `MultiDatabaseNoControl` — Multi-database environment without control plane. Features run against multiple application databases with round-robin scheduling. For single database scenarios, use this with a discovery service that returns one database.
- `MultiDatabaseWithControl` — Multi-database environment with control plane. Features run against multiple application databases with control plane coordination.

### PlatformMeterOptions (class)
Namespace: `Bravellian.Platform.Metrics`

Defines the name and version of the meter used for instrumentation.

Members:
- `public string MeterName` — Gets the meter name used to register metrics.
- `public string? MeterVersion` — Gets the optional meter version.

### PlatformMeterProvider (class)
Namespace: `Bravellian.Platform.Metrics`

Provides helpers to create meters and common metric instruments.

Members:
- `public PlatformMeterProvider(IMeterFactory meterFactory, PlatformMeterOptions options)` — Initializes a provider that uses the supplied meter factory and options.

### PlatformMetricCatalog (class)
Namespace: `Bravellian.Platform.Metrics`

Catalog of predefined platform metrics.

Members:
- `public static IReadOnlyList<MetricRegistration> All` — Gets all platform metrics.

### RoundRobinInboxSelectionStrategy (class)
Namespace: `Bravellian.Platform`

Round-robin selection strategy that cycles through all inbox work stores, processing one batch from each store before moving to the next. This ensures fair distribution of processing across all databases. This class is thread-safe.

Members:
- `public IInboxWorkStore? SelectNext( IReadOnlyList<IInboxWorkStore> stores, IInboxWorkStore? lastProcessedStore, int lastProcessedCount)` — <inheritdoc/>

### RoundRobinOutboxSelectionStrategy (class)
Namespace: `Bravellian.Platform`

Round-robin selection strategy that cycles through all outbox stores, processing one batch from each store before moving to the next. This ensures fair distribution of processing across all databases. This class is thread-safe.

Members:
- `public IOutboxStore? SelectNext( IReadOnlyList<IOutboxStore> stores, IOutboxStore? lastProcessedStore, int lastProcessedCount)` — <inheritdoc/>

### SchedulerDatabaseConfig (class)
Namespace: `Bravellian.Platform`

Configuration for a single scheduler database.

Members:
- `public required string Identifier` — Gets or sets a unique identifier for this database (e.g., customer ID, tenant ID).
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for scheduler tables. Defaults to "infra".
- `public string JobsTableName` — Gets or sets the table name for jobs. Defaults to "Jobs".
- `public string JobRunsTableName` — Gets or sets the table name for job runs. Defaults to "JobRuns".
- `public string TimersTableName` — Gets or sets the table name for timers. Defaults to "Timers".

### SchedulerRouter (class)
Namespace: `Bravellian.Platform`

Default implementation of ISchedulerRouter that uses an ISchedulerStoreProvider to route write operations to the appropriate scheduler database.

Members:
- `public SchedulerRouter(ISchedulerStoreProvider storeProvider)` — Initializes a new instance of the <see cref="SchedulerRouter"/> class.

### WatchdogAlertContext (record)
Namespace: `Bravellian.Platform.Observability`

Represents the context of a watchdog alert that is passed to alert sinks.

Members:
- (No public members found.)

### WatchdogAlertKind (enum)
Namespace: `Bravellian.Platform.Observability`

Defines the kinds of alerts that can be raised by the watchdog.

Members:
- `OverdueJob` — A scheduled job is overdue beyond the configured threshold.
- `StuckInbox` — An inbox message is stuck beyond the configured threshold.
- `StuckOutbox` — An outbox message is stuck beyond the configured threshold.
- `ProcessorNotRunning` — A processor loop is not running or has been idle beyond the configured threshold.
- `HeartbeatStale` — The watchdog heartbeat is stale.

### WatchdogOptions (class)
Namespace: `Bravellian.Platform.Observability`

Configuration options for the watchdog service.

Members:
- `public TimeSpan ScanPeriod` — Gets or sets the period between watchdog scans. Default: 15 seconds. Jitter of ±10% is applied automatically.
- `public TimeSpan HeartbeatPeriod` — Gets or sets the period between heartbeat emissions. Default: 30 seconds.
- `public TimeSpan HeartbeatTimeout` — Gets or sets the heartbeat timeout threshold. If exceeded, health becomes Unhealthy. Default: 90 seconds.
- `public TimeSpan JobOverdueThreshold` — Gets or sets the threshold for overdue jobs. Default: 30 seconds.
- `public TimeSpan InboxStuckThreshold` — Gets or sets the threshold for stuck inbox messages. Default: 5 minutes.
- `public TimeSpan OutboxStuckThreshold` — Gets or sets the threshold for stuck outbox messages. Default: 5 minutes.
- `public TimeSpan ProcessorIdleThreshold` — Gets or sets the threshold for idle processors. Default: 1 minute.
- `public TimeSpan AlertCooldown` — Gets or sets the cooldown period for alert re-emission per key. Default: 2 minutes.

### WatchdogSnapshot (record)
Namespace: `Bravellian.Platform.Observability`

Represents a snapshot of the watchdog state at a point in time.

Members:
- (No public members found.)

## Bravellian.Platform.Audit

When to use: You need an immutable, human-readable audit timeline that can be queried by anchors.
How it works: Append-only audit events with anchors and outcomes; write via IAuditEventWriter and query via IAuditEventReader.


### AuditActor (record)
Namespace: `Bravellian.Platform.Audit`

Describes the actor responsible for an audit event.

Members:
- `public AuditActor(string actorType, string actorId, string? actorDisplay)` — Initializes a new instance of the <see cref="AuditActor"/> record.

### AuditEvent (record)
Namespace: `Bravellian.Platform.Audit`

Represents a single immutable audit event.

Members:
- `public AuditEvent( AuditEventId eventId, DateTimeOffset occurredAtUtc, string name, string displayMessage, EventOutcome outcome, IReadOnlyList<EventAnchor> anchors, string? dataJson = null, AuditActor? actor = null, CorrelationContext? correlation = null)` — Initializes a new instance of the <see cref="AuditEvent"/> record.

### AuditEventId (record struct)
Namespace: `Bravellian.Platform.Audit`

Represents the identifier of an audit event.

Members:
- `public AuditEventId(string value)` — Initializes a new instance of the <see cref="AuditEventId"/> struct.

### AuditEventValidator (class)
Namespace: `Bravellian.Platform.Audit`

Validates audit events.

Members:
- `public static AuditValidationResult Validate(AuditEvent auditEvent, AuditValidationOptions? options = null)` — Validates the supplied audit event.

### AuditQuery (record)
Namespace: `Bravellian.Platform.Audit`

Defines a minimal audit event query.

Members:
- `public AuditQuery( IReadOnlyList<EventAnchor> anchors, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, string? name = null, int? limit = null)` — Initializes a new instance of the <see cref="AuditQuery"/> record.

### AuditValidationOptions (class)
Namespace: `Bravellian.Platform.Audit`

Validation options for audit events.

Members:
- `public int MaxDataJsonLength` — Gets or sets the maximum allowed size of the JSON payload in characters.

### AuditValidationResult (record)
Namespace: `Bravellian.Platform.Audit`

Represents the result of validating an audit event.

Members:
- `public AuditValidationResult(IReadOnlyList<string> errors)` — Initializes a new instance of the <see cref="AuditValidationResult"/> record.

### EventAnchor (record)
Namespace: `Bravellian.Platform.Audit`

Describes a stable anchor for querying audit events.

Members:
- `public EventAnchor(string anchorType, string anchorId, string role)` — Initializes a new instance of the <see cref="EventAnchor"/> record.

### EventOutcome (enum)
Namespace: `Bravellian.Platform.Audit`

Describes the outcome of an audit event.

Members:
- `Success` — Event completed successfully.
- `Failure` — Event failed.
- `Warning` — Event emitted a warning.
- `Info` — Informational event with no explicit success/failure.

### IAuditEventReader (interface)
Namespace: `Bravellian.Platform.Audit`

Reads audit events.

Members:
- (No public members found.)

### IAuditEventWriter (interface)
Namespace: `Bravellian.Platform.Audit`

Writes audit events.

Members:
- (No public members found.)

## Bravellian.Platform.Correlation

When to use: You want consistent correlation IDs across UI actions, inbox/outbox processing, webhooks, and operations.
How it works: CorrelationContext flows through headers and ambient accessors; serializers handle header dictionaries; scopes set ambient context.


### AmbientCorrelationContextAccessor (class)
Namespace: `Bravellian.Platform.Correlation`

Async-local correlation context accessor.

Members:
- (No public members found.)

### CorrelationContext (record)
Namespace: `Bravellian.Platform.Correlation`

Represents correlation identifiers for a single logical flow.

Members:
- `public CorrelationContext( CorrelationId correlationId, CorrelationId? causationId, string? traceId, string? spanId, DateTimeOffset createdAtUtc, IReadOnlyDictionary<string, string>? tags = null)` — Initializes a new instance of the <see cref="CorrelationContext"/> class.

### CorrelationHeaders (class)
Namespace: `Bravellian.Platform.Correlation`

Defines header keys used for correlation metadata.

Members:
- `public const string CorrelationId = "X-Correlation-Id";` — Header name for the correlation identifier.
- `public const string CausationId = "X-Causation-Id";` — Header name for the causation identifier.
- `public const string TraceId = "X-Trace-Id";` — Header name for the trace identifier.
- `public const string SpanId = "X-Span-Id";` — Header name for the span identifier.
- `public const string CreatedAtUtc = "X-Correlation-Created-At";` — Header name for the correlation creation timestamp in UTC.
- `public const string TagPrefix = "X-Correlation-Tag-";` — Prefix for correlation tag headers.

### CorrelationId (record struct)
Namespace: `Bravellian.Platform.Correlation`

Represents a stable correlation identifier.

Members:
- `public CorrelationId(string value)` — Initializes a new instance of the <see cref="CorrelationId"/> struct.

### CorrelationScope (class)
Namespace: `Bravellian.Platform.Correlation`

Sets the current correlation context for the lifetime of a scope.

Members:
- `public CorrelationScope(ICorrelationContextAccessor accessor, CorrelationContext context)` — Initializes a new instance of the <see cref="CorrelationScope"/> class.

### DefaultCorrelationIdGenerator (class)
Namespace: `Bravellian.Platform.Correlation`

Default correlation identifier generator using GUIDs.

Members:
- `public CorrelationId NewId()` — <inheritdoc />

### DefaultCorrelationSerializer (class)
Namespace: `Bravellian.Platform.Correlation`

Default serializer for correlation metadata.

Members:
- `public IReadOnlyDictionary<string, string> Serialize(CorrelationContext context)` — <inheritdoc />

### ICorrelationContextAccessor (interface)
Namespace: `Bravellian.Platform.Correlation`

Provides access to the ambient correlation context.

Members:
- (No public members found.)

### ICorrelationIdGenerator (interface)
Namespace: `Bravellian.Platform.Correlation`

Generates new correlation identifiers.

Members:
- (No public members found.)

### ICorrelationSerializer (interface)
Namespace: `Bravellian.Platform.Correlation`

Defines a serializer for correlation contexts.

Members:
- (No public members found.)

## Bravellian.Platform.Email

When to use: You need reliable, idempotent email delivery using an outbox pattern and provider adapters.
How it works: Enqueue OutboundEmailMessage to IEmailOutbox; processor claims outbox work, applies IEmailSendPolicy, sends via IOutboundEmailSender, and records delivery via IEmailDeliverySink with idempotency enforcement.


### EmailAddress (record)
Namespace: `Bravellian.Platform.Email`

Represents an email address with an optional display name.

Members:
- `public EmailAddress(string address, string? displayName = null)` — Initializes a new instance of the <see cref="EmailAddress"/> class.

### EmailAttachment (record)
Namespace: `Bravellian.Platform.Email`

Represents an email attachment.

Members:
- `public EmailAttachment(string fileName, string contentType, byte[] contentBytes, string? contentId = null)` — Initializes a new instance of the <see cref="EmailAttachment"/> class.

### EmailAuditEvents (class)
Namespace: `Bravellian.Platform.Email`

Emits audit events for outbound email operations.

Members:
- `public static Task EmitQueuedAsync( IPlatformEventEmitter? emitter, OutboundEmailMessage message, string? provider, CancellationToken cancellationToken)` — Emits an audit event for a queued email.

### EmailDeliveryAttempt (record)
Namespace: `Bravellian.Platform.Email`

Represents a delivery attempt for an outbound email.

Members:
- `public EmailDeliveryAttempt( int attemptNumber, DateTimeOffset timestampUtc, EmailDeliveryStatus status, string? providerMessageId = null, string? errorCode = null, string? errorMessage = null)` — Initializes a new instance of the <see cref="EmailDeliveryAttempt"/> class.

### EmailDeliveryStatus (enum)
Namespace: `Bravellian.Platform.Email`

Represents provider-neutral delivery states.

Members:
- `Queued = 0` — Message is queued for delivery.
- `Sent = 1` — Message was sent successfully.
- `FailedTransient = 2` — Message failed with a transient error.
- `FailedPermanent = 3` — Message failed with a permanent error.
- `Bounced = 4` — Message was bounced by the provider or recipient.
- `Suppressed = 5` — Message was suppressed by policy or provider rules.

### EmailDeliveryUpdate (record)
Namespace: `Bravellian.Platform.Email`

Represents an external email delivery update from a provider.

Members:
- (No public members found.)

### EmailFailureType (enum)
Namespace: `Bravellian.Platform.Email`

Represents the type of failure for a send attempt.

Members:
- `None = 0` — No failure occurred.
- `Transient = 1` — The failure is transient and may succeed on retry.
- `Permanent = 2` — The failure is permanent and should not be retried.

### EmailMessageValidator (class)
Namespace: `Bravellian.Platform.Email`

Validates outbound email messages.

Members:
- `public EmailMessageValidator(EmailValidationOptions? options = null)` — Initializes a new instance of the <see cref="EmailMessageValidator"/> class.

### EmailMetrics (class)
Namespace: `Bravellian.Platform.Email`

Metrics for email delivery.

Members:
- `public static void RecordQueued(OutboundEmailMessage message, string? provider)` — Records a queued email.

### EmailOutbox (class)
Namespace: `Bravellian.Platform.Email`

Default implementation of <see cref="IEmailOutbox"/>.

Members:
- `public EmailOutbox( IOutbox outbox, IEmailDeliverySink deliverySink, EmailMessageValidator? validator = null, EmailOutboxOptions? options = null)` — Initializes a new instance of the <see cref="EmailOutbox"/> class.

### EmailOutboxDefaults (class)
Namespace: `Bravellian.Platform.Email`

Default settings and helpers for the email outbox.

Members:
- `public const string Topic = "email.send";` — Default outbox topic used for outbound email messages.
- `public static TimeSpan DefaultBackoff(int attempt)` — Default exponential backoff with jitter.

### EmailOutboxDispatcher (class)
Namespace: `Bravellian.Platform.Email`

Default implementation of <see cref="IEmailOutboxDispatcher"/>.

Members:
- `public EmailOutboxDispatcher(IEmailOutboxStore outboxStore, IOutboundEmailSender sender)` — Initializes a new instance of the <see cref="EmailOutboxDispatcher"/> class.

### EmailOutboxDispatchResult (record)
Namespace: `Bravellian.Platform.Email`

Represents the outcome of a dispatch cycle.

Members:
- `public EmailOutboxDispatchResult( int attemptedCount, int succeededCount, int failedCount, int transientFailureCount)` — Initializes a new instance of the <see cref="EmailOutboxDispatchResult"/> class.

### EmailOutboxItem (record)
Namespace: `Bravellian.Platform.Email`

Represents a queued email outbox item.

Members:
- `public EmailOutboxItem( Guid id, string providerName, string messageKey, OutboundEmailMessage message, DateTimeOffset enqueuedAtUtc, DateTimeOffset? dueTimeUtc, int attemptCount)` — Initializes a new instance of the <see cref="EmailOutboxItem"/> class.

### EmailOutboxOptions (class)
Namespace: `Bravellian.Platform.Email`

Configures email outbox enqueue behavior.

Members:
- `public string Topic` — Gets or sets the outbox topic for outbound emails.

### EmailOutboxProcessor (class)
Namespace: `Bravellian.Platform.Email`

Processes outbound email messages stored in the platform outbox.

Members:
- `public EmailOutboxProcessor( IOutboxStore outboxStore, IOutboundEmailSender sender, IIdempotencyStore idempotencyStore, IEmailDeliverySink deliverySink, IOutboundEmailProbe? probe = null, IPlatformEventEmitter? eventEmitter = null, IEmailSendPolicy? policy = null, TimeProvider? timeProvider = null, EmailOutboxProcessorOptions? options = null)` — Initializes a new instance of the <see cref="EmailOutboxProcessor"/> class.

### EmailOutboxProcessorOptions (class)
Namespace: `Bravellian.Platform.Email`

Configures email outbox processing behavior.

Members:
- `public string Topic` — Gets or sets the outbox topic for outbound emails.
- `public int BatchSize` — Gets or sets the batch size for each processing cycle.
- `public int MaxAttempts` — Gets or sets the maximum number of attempts before permanently failing.
- `public Func<int, TimeSpan>? BackoffPolicy` — Gets or sets the retry backoff policy.

### EmailOutboxStatus (enum)
Namespace: `Bravellian.Platform.Email`

Represents the state of an outbox record.

Members:
- `Pending = 0` — Pending dispatch.
- `Processing = 1` — Currently processing.
- `Succeeded = 2` — Successfully dispatched.
- `Failed = 3` — Failed to dispatch.

### EmailPolicyOutcome (enum)
Namespace: `Bravellian.Platform.Email`

Represents the outcome of an email send policy evaluation.

Members:
- `Allow = 0` — Allow the send to proceed.
- `Delay = 1` — Delay the send until later.
- `Reject = 2` — Reject the send permanently.

### EmailProbeOutcome (enum)
Namespace: `Bravellian.Platform.Email`

Represents the outcome of an email probe operation.

Members:
- `Confirmed = 0` — The provider confirms delivery state.
- `NotFound = 1` — The provider reports the message was not found.
- `Unknown = 2` — The provider could not confirm delivery state.

### EmailProbeResult (record)
Namespace: `Bravellian.Platform.Email`

Represents the outcome of an outbound email probe.

Members:
- `public EmailProbeOutcome Outcome` — Gets the probe outcome.
- `public EmailDeliveryStatus? Status` — Gets the confirmed delivery status, if available.
- `public string? ProviderMessageId` — Gets the provider message identifier, if available.
- `public string? ErrorCode` — Gets the provider error code, if available.
- `public string? ErrorMessage` — Gets the provider error message, if available.
- `public static EmailProbeResult Confirmed( EmailDeliveryStatus status, string? providerMessageId = null, string? errorCode = null, string? errorMessage = null)` — Creates a confirmation result.

### EmailSendResult (record)
Namespace: `Bravellian.Platform.Email`

Represents the result of sending an outbound email.

Members:
- `public EmailDeliveryStatus Status` — Gets the delivery status.
- `public string? ProviderMessageId` — Gets the provider message identifier.
- `public EmailFailureType FailureType` — Gets the failure type.
- `public string? ErrorCode` — Gets the provider error code.
- `public string? ErrorMessage` — Gets the provider error message.
- `public static EmailSendResult Success(string? providerMessageId = null)` — Creates a successful send result.

### EmailValidationOptions (class)
Namespace: `Bravellian.Platform.Email`

Configures validation options for outbound email messages.

Members:
- `public long? MaxAttachmentBytes` — Gets or sets the maximum allowed size in bytes for a single attachment.
- `public long? MaxTotalAttachmentBytes` — Gets or sets the maximum allowed total size in bytes for all attachments.

### IEmailDeliverySink (interface)
Namespace: `Bravellian.Platform.Email`

Defines a sink for recording outbound email delivery events.

Members:
- (No public members found.)

### IEmailOutbox (interface)
Namespace: `Bravellian.Platform.Email`

Enqueues outbound emails for processing via the platform outbox.

Members:
- (No public members found.)

### IEmailOutboxDispatcher (interface)
Namespace: `Bravellian.Platform.Email`

Dispatches queued email outbox items.

Members:
- (No public members found.)

### IEmailOutboxProcessor (interface)
Namespace: `Bravellian.Platform.Email`

Processes outbox email messages.

Members:
- (No public members found.)

### IEmailOutboxStore (interface)
Namespace: `Bravellian.Platform.Email`

Defines storage for queued outbound email messages.

Members:
- (No public members found.)

### IEmailSendPolicy (interface)
Namespace: `Bravellian.Platform.Email`

Evaluates policy decisions for outbound email sends.

Members:
- (No public members found.)

### InMemoryEmailOutboxStore (class)
Namespace: `Bravellian.Platform.Email`

In-memory implementation of <see cref="IEmailOutboxStore"/> for testing and development.

Members:
- `public InMemoryEmailOutboxStore(TimeProvider? timeProvider = null)` — Initializes a new instance of the <see cref="InMemoryEmailOutboxStore"/> class.

### IOutboundEmailProbe (interface)
Namespace: `Bravellian.Platform.Email`

Probes providers to confirm whether an outbound email was delivered or accepted.

Members:
- (No public members found.)

### IOutboundEmailSender (interface)
Namespace: `Bravellian.Platform.Email`

Defines provider-specific outbound email senders.

Members:
- (No public members found.)

### NoOpEmailSendPolicy (class)
Namespace: `Bravellian.Platform.Email`

Default no-op policy that always allows sends.

Members:
- `public static NoOpEmailSendPolicy Instance` — Singleton instance.
- `public Task<PolicyDecision> EvaluateAsync(OutboundEmailMessage message, CancellationToken cancellationToken)` — <inheritdoc />

### OutboundEmailMessage (record)
Namespace: `Bravellian.Platform.Email`

Represents an outbound email message.

Members:
- `public OutboundEmailMessage( string messageKey, EmailAddress from, IEnumerable<EmailAddress> to, string subject, string? textBody = null, string? htmlBody = null, IEnumerable<EmailAddress>? cc = null, IEnumerable<EmailAddress>? bcc = null, EmailAddress? replyTo = null, IEnumerable<EmailAttachment>? attachments = null, IReadOnlyDictionary<string, string>? headers = null, IReadOnlyDictionary<string, string>? metadata = null, IEnumerable<string>? tags = null, DateTimeOffset? requestedSendAtUtc = null)` — Initializes a new instance of the <see cref="OutboundEmailMessage"/> class.

### PolicyDecision (record)
Namespace: `Bravellian.Platform.Email`

Represents a policy decision for sending an email.

Members:
- `public EmailPolicyOutcome Outcome` — Gets the policy outcome.
- `public string? Reason` — Gets the optional reason for the decision.
- `public DateTimeOffset? DelayUntilUtc` — Gets the time to delay until, when applicable.
- `public static PolicyDecision Allow()` — Creates an allow decision.

### ValidationResult (record)
Namespace: `Bravellian.Platform.Email`

Represents a validation outcome.

Members:
- `public bool Succeeded` — Gets a value indicating whether validation succeeded.
- `public IReadOnlyList<string> Errors` — Gets the validation errors.
- `public static ValidationResult Success()` — Creates a successful validation result.

## Bravellian.Platform.Email.AspNetCore

When to use: You are hosting email outbox processing inside an ASP.NET Core app.
How it works: Extension methods register the email core, provider adapters, and a hosted service loop for processing.


### EmailIdempotencyCleanupOptions (class)
Namespace: `Bravellian.Platform.Email.AspNetCore`

Configures idempotency cleanup for email sending.

Members:
- `public TimeSpan RetentionPeriod` — Gets or sets the retention period for idempotency records.
- `public TimeSpan CleanupInterval` — Gets or sets the interval between cleanup runs.

### EmailIdempotencyCleanupService (class)
Namespace: `Bravellian.Platform.Email.AspNetCore`

Background service that periodically cleans up old idempotency records.

Members:
- `public EmailIdempotencyCleanupService( IOptions<EmailIdempotencyCleanupOptions> options, IIdempotencyStoreProvider storeProvider, IMonotonicClock mono, ILogger<EmailIdempotencyCleanupService> logger, IDatabaseSchemaCompletion? schemaCompletion = null)` — Initializes a new instance of the <see cref="EmailIdempotencyCleanupService"/> class.

### EmailProcessingHostedService (class)
Namespace: `Bravellian.Platform.Email.AspNetCore`

Hosted service that periodically runs the email outbox processor.

Members:
- `public EmailProcessingHostedService( IEmailOutboxProcessor processor, IOptions<EmailProcessingOptions> options, ILogger<EmailProcessingHostedService> logger)` — Initializes a new instance of the <see cref="EmailProcessingHostedService"/> class.

### EmailProcessingOptions (class)
Namespace: `Bravellian.Platform.Email.AspNetCore`

Configuration options for email outbox processing.

Members:
- `public TimeSpan PollInterval` — Gets or sets the poll interval between processing runs.

### EmailServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Email.AspNetCore`

ASP.NET Core registration helpers for the email outbox.

Members:
- `public static IServiceCollection AddBravellianEmailCore( this IServiceCollection services, Action<EmailOutboxOptions>? configureOutboxOptions = null, Action<EmailOutboxProcessorOptions>? configureProcessorOptions = null, Action<EmailValidationOptions>? configureValidationOptions = null)` — Registers the core email outbox services.

## Bravellian.Platform.Email.Postmark

When to use: You want Postmark as the outbound email provider.
How it works: Provides IOutboundEmailSender implementation backed by Postmark HTTP APIs and Postmark options.


### IPostmarkEmailValidator (interface)
Namespace: `Bravellian.Platform.Email.Postmark`

Validates outbound email messages against Postmark requirements.

Members:
- (No public members found.)

### PostmarkEmailProbe (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark implementation of <see cref="IOutboundEmailProbe"/>.

Members:
- `public PostmarkEmailProbe(PostmarkOutboundMessageClient client)` — Initializes a new instance of the <see cref="PostmarkEmailProbe"/> class.

### PostmarkEmailSender (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark implementation of <see cref="IOutboundEmailSender"/>.

Members:
- `public PostmarkEmailSender(HttpClient httpClient, PostmarkOptions options) : this(httpClient, options, new PostmarkEmailValidator())` — Initializes a new instance of the <see cref="PostmarkEmailSender"/> class.

### PostmarkEmailValidator (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Validates outbound email messages against Postmark requirements.

Members:
- `public PostmarkEmailValidator(PostmarkValidationOptions? options = null)` — Initializes a new instance of the <see cref="PostmarkEmailValidator"/> class.

### PostmarkMessageDetailsLookup (record)
Namespace: `Bravellian.Platform.Email.Postmark`

Result for a message details query.

Members:
- `public bool IsFound` — TODO: add summary.

### PostmarkOptions (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Configures Postmark API settings.

Members:
- `public string ServerToken` — Gets or sets the Postmark server token.
- `public string? MessageStream` — Gets or sets the Postmark message stream.
- `public Uri BaseUrl` — Gets or sets the Postmark API base URL.

### PostmarkOutboundMessageClient (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Queries Postmark outbound message status for reconciliation.

Members:
- `public PostmarkOutboundMessageClient(HttpClient httpClient, PostmarkOptions options)` — Initializes a new instance of the <see cref="PostmarkOutboundMessageClient"/> class.

### PostmarkOutboundMessageDetails (record)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark outbound message details.

Members:
- `public string? MessageId` — TODO: add summary.
- `public string? Status` — TODO: add summary.
- `public IReadOnlyDictionary<string, string>? Metadata` — TODO: add summary.

### PostmarkOutboundSearchMessage (record)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark outbound search message.

Members:
- `public string? MessageId` — TODO: add summary.
- `public string? Status` — TODO: add summary.

### PostmarkOutboundSearchResponse (record)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark outbound search response.

Members:
- `public int TotalCount` — TODO: add summary.
- `public IReadOnlyList<PostmarkOutboundSearchMessage>? Messages` — TODO: add summary.

### PostmarkQueryStatus (enum)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark query status.

Members:
- `Found = 0` — TODO: add summary.
- `NotFound = 1` — TODO: add summary.
- `Error = 2` — TODO: add summary.

### PostmarkSearchLookup (record)
Namespace: `Bravellian.Platform.Email.Postmark`

Result for a metadata search query.

Members:
- `public bool IsFound` — TODO: add summary.

### PostmarkValidationOptions (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Configures Postmark-specific validation rules.

Members:
- `public long MaxMessageBytes` — Gets or sets the maximum total message size (body + base64 attachments), in bytes.
- `public long MaxBodyBytes` — Gets or sets the maximum size for each body (text or HTML), in bytes.
- `public long? MaxAttachmentBytes` — Gets or sets the maximum size for a single attachment (raw bytes).
- `public bool EnforceForbiddenExtensions` — Gets or sets a value indicating whether forbidden file extensions are rejected.
- `public IReadOnlyCollection<string> ForbiddenExtensions` — Gets or sets the forbidden file extensions.
- `public static IReadOnlyCollection<string> DefaultForbiddenExtensions` — Gets the default forbidden file extensions.

### PostmarkWebhookOptions (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Configures Postmark webhook authentication behavior.

Members:
- `public string ProviderName` — Gets or sets the provider name used in webhook envelopes.
- `public string? SigningSecret` — Gets or sets the signing secret used to validate Postmark signatures.
- `public string SignatureHeader` — Gets or sets the header containing the Postmark signature.
- `public string? SharedSecret` — Gets or sets the shared secret used for simple header authentication when signatures are unavailable.
- `public string SharedSecretHeader` — Gets or sets the header containing the shared secret value.

### PostmarkWebhookProvider (class)
Namespace: `Bravellian.Platform.Email.Postmark`

Postmark webhook provider implementation for delivery tracking events.

Members:
- `public const string DefaultProviderName = "postmark";` — Default provider name used for Postmark webhook registration.
- `public PostmarkWebhookProvider(IEmailDeliverySink deliverySink, PostmarkWebhookOptions? options = null) : this(deliverySink, null, options)` — Initializes a new instance of the <see cref="PostmarkWebhookProvider"/> class.

## Bravellian.Platform.HealthProbe

When to use: You want a deploy-time healthcheck CLI that exits with container-friendly codes.
How it works: HealthProbeApp runs when the app is invoked with healthcheck arguments, calls configured endpoints, and returns standardized exit codes.


### HealthProbeApp (class)
Namespace: `Bravellian.Platform.HealthProbe`

Command-line entry points for running health probes.

Members:
- `public static bool IsHealthCheckInvocation(string[] args)` — Determines whether the arguments represent a health check invocation.

### HealthProbeArgumentException (class)
Namespace: `Bravellian.Platform.HealthProbe`

Members:
- `public HealthProbeArgumentException()` — Initializes a new instance of the <see cref="HealthProbeArgumentException"/> class.

### HealthProbeOptions (class)
Namespace: `Bravellian.Platform.HealthProbe`

Configuration options for health probe execution.

Members:
- `public Uri? BaseUrl` — Gets or sets the base URL used to resolve relative endpoints.
- `public string? DefaultEndpoint` — Gets or sets the default endpoint name.
- `public IDictionary<string, string> Endpoints` — Gets the configured endpoint map keyed by endpoint name.
- `public TimeSpan Timeout` — Gets or sets the health probe timeout.
- `public string? ApiKey` — Gets or sets the API key used for authenticated probes.
- `public string ApiKeyHeaderName` — Gets or sets the header name for the API key.
- `public bool AllowInsecureTls` — Gets or sets a value indicating whether insecure TLS is allowed for probing.

### HealthProbeRequest (record)
Namespace: `Bravellian.Platform.HealthProbe`

Describes a resolved health probe request.

Members:
- (No public members found.)

### HealthProbeResult (class)
Namespace: `Bravellian.Platform.HealthProbe`

Represents the outcome of a health probe.

Members:
- `public HealthProbeResult(bool isHealthy, int exitCode, string message, HttpStatusCode? statusCode, TimeSpan duration)` — Initializes a new instance of the <see cref="HealthProbeResult"/> class.

### HostApplicationBuilderExtensions (class)
Namespace: `Bravellian.Platform.HealthProbe`

Extensions for configuring health probe services on host builders.

Members:
- `public static HostApplicationBuilder UseBravellianHealthProbe( this HostApplicationBuilder builder, Action<HealthProbeOptions>? configure = null)` — Adds health probe services to the host application builder.

### HttpHealthProbeRunner (class)
Namespace: `Bravellian.Platform.HealthProbe`

Runs HTTP-based health probes.

Members:
- `public HttpHealthProbeRunner( IHttpClientFactory httpClientFactory, ILogger<HttpHealthProbeRunner> logger, HealthProbeOptions options)` — Initializes a new instance of the <see cref="HttpHealthProbeRunner"/> class.

### IHealthProbeRunner (interface)
Namespace: `Bravellian.Platform.HealthProbe`

Executes health probe requests.

Members:
- (No public members found.)

### ServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.HealthProbe`

Extensions for registering health probe services.

Members:
- `public static IServiceCollection AddBravellianHealthProbe( this IServiceCollection services, Action<HealthProbeOptions>? configure = null)` — Registers health probe services in the dependency injection container.

## Bravellian.Platform.Idempotency

When to use: You must guarantee at-most-once execution per idempotency key across retries and worker restarts.
How it works: IIdempotencyStore provides TryBegin/Complete/Fail semantics; provider packages persist keys with lock durations.


### IdempotencyStoreRouter (class)
Namespace: `Bravellian.Platform.Idempotency`

Default implementation of <see cref="IIdempotencyStoreRouter"/>.

Members:
- `public IdempotencyStoreRouter(IIdempotencyStoreProvider storeProvider)` — Initializes a new instance of the <see cref="IdempotencyStoreRouter"/> class.

### IIdempotencyCleanupStore (interface)
Namespace: `Bravellian.Platform.Idempotency`

Provides cleanup capabilities for idempotency stores.

Members:
- (No public members found.)

### IIdempotencyStore (interface)
Namespace: `Bravellian.Platform.Idempotency`

Stores idempotency keys to prevent duplicate operations.

Members:
- (No public members found.)

### IIdempotencyStoreProvider (interface)
Namespace: `Bravellian.Platform.Idempotency`

Provides access to idempotency stores by key.

Members:
- (No public members found.)

### IIdempotencyStoreRouter (interface)
Namespace: `Bravellian.Platform.Idempotency`

Routes idempotency operations to a store by key.

Members:
- (No public members found.)

## Bravellian.Platform.Metrics.AspNetCore

When to use: You want Prometheus/OpenTelemetry metrics in an ASP.NET Core app.
How it works: Adds OpenTelemetry meters and optional Prometheus exporter with a mapped scrape endpoint.


### PlatformMetricsEndpointExtensions (class)
Namespace: `Bravellian.Platform.Metrics.AspNetCore`

Endpoint registration helpers for Prometheus scraping.

Members:
- `public static IEndpointConventionBuilder? MapPlatformMetricsEndpoint(this IEndpointRouteBuilder endpoints)` — Maps the Prometheus scraping endpoint on endpoint routing.

### PlatformMetricsOptions (class)
Namespace: `Bravellian.Platform.Metrics.AspNetCore`

Configures instrumentation and Prometheus exposure for ASP.NET Core.

Members:
- `public PlatformMeterOptions Meter` — Gets or sets options for the underlying meter.
- `public bool EnableAspNetCoreInstrumentation` — Gets or sets a value indicating whether ASP.NET Core instrumentation is enabled.
- `public bool EnableRuntimeInstrumentation` — Gets or sets a value indicating whether runtime instrumentation is enabled.
- `public bool EnableProcessInstrumentation` — Gets or sets a value indicating whether process instrumentation is enabled.
- `public bool EnablePrometheusExporter` — Gets or sets a value indicating whether the Prometheus exporter is enabled.
- `public string PrometheusEndpointPath` — Gets or sets the path for the Prometheus scrape endpoint.
- `public int PrometheusScrapeResponseCacheMilliseconds` — Gets or sets the cache duration (milliseconds) for scrape responses.

### PlatformMetricsServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Metrics.AspNetCore`

Service registration helpers for platform metrics in ASP.NET Core.

Members:
- `public static IServiceCollection AddPlatformMetrics( this IServiceCollection services, Action<PlatformMetricsOptions>? configure = null)` — Registers the meter provider and OpenTelemetry metrics pipeline.

## Bravellian.Platform.Metrics.HttpServer

When to use: You need a self-hosted Prometheus scrape endpoint outside ASP.NET Core.
How it works: PlatformMetricsHttpServer hosts an OpenTelemetry HTTP listener exporter with configurable prefixes and path.


### PlatformMetricsHttpServer (class)
Namespace: `Bravellian.Platform.Metrics.HttpServer`

Hosts a Prometheus scrape endpoint using the OpenTelemetry HTTP listener.

Members:
- `public PlatformMetricsHttpServer(PlatformMetricsHttpServerOptions options)` — Initializes the HTTP server using the provided options.

### PlatformMetricsHttpServerOptions (class)
Namespace: `Bravellian.Platform.Metrics.HttpServer`

Configures the self-hosted Prometheus metrics listener.

Members:
- `public PlatformMeterOptions Meter` — Gets the meter configuration used by the server.
- `public bool EnableRuntimeInstrumentation` — Gets a value indicating whether runtime instrumentation is enabled.
- `public bool EnableProcessInstrumentation` — Gets a value indicating whether process instrumentation is enabled.
- `public string[] UriPrefixes` — Gets the URI prefixes to listen on.
- `public string ScrapeEndpointPath` — Gets the path where the metrics endpoint is exposed.

## Bravellian.Platform.Modularity

When to use: You are building engine-first modules for UI, webhooks, or background workflows that need discovery and composition.
How it works: IModuleDefinition describes modules and their engines; descriptors/registries provide manifests and DI wiring.


### EngineKind (enum)
Namespace: `Bravellian.Platform.Modularity`

Supported engine types. Engines are framework-agnostic and must not depend on transport concerns.

Members:
- `Ui` — UI-first engines that produce view models and navigation outcomes.
- `Webhook` — Webhook engines that react to external callbacks.

### IModuleDefinition (interface)
Namespace: `Bravellian.Platform.Modularity`

Base contract for all modules.

Members:
- (No public members found.)

### IModuleEngineDescriptor (interface)
Namespace: `Bravellian.Platform.Modularity`

Base abstraction for an engine descriptor. Implementations should remain transport agnostic.

Members:
- (No public members found.)

### IModuleWebhookEngine (interface)
Namespace: `Bravellian.Platform.Modularity`

Webhook engine contract for module webhook handling.

Members:
- (No public members found.)

### IRequiredServiceValidator (interface)
Namespace: `Bravellian.Platform.Modularity`

Validates that required engine services are available for a host.

Members:
- (No public members found.)

### IUiEngine (interface)
Namespace: `Bravellian.Platform.Modularity`

Generic UI engine contract that operates on DTOs and produces view models.

Members:
- (No public members found.)

### ModuleEngineAdapterHints (record)
Namespace: `Bravellian.Platform.Modularity`

Adapter-level hints used by hosts to wire up engines to transports.

Members:
- (No public members found.)

### ModuleEngineCapabilities (record)
Namespace: `Bravellian.Platform.Modularity`

Declares the actions and events an engine can process.

Members:
- (No public members found.)

### ModuleEngineCompatibility (record)
Namespace: `Bravellian.Platform.Modularity`

Compatibility metadata for engine evolution.

Members:
- (No public members found.)

### ModuleEngineDescriptor (record)
Namespace: `Bravellian.Platform.Modularity`

Strongly typed engine descriptor registered by a module. Factories are provided by modules and consumed by adapters/hosts.

Members:
- `public Type ContractType` — Gets the contract type implemented by the engine.
- `public object? Create(IServiceProvider serviceProvider)` — Creates the engine instance from the service provider.

### ModuleEngineDiscoveryService (class)
Namespace: `Bravellian.Platform.Modularity`

Members:
- `public IReadOnlyCollection<IModuleEngineDescriptor> List()` — Lists all engines registered by modules.
- `public IReadOnlyCollection<IModuleEngineDescriptor> List(EngineKind? kind, string? featureArea = null)` — Lists engines filtered by kind or feature area.

### ModuleEngineManifest (record)
Namespace: `Bravellian.Platform.Modularity`

Describes a module engine in a transport-agnostic manner.

Members:
- (No public members found.)

### ModuleEngineNavigationHints (record)
Namespace: `Bravellian.Platform.Modularity`

Navigation hints are abstract tokens adapters map to routes, dialogs, or screens.

Members:
- (No public members found.)

### ModuleEngineSchema (record)
Namespace: `Bravellian.Platform.Modularity`

Declares schema hints for engine inputs or outputs.

Members:
- (No public members found.)

### ModuleEngineSecurity (record)
Namespace: `Bravellian.Platform.Modularity`

Security metadata used by adapters for validation.

Members:
- (No public members found.)

### ModuleEngineWebhookMetadata (record)
Namespace: `Bravellian.Platform.Modularity`

Webhook event metadata advertised by webhook engines.

Members:
- (No public members found.)

### ModuleHealthCheckBuilder (class)
Namespace: `Bravellian.Platform.Modularity`

Provides a bridge between module health checks and host builders.

Members:
- `public void AddCheck(string name, Func<HealthCheckResult> check, IEnumerable<string>? tags = null)` — Adds a health check for the module.

### ModuleHealthCheckRegistration (record)
Namespace: `Bravellian.Platform.Modularity`

Captures a module health check for hosts that do not wire ASP.NET Core.

Members:
- (No public members found.)

### ModuleNavigationToken (record)
Namespace: `Bravellian.Platform.Modularity`

Typed navigation token an engine can emit. Adapters map these to concrete routes, dialogs, or screens.

Members:
- (No public members found.)

### ModuleRegistry (class)
Namespace: `Bravellian.Platform.Modularity`

Registry of module types and initialized instances.

Members:
- `public static void RegisterModule<T>() where T : class, IModuleDefinition, new()` — Registers a module type.

### ModuleServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Modularity`

Registration helpers for modules.

Members:
- `public static IServiceCollection AddModuleServices( this IServiceCollection services, IConfiguration configuration, ILoggerFactory? loggerFactory = null)` — Registers services for modules and engine discovery.

### ModuleSignatureAlgorithm (enum)
Namespace: `Bravellian.Platform.Modularity`

Enumerates supported signature algorithms for webhook verification.

Members:
- `None = 0` — No signature verification is required.
- `HmacSha256 = 1` — HMAC SHA-256 signature.
- `HmacSha512 = 2` — HMAC SHA-512 signature.
- `RsaSha256 = 3` — RSA SHA-256 signature.

### ModuleWebhookAuthenticatorContext (record)
Namespace: `Bravellian.Platform.Modularity`

Context for building a module webhook authenticator.

Members:
- (No public members found.)

### ModuleWebhookOptions (class)
Namespace: `Bravellian.Platform.Modularity`

Options for integrating modular webhook engines with the webhook pipeline.

Members:
- `public const string DefaultEventTypeHeaderName = "X-Bravellian-Webhook-EventType";` — Default header used to pass event type information from endpoint routing to the classifier.
- `public string EventTypeHeaderName` — Header name used to pass the event type into the webhook classifier.
- `public JsonSerializerOptions? SerializerOptions` — Serializer options used when deserializing webhook payloads for engines.
- `public ICollection<Func<ModuleWebhookAuthenticatorContext, IWebhookAuthenticator>> Authenticators` — Optional authenticators that must succeed before webhook processing continues.

### ModuleWebhookProviderRegistry (class)
Namespace: `Bravellian.Platform.Modularity`

Webhook provider registry that exposes module webhook engines through the webhook pipeline.

Members:
- `public ModuleWebhookProviderRegistry(ModuleEngineDiscoveryService discovery, IServiceProvider services, ModuleWebhookOptions? options = null)` — Initializes a new instance of the <see cref="ModuleWebhookProviderRegistry"/> class.

### ModuleWebhookRequest (record)
Namespace: `Bravellian.Platform.Modularity`

Request envelope provided to module webhook engines.

Members:
- (No public members found.)

### ModuleWebhookServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Modularity`

Registration helpers for modular webhook integration.

Members:
- `public static IServiceCollection AddModuleWebhookProviders( this IServiceCollection services, Action<ModuleWebhookOptions>? configure = null)` — Registers the module webhook provider registry for the webhook ingestion pipeline.

### NavigationTargetKind (enum)
Namespace: `Bravellian.Platform.Modularity`

Enumerates the supported navigation targets that hosts can map to runtime concepts.

Members:
- `Route = 0` — Navigate to an in-app route.
- `Dialog = 1` — Open a modal or dialog surface.
- `Component = 2` — Render a component in the host UI.
- `External = 3` — Navigate to an external URL.

### UiAdapterResponse (record)
Namespace: `Bravellian.Platform.Modularity`

Adapter response used by UI hosts. Navigation tokens are mapped to routes/pages by the host.

Members:
- (No public members found.)

### UiEngineAdapter (class)
Namespace: `Bravellian.Platform.Modularity`

Adapter that maps UI engine contracts to host navigation tokens.

Members:
- `public UiEngineAdapter(ModuleEngineDiscoveryService discoveryService, IServiceProvider services)` — Initializes a new instance of the <see cref="UiEngineAdapter"/> class.

### UiEngineResult (record)
Namespace: `Bravellian.Platform.Modularity`

UI engine result containing the view model and optional navigation tokens/events.

Members:
- (No public members found.)

## Bravellian.Platform.Modularity.AspNetCore

When to use: You are hosting modularity components in ASP.NET Core.
How it works: Adds ASP.NET Core registration helpers and bindings for module engine hosting.


### ModuleEndpointRouteBuilderExtensions (class)
Namespace: `Bravellian.Platform.Modularity`

ASP.NET Core endpoint helpers for module engines.

Members:
- `public static IEndpointConventionBuilder MapUiEngineEndpoints( this IEndpointRouteBuilder endpoints, Action<UiEngineEndpointOptions>? configure = null)` — Maps a generic UI engine endpoint that uses engine manifests to deserialize inputs.

### UiEngineEndpointOptions (class)
Namespace: `Bravellian.Platform.Modularity`

Options for mapping UI engine endpoints.

Members:
- `public string RoutePattern` — Route pattern for UI engine execution.
- `public string ModuleKeyRouteParameterName` — Route parameter name for the module key.
- `public string EngineIdRouteParameterName` — Route parameter name for the engine id.
- `public string? InputSchemaName` — Optional schema name to select the input type.
- `public string? OutputSchemaName` — Optional schema name to select the output type.
- `public JsonSerializerOptions? SerializerOptions` — Overrides the JSON serializer options.
- `public Func<object, IResult>? ResponseFactory` — Custom response mapping for UI adapter results.

### WebhookEndpointOptions (class)
Namespace: `Bravellian.Platform.Modularity`

Options for mapping webhook engine endpoints.

Members:
- `public string RoutePattern` — Route pattern for webhook intake.
- `public string ProviderRouteParameterName` — Route parameter name for the provider.
- `public string EventTypeRouteParameterName` — Route parameter name for the event type.
- `public string EventTypeHeaderName` — Header name used to pass the event type into the webhook ingestion pipeline.

## Bravellian.Platform.Modularity.Razor

When to use: You need Razor UI modules integrated with the modularity engine.
How it works: IRazorModule extends module definitions and configures Razor pages/areas for UI engines.


### IRazorModule (interface)
Namespace: `Bravellian.Platform.Modularity`

Module that provides Razor Pages UI on top of module engines.

Members:
- (No public members found.)

### RazorModuleServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Modularity`

Razor Pages helpers for modules that expose UI adapters.

Members:
- `public static IMvcBuilder ConfigureRazorModulePages( this IMvcBuilder builder, ILoggerFactory? loggerFactory = null)` — Adds Razor Pages configuration for registered Razor modules.

## Bravellian.Platform.Observability

When to use: You want consistent audit/operation/metric conventions across platform subsystems.
How it works: Supplies standard event names, tag keys, and helpers like PlatformEventEmitter to emit coordinated signals.


### IPlatformEventEmitter (interface)
Namespace: `Bravellian.Platform.Observability`

Emits platform events that coordinate audit and operation tracking.

Members:
- (No public members found.)

### ObservationAnchor (record)
Namespace: `Bravellian.Platform.Observability`

Represents an anchor for linking observability records.

Members:
- `public ObservationAnchor(string type, string value)` — Initializes a new instance of the <see cref="ObservationAnchor"/> record.

### PlatformEventEmitter (class)
Namespace: `Bravellian.Platform.Observability`

Default implementation of <see cref="IPlatformEventEmitter"/>.

Members:
- `public PlatformEventEmitter( IAuditEventWriter auditWriter, IOperationTracker operationTracker, ICorrelationContextAccessor? correlationAccessor = null, TimeProvider? timeProvider = null)` — Initializes a new instance of the <see cref="PlatformEventEmitter"/> class.

### PlatformEventNames (class)
Namespace: `Bravellian.Platform.Observability`

Standard event names for platform observability.

Members:
- `public const string OutboxMessageProcessed = "outbox.message.processed";` — Audit event emitted when an outbox message is processed.
- `public const string WebhookReceived = "webhook.received";` — Audit event emitted when a webhook is received.
- `public const string EmailSent = "email.sent";` — Audit event emitted when an email is sent.
- `public const string EmailQueued = "email.queued";` — Audit event emitted when an email is queued.
- `public const string EmailAttempted = "email.attempted";` — Audit event emitted when an email send attempt occurs.
- `public const string EmailFailed = "email.failed";` — Audit event emitted when an email fails.
- `public const string EmailSuppressed = "email.suppressed";` — Audit event emitted when an email is suppressed.
- `public const string EmailBounced = "email.bounced";` — Audit event emitted when an email bounces.
- `public const string OperationStarted = "operation.started";` — Audit event emitted when an operation starts.
- `public const string OperationCompleted = "operation.completed";` — Audit event emitted when an operation completes.
- `public const string OperationFailed = "operation.failed";` — Audit event emitted when an operation fails.

### PlatformTagKeys (class)
Namespace: `Bravellian.Platform.Observability`

Standard tag keys for platform observability.

Members:
- `public const string Tenant = "tenant";` — Tag for the tenant identifier.
- `public const string Partition = "partition";` — Tag for the logical partition identifier.
- `public const string Provider = "provider";` — Tag for provider identifiers (email/webhook/etc).
- `public const string MessageKey = "messageKey";` — Tag for a stable message key (idempotency key).
- `public const string OperationId = "operationId";` — Tag for an operation identifier.
- `public const string OutboxMessageId = "outboxMessageId";` — Tag for an outbox message identifier.
- `public const string InboxMessageId = "inboxMessageId";` — Tag for an inbox message identifier.
- `public const string WebhookEventId = "webhookEventId";` — Tag for a webhook event identifier.

## Bravellian.Platform.Operations

When to use: You need to track long-running or multi-step operations with progress and status.
How it works: IOperationTracker records snapshots and events; IOperationWatcher identifies stalled operations for alerting.


### IOperationTracker (interface)
Namespace: `Bravellian.Platform.Operations`

Tracks long-running operations.

Members:
- (No public members found.)

### IOperationWatcher (interface)
Namespace: `Bravellian.Platform.Operations`

Watches for stalled operations.

Members:
- (No public members found.)

### OperationEvent (record)
Namespace: `Bravellian.Platform.Operations`

Represents an append-only event emitted by an operation.

Members:
- `public OperationEvent( OperationId operationId, DateTimeOffset occurredAtUtc, string kind, string message, string? dataJson = null)` — Initializes a new instance of the <see cref="OperationEvent"/> record.

### OperationId (record struct)
Namespace: `Bravellian.Platform.Operations`

Represents the identifier of an operation.

Members:
- `public OperationId(string value)` — Initializes a new instance of the <see cref="OperationId"/> struct.

### OperationScope (class)
Namespace: `Bravellian.Platform.Operations`

Provides a scope that starts and completes an operation.

Members:
- `public OperationId OperationId` — Gets the operation identifier.
- `public static async Task<OperationScope> StartAsync( IOperationTracker tracker, string name, CorrelationContext? correlationContext = null, OperationId? parentOperationId = null, IReadOnlyDictionary<string, string>? tags = null, string? successMessage = null, CancellationToken cancellationToken = default)` — Starts a new operation and returns a scope that completes it on disposal.

### OperationSnapshot (record)
Namespace: `Bravellian.Platform.Operations`

Represents the current snapshot of a long-running operation.

Members:
- `public OperationSnapshot( OperationId operationId, string name, OperationStatus status, DateTimeOffset startedAtUtc, DateTimeOffset updatedAtUtc, DateTimeOffset? completedAtUtc = null, double? percentComplete = null, string? message = null, CorrelationContext? correlation = null, OperationId? parentOperationId = null, IReadOnlyDictionary<string, string>? tags = null)` — Initializes a new instance of the <see cref="OperationSnapshot"/> record.

### OperationStatus (enum)
Namespace: `Bravellian.Platform.Operations`

Describes the lifecycle state of an operation.

Members:
- `Pending` — Operation is pending execution.
- `Running` — Operation is currently running.
- `Succeeded` — Operation completed successfully.
- `Failed` — Operation failed.
- `Canceled` — Operation was canceled.
- `Stalled` — Operation is stalled.

### OperationTrackerExtensions (class)
Namespace: `Bravellian.Platform.Operations`

Provides helper methods for operation tracking.

Members:
- `public static async Task RecordFailureAsync( this IOperationTracker tracker, OperationId operationId, Exception exception, string? message = null, CancellationToken cancellationToken = default)` — Records a failure event and completes the operation as failed.

## Bravellian.Platform.Postgres

When to use: You want PostgreSQL-backed implementations of platform primitives.
How it works: Registers Postgres stores for outbox/inbox/scheduler/fanout/leases/metrics and optional schema deployment.


### ConfiguredSchedulerStoreProvider (class)
Namespace: `Bravellian.Platform`

Provides access to a pre-configured list of scheduler stores. Each scheduler store represents a separate database/tenant.

Members:
- `public ConfiguredSchedulerStoreProvider( IEnumerable<SchedulerDatabaseConfig> configs, TimeProvider timeProvider, ILoggerFactory loggerFactory)` — Initializes a new instance of the <see cref="ConfiguredSchedulerStoreProvider"/> class.

### DynamicSchedulerStoreProvider (class)
Namespace: `Bravellian.Platform`

Provides access to multiple scheduler stores that are discovered dynamically at runtime. This implementation queries an ISchedulerDatabaseDiscovery service to detect new or removed databases and manages the lifecycle of scheduler stores accordingly.

Members:
- `public DynamicSchedulerStoreProvider( ISchedulerDatabaseDiscovery discovery, TimeProvider timeProvider, ILoggerFactory loggerFactory, ILogger<DynamicSchedulerStoreProvider> logger, TimeSpan? refreshInterval = null)` — Initializes a new instance of the <see cref="DynamicSchedulerStoreProvider"/> class.

### InboxCleanupService (class)
Namespace: `Bravellian.Platform`

Background service that periodically cleans up old processed inbox messages based on the configured retention period.

Members:
- `public InboxCleanupService( IOptions<PostgresInboxOptions> options, IMonotonicClock mono, ILogger<InboxCleanupService> logger, IDatabaseSchemaCompletion? schemaCompletion = null)` — Initializes a new instance of the <see cref="InboxCleanupService"/> class.

### OutboxCleanupService (class)
Namespace: `Bravellian.Platform`

Background service that periodically cleans up old processed outbox messages based on the configured retention period.

Members:
- `public OutboxCleanupService( IOptions<PostgresOutboxOptions> options, IMonotonicClock mono, ILogger<OutboxCleanupService> logger, IDatabaseSchemaCompletion? schemaCompletion = null)` — Initializes a new instance of the <see cref="OutboxCleanupService"/> class.

### PostgresAuditEventReader (class)
Namespace: `Bravellian.Platform`

PostgreSQL implementation of <see cref="IAuditEventReader"/>.

Members:
- `public PostgresAuditEventReader(IOptions<PostgresAuditOptions> options, ILogger<PostgresAuditEventReader> logger)` — Initializes a new instance of the <see cref="PostgresAuditEventReader"/> class.

### PostgresAuditEventWriter (class)
Namespace: `Bravellian.Platform`

PostgreSQL implementation of <see cref="IAuditEventWriter"/>.

Members:
- `public PostgresAuditEventWriter(IOptions<PostgresAuditOptions> options, ILogger<PostgresAuditEventWriter> logger)` — Initializes a new instance of the <see cref="PostgresAuditEventWriter"/> class.

### PostgresAuditOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for PostgreSQL-backed audit events.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string AuditEventsTable` — Gets or sets the table name for audit events. Defaults to "AuditEvents".
- `public string AuditAnchorsTable` — Gets or sets the table name for audit anchors. Defaults to "AuditAnchors".
- `public AuditValidationOptions ValidationOptions` — Gets or sets validation options for audit events.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### PostgresAuditServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for Postgres audit storage.

Members:
- `public static IServiceCollection AddPostgresAudit( this IServiceCollection services, PostgresAuditOptions options)` — Adds Postgres audit storage using the specified options.

### PostgresDapperTypeHandlerRegistration (class)
Namespace: `Bravellian.Platform`

Provides registration methods for all Dapper type handlers for strongly-typed IDs.

Members:
- `public static void RegisterTypeHandlers()` — Registers all Dapper type handlers for strongly-typed ID types. This method is idempotent and thread-safe.

### PostgresEmailDeliveryOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for PostgreSQL-backed email delivery logging.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for email delivery events. Defaults to "EmailDeliveryEvents".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### PostgresEmailDeliveryServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for PostgreSQL email delivery logging.

Members:
- `public static IServiceCollection AddPostgresEmailDelivery( this IServiceCollection services, PostgresEmailDeliveryOptions options)` — Adds PostgreSQL email delivery logging with the specified options.

### PostgresEmailDeliverySink (class)
Namespace: `Bravellian.Platform`

PostgreSQL implementation of <see cref="IEmailDeliverySink"/>.

Members:
- `public PostgresEmailDeliverySink( IOptions<PostgresEmailDeliveryOptions> options, TimeProvider timeProvider, ICorrelationContextAccessor? correlationAccessor, ILogger<PostgresEmailDeliverySink> logger)` — Initializes a new instance of the <see cref="PostgresEmailDeliverySink"/> class.

### PostgresEmailOutboxOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for PostgreSQL-backed email outbox storage.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for email outbox items. Defaults to "EmailOutbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### PostgresEmailOutboxServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for PostgreSQL email outbox storage.

Members:
- `public static IServiceCollection AddPostgresEmailOutbox( this IServiceCollection services, PostgresEmailOutboxOptions options)` — Adds PostgreSQL email outbox storage with the specified options.

### PostgresEmailOutboxStore (class)
Namespace: `Bravellian.Platform`

PostgreSQL implementation of <see cref="IEmailOutboxStore"/>.

Members:
- `public PostgresEmailOutboxStore( IOptions<PostgresEmailOutboxOptions> options, TimeProvider timeProvider, ILogger<PostgresEmailOutboxStore> logger)` — Initializes a new instance of the <see cref="PostgresEmailOutboxStore"/> class.

### PostgresFanoutOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for SQL-based fanout services. Specifies connection string and table names for fanout storage.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for fanout tables.
- `public string PolicyTableName` — Gets or sets the table name for fanout policies.
- `public string CursorTableName` — Gets or sets the table name for fanout cursors.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether gets or sets whether to automatically deploy database schema.

### PostgresFanoutServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for configuring fanout services with the DI container.

Members:
- `public static IServiceCollection AddMultiPostgresFanout( this IServiceCollection services, IEnumerable<PostgresFanoutOptions> fanoutOptions)` — Adds SQL multi-fanout functionality with support for processing across multiple databases. This enables a single worker to process fanout operations from multiple customer databases.

### PostgresIdempotencyOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the Postgres idempotency store.

Members:
- `public string ConnectionString` — Gets or sets the Postgres connection string.
- `public string SchemaName` — Gets or sets the schema name (default: "infra").
- `public string TableName` — Gets or sets the idempotency table name (default: "Idempotency").
- `public TimeSpan LockDuration` — Gets or sets the lock duration for in-progress keys.
- `public Func<string, TimeSpan>? LockDurationProvider` — Gets or sets an optional lock duration provider for per-key customization.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether schema deployment should run at startup.

### PostgresIdempotencyServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for Postgres idempotency stores.

Members:
- `public static IServiceCollection AddPostgresIdempotency( this IServiceCollection services, PostgresIdempotencyOptions options)` — Adds Postgres idempotency tracking with the specified options.

### PostgresInboxOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the Postgres inbox.

Members:
- `public string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name for the inbox table. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for the inbox. Defaults to "Inbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.
- `public int MaxAttempts` — Gets or sets the maximum number of retry attempts for failed messages. Defaults to 5.
- `public int LeaseSeconds` — Gets or sets the lease duration in seconds for claimed messages. Defaults to 30 seconds.
- `public TimeSpan RetentionPeriod` — Gets or sets the retention period for processed inbox messages. Messages older than this period will be deleted during cleanup. Defaults to 7 days.
- `public bool EnableAutomaticCleanup` — Gets or sets a value indicating whether automatic cleanup of old processed messages is enabled. When true, a background service will periodically delete processed messages older than RetentionPeriod. Defaults to true.
- `public TimeSpan CleanupInterval` — Gets or sets the interval at which the cleanup job runs. Defaults to 1 hour.

### PostgresLeaseApi (class)
Namespace: `Bravellian.Platform`

Provides data access operations for the lease functionality.

Members:
- `public PostgresLeaseApi(string connectionString, string schemaName = "infra")` — Initializes a new instance of the <see cref="PostgresLeaseApi"/> class.

### PostgresLeaseRunner (class)
Namespace: `Bravellian.Platform`

Members:
- `public string LeaseName` — Gets the name of the lease.
- `public string Owner` — Gets the owner identifier.
- `public bool IsLost` — Gets a value indicating whether the lease has been lost.
- `public CancellationToken CancellationToken` — Gets a cancellation token that is canceled when the lease is lost or disposed.
- `public static async Task<PostgresLeaseRunner?> AcquireAsync( PostgresLeaseApi leaseApi, IMonotonicClock monotonicClock, TimeProvider timeProvider, string leaseName, string owner, TimeSpan leaseDuration, double renewPercent = 0.6, ILogger? logger = null, CancellationToken cancellationToken = default)` — Acquires a lease and returns a lease runner that will automatically renew it.

### PostgresLeaseServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for registering lease services with the service collection.

Members:
- `public static IServiceCollection AddSystemLeases(this IServiceCollection services, PostgresSystemLeaseOptions options)` — Adds system lease functionality with SQL Server backend.

### PostgresMetricsExporterOptions (class)
Namespace: `Bravellian.Platform.Metrics`

Configuration options for the metrics exporter.

Members:
- `public bool Enabled` — Gets or sets a value indicating whether the metrics exporter is enabled.
- `public TimeSpan FlushInterval` — Gets or sets the interval for minute aggregation and flush.
- `public int ReservoirSize` — Gets or sets the size of the reservoir for percentile calculation.
- `public bool EnableCentralRollup` — Gets or sets a value indicating whether central hourly rollups are enabled.
- `public int MinuteRetentionDays` — Gets or sets the retention period for minute data in days.
- `public int HourlyRetentionDays` — Gets or sets the retention period for hourly data in days.
- `public string ServiceName` — Gets or sets the service name for this instance.
- `public string? CentralConnectionString` — Gets or sets the connection string for the central database (for hourly rollups).
- `public string SchemaName` — Gets or sets the schema name for metrics tables (default: "infra").

### PostgresMetricsServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Metrics`

Extension methods for registering metrics exporter services.

Members:
- `public static IServiceCollection AddMetricsExporter( this IServiceCollection services, Action<PostgresMetricsExporterOptions>? configure = null)` — Adds the metrics exporter service to the service collection.

### PostgresOperationOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for PostgreSQL-backed operation tracking.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string OperationsTable` — Gets or sets the table name for operations. Defaults to "Operations".
- `public string OperationEventsTable` — Gets or sets the table name for operation events. Defaults to "OperationEvents".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### PostgresOperationServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for PostgreSQL operation tracking.

Members:
- `public static IServiceCollection AddPostgresOperations( this IServiceCollection services, PostgresOperationOptions options)` — Adds PostgreSQL operation tracking using the specified options.

### PostgresOperationTracker (class)
Namespace: `Bravellian.Platform`

PostgreSQL implementation of <see cref="IOperationTracker"/>.

Members:
- `public PostgresOperationTracker( IOptions<PostgresOperationOptions> options, TimeProvider timeProvider, ILogger<PostgresOperationTracker> logger)` — Initializes a new instance of the <see cref="PostgresOperationTracker"/> class.

### PostgresOperationWatcher (class)
Namespace: `Bravellian.Platform`

PostgreSQL implementation of <see cref="IOperationWatcher"/>.

Members:
- `public PostgresOperationWatcher( IOptions<PostgresOperationOptions> options, TimeProvider timeProvider, ILogger<PostgresOperationWatcher> logger)` — Initializes a new instance of the <see cref="PostgresOperationWatcher"/> class.

### PostgresOutboxOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the Postgres outbox.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name for the outbox table. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for the outbox. Defaults to "Outbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.
- `public TimeSpan RetentionPeriod` — Gets or sets the retention period for processed outbox messages. Messages older than this period will be deleted during cleanup. Defaults to 7 days.
- `public bool EnableAutomaticCleanup` — Gets or sets a value indicating whether automatic cleanup of old processed messages is enabled. When true, a background service will periodically delete processed messages older than RetentionPeriod. Defaults to true.
- `public TimeSpan CleanupInterval` — Gets or sets the interval at which the cleanup job runs. Defaults to 1 hour.
- `public TimeSpan LeaseDuration` — Gets or sets the duration for which claimed messages are locked before they can be claimed again. Defaults to 5 minutes (300 seconds).

### PostgresPlatformFeatureServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Unified feature registration helpers that wire multi-database providers through <see cref="IPlatformDatabaseDiscovery"/> and <see cref="PlatformConfiguration"/>. These helpers mirror the registrations used by <see cref="PostgresPlatformServiceCollectionExtensions"/> so that individual features can participate in discovery-first environments without re-implementing feature-specific discovery interfaces.

Members:
- `public static IServiceCollection AddPlatformOutbox( this IServiceCollection services, string tableName = "Outbox", bool enableSchemaDeployment = false)` — Registers multi-database Outbox services backed by <see cref="IPlatformDatabaseDiscovery"/>.

### PostgresPlatformOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for registering the Postgres platform stack in one call.

Members:
- `public required string ConnectionString` — Gets or sets the PostgreSQL connection string.
- `public string SchemaName` — Gets or sets the schema name (default: "infra").
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether schema deployment should run at startup.
- `public bool EnableSchedulerWorkers` — Gets or sets a value indicating whether scheduler background workers should run.
- `public Action<PostgresOutboxOptions>? ConfigureOutbox` — Optional outbox options customization.
- `public Action<PostgresInboxOptions>? ConfigureInbox` — Optional inbox options customization.
- `public Action<PostgresSchedulerOptions>? ConfigureScheduler` — Optional scheduler options customization.
- `public Action<PostgresFanoutOptions>? ConfigureFanout` — Optional fanout options customization.
- `public Action<PostgresIdempotencyOptions>? ConfigureIdempotency` — Optional idempotency options customization.
- `public Action<PostgresMetricsExporterOptions>? ConfigureMetrics` — Optional metrics exporter options customization.
- `public Action<PostgresAuditOptions>? ConfigureAudit` — Optional audit options customization.
- `public Action<PostgresOperationOptions>? ConfigureOperations` — Optional operations options customization.
- `public Action<PostgresEmailOutboxOptions>? ConfigureEmailOutbox` — Optional email outbox options customization.
- `public Action<PostgresEmailDeliveryOptions>? ConfigureEmailDelivery` — Optional email delivery options customization.

### PostgresPlatformServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for unified platform registration.

Members:
- `public static IServiceCollection AddPostgresPlatform( this IServiceCollection services, string connectionString, Action<PostgresPlatformOptions>? configure = null)` — Registers all Postgres-backed platform storage components using a single connection string. Includes Operations, Audit, Email (outbox + delivery), Webhooks/Observability dependencies, and shared platform services.

### PostgresSchedulerOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the Postgres scheduler.

Members:
- `public const string SectionName = "PostgresScheduler";` — The configuration section name for scheduler options.
- `public string ConnectionString` — Gets or sets the database connection string for the scheduler.
- `public TimeSpan MaxPollingInterval` — Gets or sets the maximum time the scheduler will sleep before re-checking for new jobs, even if the next scheduled job is far in the future. Recommended: 30 seconds.
- `public bool EnableBackgroundWorkers` — Gets or sets a value indicating whether if true, the background IHostedService workers (PostgresSchedulerService, OutboxProcessor) will be registered and started. Set to false for environments where you only want to schedule jobs (e.g., in a web API) but not execute them.
- `public string SchemaName` — Gets or sets the database schema name for all scheduler tables. Defaults to "infra".
- `public string JobsTableName` — Gets or sets the table name for jobs. Defaults to "Jobs".
- `public string JobRunsTableName` — Gets or sets the table name for job runs. Defaults to "JobRuns".
- `public string TimersTableName` — Gets or sets the table name for timers. Defaults to "Timers".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### PostgresSchedulerServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for Postgres scheduler, outbox, and fanout services.

Members:
- `public static IServiceCollection AddPostgresOutbox(this IServiceCollection services, PostgresOutboxOptions options)` — Adds SQL outbox functionality to the service collection using the specified options. Configures outbox options, registers multi-outbox infrastructure, cleanup and schema deployment services as needed.

### PostgresSystemLeaseOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for system leases.

Members:
- `public string? ConnectionString` — Gets or sets the connection string for the distributed lock database.
- `public string SchemaName` — Gets or sets the schema name for the distributed lock table. Default is "infra".
- `public TimeSpan DefaultLeaseDuration` — Gets or sets the default lease duration for new leases. Default is 30 seconds.
- `public double RenewPercent` — Gets or sets the percentage of the lease duration at which renewal should occur. Default is 0.6 (60%).
- `public bool UseGate` — Gets or sets a value indicating whether to use a short advisory-lock gate to reduce contention. Default is false.
- `public int GateTimeoutMs` — Gets or sets the timeout in milliseconds for the advisory-lock gate. Default is 200ms.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

## Bravellian.Platform.SqlServer

When to use: You want SQL Server-backed implementations of platform primitives.
How it works: Registers SQL Server stores for outbox/inbox/scheduler/fanout/leases/metrics and optional schema deployment.


### ConfiguredSchedulerStoreProvider (class)
Namespace: `Bravellian.Platform`

Provides access to a pre-configured list of scheduler stores. Each scheduler store represents a separate database/tenant.

Members:
- `public ConfiguredSchedulerStoreProvider( IEnumerable<SchedulerDatabaseConfig> configs, TimeProvider timeProvider, ILoggerFactory loggerFactory, IPlatformEventEmitter? eventEmitter = null)` — Initializes a new instance of the <see cref="ConfiguredSchedulerStoreProvider"/> class.

### DapperTypeHandlerRegistration (class)
Namespace: `Bravellian.Platform`

Provides registration methods for all Dapper type handlers for strongly-typed IDs.

Members:
- `public static void RegisterTypeHandlers()` — Registers all Dapper type handlers for strongly-typed ID types. This method is idempotent and thread-safe.

### DynamicSchedulerStoreProvider (class)
Namespace: `Bravellian.Platform`

Provides access to multiple scheduler stores that are discovered dynamically at runtime. This implementation queries an ISchedulerDatabaseDiscovery service to detect new or removed databases and manages the lifecycle of scheduler stores accordingly.

Members:
- `public DynamicSchedulerStoreProvider( ISchedulerDatabaseDiscovery discovery, TimeProvider timeProvider, ILoggerFactory loggerFactory, ILogger<DynamicSchedulerStoreProvider> logger, TimeSpan? refreshInterval = null, IPlatformEventEmitter? eventEmitter = null)` — Initializes a new instance of the <see cref="DynamicSchedulerStoreProvider"/> class.

### FanoutServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for configuring fanout services with the DI container.

Members:
- `public static IServiceCollection AddMultiSqlFanout( this IServiceCollection services, IEnumerable<SqlFanoutOptions> fanoutOptions)` — Adds SQL multi-fanout functionality with support for processing across multiple databases. This enables a single worker to process fanout operations from multiple customer databases.

### IdempotencyServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for SQL Server idempotency stores.

Members:
- `public static IServiceCollection AddSqlIdempotency( this IServiceCollection services, SqlIdempotencyOptions options)` — Adds SQL Server idempotency tracking with the specified options.

### InboxCleanupService (class)
Namespace: `Bravellian.Platform`

Background service that periodically cleans up old processed inbox messages based on the configured retention period.

Members:
- `public InboxCleanupService( IOptions<SqlInboxOptions> options, IMonotonicClock mono, ILogger<InboxCleanupService> logger, IDatabaseSchemaCompletion? schemaCompletion = null)` — Initializes a new instance of the <see cref="InboxCleanupService"/> class.

### LeaseApi (class)
Namespace: `Bravellian.Platform`

Provides data access operations for the lease functionality.

Members:
- `public LeaseApi(string connectionString, string schemaName = "infra")` — Initializes a new instance of the <see cref="LeaseApi"/> class.

### LeaseRunner (class)
Namespace: `Bravellian.Platform`

Members:
- `public string LeaseName` — Gets the name of the lease.
- `public string Owner` — Gets the owner identifier.
- `public bool IsLost` — Gets a value indicating whether the lease has been lost.
- `public CancellationToken CancellationToken` — Gets a cancellation token that is canceled when the lease is lost or disposed.
- `public static async Task<LeaseRunner?> AcquireAsync( LeaseApi leaseApi, IMonotonicClock monotonicClock, TimeProvider timeProvider, string leaseName, string owner, TimeSpan leaseDuration, double renewPercent = 0.6, ILogger? logger = null, CancellationToken cancellationToken = default)` — Acquires a lease and returns a lease runner that will automatically renew it.

### LeaseServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for registering lease services with the service collection.

Members:
- `public static IServiceCollection AddSystemLeases(this IServiceCollection services, SystemLeaseOptions options)` — Adds system lease functionality with SQL Server backend.

### MetricsExporterOptions (class)
Namespace: `Bravellian.Platform.Metrics`

Configuration options for the metrics exporter.

Members:
- `public bool Enabled` — Gets or sets a value indicating whether the metrics exporter is enabled.
- `public TimeSpan FlushInterval` — Gets or sets the interval for minute aggregation and flush.
- `public int ReservoirSize` — Gets or sets the size of the reservoir for percentile calculation.
- `public bool EnableCentralRollup` — Gets or sets a value indicating whether central hourly rollups are enabled.
- `public int MinuteRetentionDays` — Gets or sets the retention period for minute data in days.
- `public int HourlyRetentionDays` — Gets or sets the retention period for hourly data in days.
- `public string ServiceName` — Gets or sets the service name for this instance.
- `public string? CentralConnectionString` — Gets or sets the connection string for the central database (for hourly rollups).
- `public string SchemaName` — Gets or sets the schema name for metrics tables (default: "infra").

### MetricsServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Metrics`

Extension methods for registering metrics exporter services.

Members:
- `public static IServiceCollection AddMetricsExporter( this IServiceCollection services, Action<MetricsExporterOptions>? configure = null)` — Adds the metrics exporter service to the service collection.

### OutboxCleanupService (class)
Namespace: `Bravellian.Platform`

Background service that periodically cleans up old processed outbox messages based on the configured retention period.

Members:
- `public OutboxCleanupService( IOptions<SqlOutboxOptions> options, IMonotonicClock mono, ILogger<OutboxCleanupService> logger, IDatabaseSchemaCompletion? schemaCompletion = null)` — Initializes a new instance of the <see cref="OutboxCleanupService"/> class.

### PlatformFeatureServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Unified feature registration helpers that wire multi-database providers through <see cref="IPlatformDatabaseDiscovery"/> and <see cref="PlatformConfiguration"/>. These helpers mirror the registrations used by <see cref="PlatformServiceCollectionExtensions"/> so that individual features can participate in discovery-first environments without re-implementing feature-specific discovery interfaces.

Members:
- `public static IServiceCollection AddPlatformOutbox( this IServiceCollection services, string tableName = "Outbox", bool enableSchemaDeployment = false)` — Registers multi-database Outbox services backed by <see cref="IPlatformDatabaseDiscovery"/>.

### PlatformServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Extension methods for unified platform registration.

Members:
- `public static IServiceCollection AddPlatformMultiDatabaseWithList( this IServiceCollection services, IEnumerable<PlatformDatabase> databases, bool enableSchemaDeployment = false)` — Registers the platform for a multi-database environment without control plane. Features run across the provided list of databases using round-robin scheduling. For single database scenarios, pass a list with one database.

### SchedulerServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for SQL Server scheduler, outbox, and fanout services.

Members:
- `public static IServiceCollection AddSqlOutbox(this IServiceCollection services, SqlOutboxOptions options)` — Adds SQL outbox functionality to the service collection using the specified options. Configures outbox options, registers multi-outbox infrastructure, cleanup and schema deployment services as needed.

### SqlAuditEventReader (class)
Namespace: `Bravellian.Platform`

SQL Server implementation of <see cref="IAuditEventReader"/>.

Members:
- `public SqlAuditEventReader(IOptions<SqlAuditOptions> options, ILogger<SqlAuditEventReader> logger)` — Initializes a new instance of the <see cref="SqlAuditEventReader"/> class.

### SqlAuditEventWriter (class)
Namespace: `Bravellian.Platform`

SQL Server implementation of <see cref="IAuditEventWriter"/>.

Members:
- `public SqlAuditEventWriter(IOptions<SqlAuditOptions> options, ILogger<SqlAuditEventWriter> logger)` — Initializes a new instance of the <see cref="SqlAuditEventWriter"/> class.

### SqlAuditOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for SQL Server-backed audit events.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string AuditEventsTable` — Gets or sets the table name for audit events. Defaults to "AuditEvents".
- `public string AuditAnchorsTable` — Gets or sets the table name for audit anchors. Defaults to "AuditAnchors".
- `public AuditValidationOptions ValidationOptions` — Gets or sets validation options for audit events.

### SqlAuditSchemaScripts (class)
Namespace: `Bravellian.Platform`

Provides SQL scripts for SQL Server audit tables.

Members:
- `public static IReadOnlyList<string> GetScripts( string schemaName, string auditEventsTable, string auditAnchorsTable)` — Returns the embedded schema scripts with variables applied.

### SqlEmailOutboxOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for SQL Server-backed email outbox storage.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for email outbox items. Defaults to "EmailOutbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### SqlEmailOutboxServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

Service collection extensions for SQL Server email outbox storage.

Members:
- `public static IServiceCollection AddSqlEmailOutbox( this IServiceCollection services, SqlEmailOutboxOptions options)` — Adds SQL Server email outbox storage with the specified options.

### SqlEmailOutboxStore (class)
Namespace: `Bravellian.Platform`

SQL Server implementation of <see cref="IEmailOutboxStore"/>.

Members:
- `public SqlEmailOutboxStore( IOptions<SqlEmailOutboxOptions> options, TimeProvider timeProvider, ILogger<SqlEmailOutboxStore> logger)` — Initializes a new instance of the <see cref="SqlEmailOutboxStore"/> class.

### SqlExternalSideEffectOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for SQL Server external side effects.

Members:
- `public string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name.
- `public string TableName` — Gets or sets the table name for external side effects.

### SqlFanoutOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for SQL-based fanout services. Specifies connection string and table names for fanout storage.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the schema name for fanout tables.
- `public string PolicyTableName` — Gets or sets the table name for fanout policies.
- `public string CursorTableName` — Gets or sets the table name for fanout cursors.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether gets or sets whether to automatically deploy database schema.

### SqlIdempotencyOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the SQL Server idempotency store.

Members:
- `public string ConnectionString` — Gets or sets the SQL Server connection string.
- `public string SchemaName` — Gets or sets the schema name (default: "infra").
- `public string TableName` — Gets or sets the idempotency table name (default: "Idempotency").
- `public TimeSpan LockDuration` — Gets or sets the lock duration for in-progress keys.
- `public Func<string, TimeSpan>? LockDurationProvider` — Gets or sets an optional lock duration provider for per-key customization.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether schema deployment should run at startup.

### SqlInboxOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the SQL Server inbox.

Members:
- `public string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name for the inbox table. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for the inbox. Defaults to "Inbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.
- `public int MaxAttempts` — Gets or sets the maximum number of retry attempts for failed messages. Defaults to 5.
- `public int LeaseSeconds` — Gets or sets the lease duration in seconds for claimed messages. Defaults to 30 seconds.
- `public TimeSpan RetentionPeriod` — Gets or sets the retention period for processed inbox messages. Messages older than this period will be deleted during cleanup. Defaults to 7 days.
- `public bool EnableAutomaticCleanup` — Gets or sets a value indicating whether automatic cleanup of old processed messages is enabled. When true, a background service will periodically delete processed messages older than RetentionPeriod. Defaults to true.
- `public TimeSpan CleanupInterval` — Gets or sets the interval at which the cleanup job runs. Defaults to 1 hour.

### SqlOperationOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for SQL Server-backed operation tracking.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name. Defaults to "infra".
- `public string OperationsTable` — Gets or sets the table name for operations. Defaults to "Operations".
- `public string OperationEventsTable` — Gets or sets the table name for operation events. Defaults to "OperationEvents".

### SqlOperationSchemaScripts (class)
Namespace: `Bravellian.Platform`

Provides SQL scripts for SQL Server operation tracking tables.

Members:
- `public static IReadOnlyList<string> GetScripts( string schemaName, string operationsTable, string operationEventsTable)` — Returns the embedded schema scripts with variables applied.

### SqlOperationTracker (class)
Namespace: `Bravellian.Platform`

SQL Server implementation of <see cref="IOperationTracker"/>.

Members:
- `public SqlOperationTracker( IOptions<SqlOperationOptions> options, TimeProvider timeProvider, ILogger<SqlOperationTracker> logger)` — Initializes a new instance of the <see cref="SqlOperationTracker"/> class.

### SqlOperationWatcher (class)
Namespace: `Bravellian.Platform`

SQL Server implementation of <see cref="IOperationWatcher"/>.

Members:
- `public SqlOperationWatcher( IOptions<SqlOperationOptions> options, TimeProvider timeProvider, ILogger<SqlOperationWatcher> logger)` — Initializes a new instance of the <see cref="SqlOperationWatcher"/> class.

### SqlOutboxOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the SQL Server outbox.

Members:
- `public required string ConnectionString` — Gets or sets the database connection string.
- `public string SchemaName` — Gets or sets the database schema name for the outbox table. Defaults to "infra".
- `public string TableName` — Gets or sets the table name for the outbox. Defaults to "Outbox".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.
- `public TimeSpan RetentionPeriod` — Gets or sets the retention period for processed outbox messages. Messages older than this period will be deleted during cleanup. Defaults to 7 days.
- `public bool EnableAutomaticCleanup` — Gets or sets a value indicating whether automatic cleanup of old processed messages is enabled. When true, a background service will periodically delete processed messages older than RetentionPeriod. Defaults to true.
- `public TimeSpan CleanupInterval` — Gets or sets the interval at which the cleanup job runs. Defaults to 1 hour.
- `public TimeSpan LeaseDuration` — Gets or sets the duration for which claimed messages are locked before they can be claimed again. Defaults to 5 minutes (300 seconds).

### SqlPlatformOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for registering the SQL Server platform stack in one call.

Members:
- `public required string ConnectionString` — Gets or sets the SQL Server connection string.
- `public string SchemaName` — Gets or sets the schema name (default: "infra").
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether schema deployment should run at startup.
- `public bool EnableSchedulerWorkers` — Gets or sets a value indicating whether scheduler background workers should run.
- `public Action<SqlOutboxOptions>? ConfigureOutbox` — Optional outbox options customization.
- `public Action<SqlInboxOptions>? ConfigureInbox` — Optional inbox options customization.
- `public Action<SqlSchedulerOptions>? ConfigureScheduler` — Optional scheduler options customization.
- `public Action<SqlFanoutOptions>? ConfigureFanout` — Optional fanout options customization.
- `public Action<SqlIdempotencyOptions>? ConfigureIdempotency` — Optional idempotency options customization.
- `public Action<SqlExternalSideEffectOptions>? ConfigureExternalSideEffects` — Optional external side-effect options customization.
- `public Action<MetricsExporterOptions>? ConfigureMetrics` — Optional metrics exporter options customization.
- `public Action<SqlAuditOptions>? ConfigureAudit` — Optional audit options customization.
- `public Action<SqlOperationOptions>? ConfigureOperations` — Optional operations options customization.
- `public Action<SqlEmailOutboxOptions>? ConfigureEmailOutbox` — Optional email outbox options customization.

### SqlPlatformServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform`

SQL Server provider registration helpers for the full platform stack.

Members:
- `public static IServiceCollection AddSqlPlatform( this IServiceCollection services, string connectionString, Action<SqlPlatformOptions>? configure = null)` — Registers all SQL Server-backed platform storage components using a single connection string. Includes Operations, Audit, Email outbox, Webhooks/Observability dependencies, and shared platform services.

### SqlSchedulerOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for the SQL Server scheduler.

Members:
- `public const string SectionName = "SqlScheduler";` — The configuration section name for scheduler options.
- `public string ConnectionString` — Gets or sets the database connection string for the scheduler.
- `public TimeSpan MaxPollingInterval` — Gets or sets the maximum time the scheduler will sleep before re-checking for new jobs, even if the next scheduled job is far in the future. Recommended: 30 seconds.
- `public bool EnableBackgroundWorkers` — Gets or sets a value indicating whether if true, the background IHostedService workers (SqlSchedulerService, OutboxProcessor) will be registered and started. Set to false for environments where you only want to schedule jobs (e.g., in a web API) but not execute them.
- `public string SchemaName` — Gets or sets the database schema name for all scheduler tables. Defaults to "infra".
- `public string JobsTableName` — Gets or sets the table name for jobs. Defaults to "Jobs".
- `public string JobRunsTableName` — Gets or sets the table name for job runs. Defaults to "JobRuns".
- `public string TimersTableName` — Gets or sets the table name for timers. Defaults to "Timers".
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

### SystemLeaseOptions (class)
Namespace: `Bravellian.Platform`

Configuration options for system leases.

Members:
- `public string? ConnectionString` — Gets or sets the connection string for the distributed lock database.
- `public string SchemaName` — Gets or sets the schema name for the distributed lock table. Default is "infra".
- `public TimeSpan DefaultLeaseDuration` — Gets or sets the default lease duration for new leases. Default is 30 seconds.
- `public double RenewPercent` — Gets or sets the percentage of the lease duration at which renewal should occur. Default is 0.6 (60%).
- `public bool UseGate` — Gets or sets a value indicating whether gets or sets whether to use the short sp_getapplock gate to reduce contention. Default is false.
- `public int GateTimeoutMs` — Gets or sets the timeout in milliseconds for the sp_getapplock gate. Default is 200ms.
- `public bool EnableSchemaDeployment` — Gets or sets a value indicating whether database schema deployment should be performed automatically. When true, the required database schema will be created/updated on startup. Defaults to true.

## Bravellian.Platform.Webhooks

When to use: You need provider-agnostic webhook ingestion with authentication, classification, and durable processing.
How it works: Ingest builds WebhookEnvelope, authenticates/classifies, enqueues into inbox with dedupe; processor claims and runs handlers with retries.


### AuthResult (record)
Namespace: `Bravellian.Platform.Webhooks`

Authentication outcome for a webhook request.

Members:
- (No public members found.)

### ClassifyResult (record)
Namespace: `Bravellian.Platform.Webhooks`

Classification outcome for a webhook request.

Members:
- (No public members found.)

### IWebhookAuthenticator (interface)
Namespace: `Bravellian.Platform.Webhooks`

Authenticates incoming webhook requests.

Members:
- (No public members found.)

### IWebhookClassifier (interface)
Namespace: `Bravellian.Platform.Webhooks`

Classifies webhook requests and extracts metadata needed for processing.

Members:
- (No public members found.)

### IWebhookHandler (interface)
Namespace: `Bravellian.Platform.Webhooks`

Handles classified webhook events.

Members:
- (No public members found.)

### IWebhookIngestor (interface)
Namespace: `Bravellian.Platform.Webhooks`

Ingests webhook requests into the processing pipeline.

Members:
- (No public members found.)

### IWebhookPartitionResolver (interface)
Namespace: `Bravellian.Platform.Webhooks`

Resolves optional partition keys for webhook requests.

Members:
- (No public members found.)

### IWebhookProcessor (interface)
Namespace: `Bravellian.Platform.Webhooks`

Processes webhook events from the inbox queue.

Members:
- (No public members found.)

### IWebhookProvider (interface)
Namespace: `Bravellian.Platform.Webhooks`

Describes a webhook provider with its authentication, classification, and handling capabilities.

Members:
- (No public members found.)

### IWebhookProviderRegistry (interface)
Namespace: `Bravellian.Platform.Webhooks`

Registry for webhook providers.

Members:
- (No public members found.)

### ProcessingResult (record)
Namespace: `Bravellian.Platform.Webhooks`

Result of processing a webhook event.

Members:
- (No public members found.)

### WebhookDedupe (class)
Namespace: `Bravellian.Platform.Webhooks`

Helper methods for generating webhook dedupe keys.

Members:
- `public static WebhookDedupeResult Create(string provider, string? providerEventId, byte[]? bodyBytes)` — Creates a dedupe key using the provider event identifier when available, or a SHA-256 hash of the body when it is not.

### WebhookDedupeResult (record struct)
Namespace: `Bravellian.Platform.Webhooks`

Dedupe key result with strength indicator.

Members:
- (No public members found.)

### WebhookEnvelope (record)
Namespace: `Bravellian.Platform.Webhooks`

Members:
- (No public members found.)

### WebhookEventContext (record)
Namespace: `Bravellian.Platform.Webhooks`

Members:
- (No public members found.)

### WebhookEventRecord (record)
Namespace: `Bravellian.Platform.Webhooks`

Members:
- (No public members found.)

### WebhookEventStatus (enum)
Namespace: `Bravellian.Platform.Webhooks`

Represents the processing state of a webhook event.

Members:
- `Pending` — Event is pending processing.
- `Processing` — Event is currently being processed.
- `Completed` — Event completed successfully.
- `FailedRetryable` — Event failed but will be retried.
- `Poisoned` — Event exceeded retry limits and is marked as poison.
- `Rejected` — Event was rejected and should not be processed.

### WebhookIngestDecision (enum)
Namespace: `Bravellian.Platform.Webhooks`

Indicates how the webhook ingress pipeline should treat the incoming request.

Members:
- `Accepted` — Accept the webhook for further processing.
- `Ignored` — Ignore the webhook without further processing.
- `Rejected` — Reject the webhook as invalid or unauthorized.

### WebhookIngestor (class)
Namespace: `Bravellian.Platform.Webhooks`

Default implementation of <see cref="IWebhookIngestor"/>.

Members:
- `public WebhookIngestor( IWebhookProviderRegistry providerRegistry, IInbox inbox, TimeProvider? timeProvider = null, WebhookOptions? options = null, IInboxRouter? inboxRouter = null, IWebhookPartitionResolver? partitionResolver = null)` — Initializes a new instance of the <see cref="WebhookIngestor"/> class.

### WebhookIngestResult (record)
Namespace: `Bravellian.Platform.Webhooks`

Result from webhook ingestion that drives the HTTP response and downstream handling.

Members:
- (No public members found.)

### WebhookMissingHandlerBehavior (enum)
Namespace: `Bravellian.Platform.Webhooks`

Defines how the processor should behave when no handler is registered.

Members:
- `Complete` — Treat missing handlers as completed and acknowledge the message.
- `Retry` — Treat missing handlers as retryable failures.
- `Poison` — Mark messages as poisoned immediately when no handler is found.

### WebhookOptions (class)
Namespace: `Bravellian.Platform.Webhooks`

Configuration options for webhook ingestion behavior.

Members:
- `public bool StoreRejected` — Gets or sets a value indicating whether rejected webhook requests should be stored.
- `public bool RedactRejectedBody` — Gets or sets a value indicating whether rejected webhook payloads should be redacted.
- `public Action<WebhookIngestResult, WebhookEnvelope>? OnIngested` — Gets or sets a callback invoked after ingestion completes.
- `public Action<ProcessingResult, WebhookEventContext>? OnProcessed` — Gets or sets a callback invoked after processing completes.
- `public Action<string?, WebhookEnvelope, WebhookIngestResult?>? OnRejected` — Gets or sets a callback invoked when a webhook is rejected.

### WebhookProcessor (class)
Namespace: `Bravellian.Platform.Webhooks`

Default processor for webhook inbox messages.

Members:
- `public WebhookProcessor( IInboxWorkStore workStore, IWebhookProviderRegistry providerRegistry, WebhookProcessorOptions? options = null, WebhookOptions? webhookOptions = null)` — Initializes a new instance of the <see cref="WebhookProcessor"/> class.

### WebhookProcessorOptions (class)
Namespace: `Bravellian.Platform.Webhooks`

Configuration options for webhook processing.

Members:
- `public int BatchSize` — Gets or sets the maximum number of messages to claim per iteration.
- `public int LeaseSeconds` — Gets or sets the lease duration in seconds for claimed messages.
- `public int MaxAttempts` — Gets or sets the maximum number of attempts before poisoning a message.
- `public TimeSpan BaseBackoff` — Gets or sets the base backoff used for exponential retry delays.
- `public TimeSpan MaxBackoff` — Gets or sets the maximum backoff delay for retries.
- `public WebhookMissingHandlerBehavior MissingHandlerBehavior` — Gets or sets the behavior when no handler is registered for an event type.

### WebhookProviderBase (class)
Namespace: `Bravellian.Platform.Webhooks`

Base class for composing webhook providers from their parts.

Members:
- `public abstract string Name` — <inheritdoc />
- `public IWebhookAuthenticator Authenticator` — <inheritdoc />
- `public IWebhookClassifier Classifier` — <inheritdoc />
- `public IReadOnlyList<IWebhookHandler> Handlers` — <inheritdoc />

### WebhookProviderRegistry (class)
Namespace: `Bravellian.Platform.Webhooks`

Default implementation of <see cref="IWebhookProviderRegistry"/> backed by an enumerable set of providers.

Members:
- `public WebhookProviderRegistry(IEnumerable<IWebhookProvider> providers)` — Initializes a new instance of the <see cref="WebhookProviderRegistry"/> class.

### WebhookTelemetryEvents (class)
Namespace: `Bravellian.Platform.Webhooks`

Common telemetry event names for webhook processing.

Members:
- `public const string IngestAccepted = "webhooks.ingest.accepted";` — Webhook ingest accepted event name.
- `public const string IngestIgnored = "webhooks.ingest.ignored";` — Webhook ingest ignored event name.
- `public const string IngestRejected = "webhooks.ingest.rejected";` — Webhook ingest rejected event name.
- `public const string IngestDuplicate = "webhooks.ingest.duplicate";` — Webhook ingest duplicate event name.
- `public const string ProcessCompleted = "webhooks.process.completed";` — Webhook processing completed event name.
- `public const string ProcessRetryScheduled = "webhooks.process.retry";` — Webhook processing retry scheduled event name.
- `public const string ProcessPoisoned = "webhooks.process.poisoned";` — Webhook processing poisoned event name.
- `public const string ProcessRejected = "webhooks.process.rejected";` — Webhook processing rejected event name.

## Bravellian.Platform.Webhooks.AspNetCore

When to use: You want ASP.NET Core endpoint helpers for the webhook pipeline.
How it works: Adds DI wiring and endpoint helpers to capture raw requests and hand them to the core pipeline.


### WebhookEndpoint (class)
Namespace: `Bravellian.Platform.Webhooks.AspNetCore`

Helpers for exposing webhook endpoints.

Members:
- `public static async Task<IResult> HandleAsync( HttpContext context, string providerName, IWebhookIngestor ingestor, CancellationToken cancellationToken)` — Handles an inbound webhook request and returns the appropriate HTTP result.

### WebhookProcessingHostedService (class)
Namespace: `Bravellian.Platform.Webhooks.AspNetCore`

Hosted service that periodically runs the webhook processor.

Members:
- `public WebhookProcessingHostedService( IWebhookProcessor processor, IOptions<WebhookProcessingOptions> options, ILogger<WebhookProcessingHostedService> logger)` — Initializes a new instance of the <see cref="WebhookProcessingHostedService"/> class.

### WebhookProcessingOptions (class)
Namespace: `Bravellian.Platform.Webhooks.AspNetCore`

Options for webhook processing hosted service scheduling.

Members:
- `public TimeSpan PollInterval` — Gets or sets the polling interval between processing runs.
- `public int BatchSize` — Gets or sets the batch size for each processing run.
- `public int MaxAttempts` — Gets or sets the maximum number of attempts before poisoning a message.

### WebhookServiceCollectionExtensions (class)
Namespace: `Bravellian.Platform.Webhooks.AspNetCore`

Service registration extensions for webhook ingestion and processing.

Members:
- `public static IServiceCollection AddBravellianWebhooks( this IServiceCollection services, Action<WebhookOptions>? configureOptions = null)` — Registers Bravellian webhook services.





