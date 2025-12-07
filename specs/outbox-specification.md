# Outbox Component - Functional Specification

## 1. Meta

| Property | Value |
|----------|-------|
| **Component** | Outbox |
| **Version** | 1.0 |
| **Status** | Active |
| **Owner** | Bravellian Platform Team |
| **Last Updated** | 2025-12-07 |

## 2. Purpose and Scope

### 2.1 Purpose

The Outbox component implements the [Transactional Outbox pattern](https://microservices.io/patterns/data/transactional-outbox.html) to ensure reliable, at-least-once message delivery in distributed systems. It solves the dual-write problem by guaranteeing that database changes and message publishing happen atomically.

### 2.2 Core Responsibilities

1. **Transactional Enqueuing**: Accept messages within database transactions, ensuring atomicity with business operations
2. **Reliable Processing**: Process enqueued messages asynchronously using a work queue pattern with claim-ack-abandon semantics
3. **Message Routing**: Route messages to appropriate handlers based on topic
4. **Failure Handling**: Implement automatic retry with exponential backoff for transient failures
5. **Multi-Database Support**: Process messages across multiple databases in multi-tenant scenarios
6. **Join/Fan-In Coordination**: Coordinate multiple related messages and trigger follow-up actions when all complete

### 2.3 Scope

**In Scope:**
- Transactional message enqueueing (standalone and within existing transactions)
- Work queue message processing with leases
- Handler-based message routing and processing
- Automatic retry with configurable backoff policies
- Multi-database/multi-tenant message processing
- Join/fan-in coordination for workflow orchestration
- Message scheduling with due times
- SQL Server backend implementation

**Out of Scope:**
- Direct message broker integration (handled by user-provided handlers)
- Message transformation or enrichment (handled by user-provided handlers)
- Exactly-once delivery semantics (provides at-least-once; handlers must be idempotent)
- Message ordering guarantees within a topic
- Non-SQL Server backends (future consideration)
- Message priority queuing (future consideration)

## 3. Non-Goals

This component does NOT:

1. **Replace Message Brokers**: The Outbox is not a general-purpose message broker. It coordinates local database writes with eventual message delivery.
2. **Guarantee Exactly-Once Processing**: The component provides at-least-once delivery semantics. Handlers must be idempotent.
3. **Provide Ordered Message Delivery**: Messages may be processed in any order, regardless of creation time or topic.
4. **Support Cross-Database Transactions**: Each message is enqueued in a single database; joins across databases are not currently supported.
5. **Implement Business Logic**: The Outbox dispatches messages; all business logic lives in handler implementations.

## 4. Key Concepts and Terms

### 4.1 Core Entities

- **Message**: A unit of work to be processed asynchronously, consisting of a topic, payload, and metadata
- **Topic**: A string identifier used to route messages to appropriate handlers (e.g., "order.created", "email.send")
- **Payload**: The message content, typically serialized as JSON
- **Correlation ID**: An optional identifier to trace messages back to their source or group related messages
- **Handler**: An implementation of `IOutboxHandler` that processes messages for a specific topic

### 4.2 Work Queue Semantics

- **Claim**: Atomically reserve messages for processing with a time-bounded lease
- **Lease**: A time-limited lock on a message, preventing other workers from processing it
- **Acknowledge (Ack)**: Mark a message as successfully processed and remove it from the queue
- **Abandon**: Release a message's lease and return it to ready state for retry (used for transient failures)
- **Fail**: Permanently mark a message as failed (used for permanent errors after max retries)
- **Reap**: Recover messages whose leases have expired due to worker crashes or timeouts

### 4.3 Multi-Database Concepts

- **Outbox Store**: A single database instance containing an Outbox table
- **Store Provider**: Manages access to multiple outbox stores (one per database/tenant)
- **Selection Strategy**: Algorithm for choosing which store to poll next (e.g., round-robin, drain-first)
- **Router**: Routes write operations to the correct outbox store based on a routing key (e.g., tenant ID)

### 4.4 Join/Fan-In Concepts

- **Join**: A coordination primitive that tracks completion of multiple related messages
- **Join Member**: An association between a join and a specific outbox message
- **Expected Steps**: The total number of messages that must complete for a join to finish
- **Completed Steps**: Count of messages that have been successfully processed
- **Failed Steps**: Count of messages that have permanently failed
- **Join Status**: Current state of the join (Pending, Completed, Failed, Cancelled)

### 4.5 Scheduling Concepts

- **Due Time**: An optional UTC timestamp indicating when a message should become eligible for processing
- **Deferred Message**: A message with a future due time that won't be claimed until that time arrives
- **Next Attempt Time**: For failed messages, the calculated time when the next retry should occur

## 5. Public API Surface

### 5.1 Core Interfaces

#### 5.1.1 IOutbox

The primary interface for enqueuing and managing outbox messages.

**Enqueue Operations:**

```csharp
// Standalone (creates own transaction)
Task EnqueueAsync(string topic, string payload, CancellationToken cancellationToken)
Task EnqueueAsync(string topic, string payload, string? correlationId, CancellationToken cancellationToken)
Task EnqueueAsync(string topic, string payload, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)

// Transactional (participates in existing transaction)
Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, CancellationToken cancellationToken)
Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, CancellationToken cancellationToken)
Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId, DateTimeOffset? dueTimeUtc, CancellationToken cancellationToken)
```

**Work Queue Operations:**

```csharp
Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(OwnerToken ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken)
Task AckAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
Task AbandonAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
Task FailAsync(OwnerToken ownerToken, IEnumerable<OutboxWorkItemIdentifier> ids, CancellationToken cancellationToken)
Task ReapExpiredAsync(CancellationToken cancellationToken)
```

**Join Operations:**

```csharp
Task<JoinIdentifier> StartJoinAsync(long tenantId, int expectedSteps, string? metadata, CancellationToken cancellationToken)
Task AttachMessageToJoinAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
Task ReportStepCompletedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
Task ReportStepFailedAsync(JoinIdentifier joinId, OutboxMessageIdentifier outboxMessageId, CancellationToken cancellationToken)
```

#### 5.1.2 IOutboxHandler

Interface for implementing message handlers.

```csharp
string Topic { get; }  // The topic this handler processes
Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
```

#### 5.1.3 IOutboxRouter

Interface for routing write operations to specific databases in multi-tenant scenarios.

```csharp
IOutbox GetOutbox(string key)  // Get outbox for a string routing key
IOutbox GetOutbox(Guid key)    // Get outbox for a GUID routing key
```

#### 5.1.4 IOutboxStore

Low-level storage interface for message persistence and retrieval.

```csharp
Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
```

### 5.2 Data Types

#### 5.2.1 OutboxMessage

```csharp
public sealed record OutboxMessage
{
    public OutboxWorkItemIdentifier Id { get; }
    public string Payload { get; }
    public string Topic { get; }
    public DateTimeOffset CreatedAt { get; }
    public bool IsProcessed { get; }
    public DateTimeOffset? ProcessedAt { get; }
    public string? ProcessedBy { get; }
    public int RetryCount { get; }
    public string? LastError { get; }
    public OutboxMessageIdentifier MessageId { get; }
    public string? CorrelationId { get; }
    public DateTimeOffset? DueTimeUtc { get; }
}
```

#### 5.2.2 Configuration Types

```csharp
public class SqlOutboxOptions
{
    public string ConnectionString { get; set; }
    public string SchemaName { get; set; } = "dbo";
    public string TableName { get; set; } = "Outbox";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

### 5.3 Service Registration

```csharp
// Single database
IServiceCollection AddSqlOutbox(this IServiceCollection services, SqlOutboxOptions options)

// Multiple databases (static configuration)
IServiceCollection AddMultiSqlOutbox(this IServiceCollection services, IEnumerable<SqlOutboxOptions> options)

// Multiple databases (dynamic discovery)
IServiceCollection AddDynamicMultiSqlOutbox(this IServiceCollection services, TimeSpan? refreshInterval = null)

// Handler registration
IServiceCollection AddOutboxHandler<THandler>(this IServiceCollection services) where THandler : class, IOutboxHandler
```

## 6. Behavioral Requirements

### 6.1 Message Enqueuing

**OBX-001**: The Outbox MUST persist messages durably to a SQL Server table when `EnqueueAsync` is called.

**OBX-002**: When using the standalone `EnqueueAsync` (without transaction parameter), the Outbox MUST create its own connection and transaction, and commit that transaction atomically with the message insert.

**OBX-003**: When using the transactional `EnqueueAsync` (with transaction parameter), the Outbox MUST participate in the provided transaction and MUST NOT commit or rollback that transaction.

**OBX-004**: The Outbox MUST reject `EnqueueAsync` calls with null or empty `topic` by throwing an `ArgumentException`.

**OBX-005**: The Outbox MUST reject `EnqueueAsync` calls with null `payload` by throwing an `ArgumentException`.

**OBX-006**: If `dueTimeUtc` is provided and is in the future, the Outbox MUST NOT make the message available for claiming until that time has passed.

**OBX-007**: If `dueTimeUtc` is null or in the past, the Outbox MUST make the message immediately available for claiming.

**OBX-008**: The Outbox MUST assign each message a unique `OutboxWorkItemIdentifier` upon insertion.

**OBX-009**: The Outbox MUST record the `CreatedAt` timestamp using the database server's UTC time upon insertion.

**OBX-010**: The Outbox MUST initialize newly enqueued messages with `RetryCount` = 0 and `IsProcessed` = false.

### 6.2 Message Claiming

**OBX-011**: `ClaimAsync` MUST atomically select and lock up to `batchSize` ready messages using database-level locking mechanisms (e.g., `WITH (UPDLOCK, READPAST, ROWLOCK)`).

**OBX-012**: `ClaimAsync` MUST only claim messages where `DueTimeUtc` is null or less than or equal to the current UTC time.

**OBX-013**: `ClaimAsync` MUST only claim messages that are not currently leased by another worker (i.e., `LockedUntil` is null or in the past).

**OBX-014**: `ClaimAsync` MUST set `LockedUntil` to the current UTC time plus `leaseSeconds`.

**OBX-015**: `ClaimAsync` MUST set `OwnerToken` to the provided `ownerToken` value.

**OBX-016**: `ClaimAsync` MUST return a list of `OutboxWorkItemIdentifier` for all successfully claimed messages.

**OBX-017**: If no messages are ready, `ClaimAsync` MUST return an empty list without throwing an exception.

**OBX-018**: `ClaimAsync` MUST NOT claim messages that are marked as processed (`IsProcessed` = true).

**OBX-019**: `ClaimAsync` MUST NOT claim messages that are marked as permanently failed.

**OBX-020**: `ClaimAsync` MUST respect the `batchSize` limit and MUST NOT claim more messages than requested.

### 6.3 Message Acknowledgment

**OBX-021**: `AckAsync` MUST mark the specified messages as successfully processed by setting `IsProcessed` = true.

**OBX-022**: `AckAsync` MUST set `ProcessedAt` to the current UTC timestamp.

**OBX-023**: `AckAsync` SHOULD set `ProcessedBy` to identify the worker that processed the message.

**OBX-024**: `AckAsync` MUST only acknowledge messages whose `OwnerToken` matches the provided `ownerToken`.

**OBX-025**: `AckAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**OBX-026**: After `AckAsync` completes, the acknowledged messages MUST NOT be returned by subsequent `ClaimAsync` calls.

**OBX-027**: If a message is part of any joins, `AckAsync` MUST increment the `CompletedSteps` counter for each associated join.

**OBX-028**: The increment of join counters in `AckAsync` MUST occur atomically with marking the message as processed.

### 6.4 Message Abandonment

**OBX-029**: `AbandonAsync` MUST release the lease on the specified messages by setting `LockedUntil` to null and `OwnerToken` to null.

**OBX-030**: `AbandonAsync` MUST increment the `RetryCount` for each abandoned message.

**OBX-031**: `AbandonAsync` MUST calculate a new `NextAttemptAt` time using exponential backoff based on `RetryCount`.

**OBX-032**: `AbandonAsync` MUST only abandon messages whose `OwnerToken` matches the provided `ownerToken`.

**OBX-033**: `AbandonAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**OBX-034**: After `AbandonAsync` completes, the abandoned messages MUST become available for claiming again after the backoff period expires.

**OBX-035**: `AbandonAsync` SHOULD record the last error message if provided by the caller.

### 6.5 Message Failure

**OBX-036**: `FailAsync` MUST mark the specified messages as permanently failed and prevent them from being claimed again.

**OBX-037**: `FailAsync` MUST record the `lastError` message provided by the caller.

**OBX-038**: `FailAsync` MUST only fail messages whose `OwnerToken` matches the provided `ownerToken`.

**OBX-039**: `FailAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**OBX-040**: If a message is part of any joins, `FailAsync` MUST increment the `FailedSteps` counter for each associated join.

**OBX-041**: The increment of join counters in `FailAsync` MUST occur atomically with marking the message as failed.

**OBX-042**: After `FailAsync` completes, the failed messages MUST NOT be returned by subsequent `ClaimAsync` calls.

### 6.6 Lease Expiration and Reaping

**OBX-043**: `ReapExpiredAsync` MUST identify all messages where `LockedUntil` is not null and is less than the current UTC time.

**OBX-044**: `ReapExpiredAsync` MUST release the lease on expired messages by setting `LockedUntil` to null and `OwnerToken` to null.

**OBX-045**: `ReapExpiredAsync` MUST make reaped messages available for claiming by subsequent `ClaimAsync` calls.

**OBX-046**: `ReapExpiredAsync` MUST NOT modify messages that have been acknowledged or permanently failed.

**OBX-047**: The Outbox polling service SHOULD call `ReapExpiredAsync` periodically to recover from worker crashes.

### 6.7 Message Handlers

**OBX-048**: The Outbox dispatcher MUST route each claimed message to the handler whose `Topic` property matches the message's topic.

**OBX-049**: If no handler is registered for a message's topic, the Outbox dispatcher MUST log a warning and SHOULD abandon the message for retry.

**OBX-050**: If a handler throws an exception, the Outbox dispatcher MUST catch the exception and determine whether to abandon or fail the message based on a backoff policy.

**OBX-051**: Handlers MUST be invoked with the full `OutboxMessage` object and a `CancellationToken`.

**OBX-052**: Handlers SHOULD be idempotent, as messages may be delivered more than once due to retries or worker failures.

**OBX-053**: The Outbox dispatcher MUST NOT call handlers concurrently for the same message.

**OBX-054**: The Outbox dispatcher MAY call handlers concurrently for different messages.

### 6.8 Retry and Backoff

**OBX-055**: The Outbox MUST implement exponential backoff for retrying failed messages.

**OBX-056**: The default backoff policy SHOULD use the formula: `delay = min(2^retryCount seconds, 60 seconds)`.

**OBX-057**: The backoff policy MAY be customizable via configuration or dependency injection.

**OBX-058**: After the maximum retry count is reached, the Outbox SHOULD permanently fail the message by calling `FailAsync`.

**OBX-059**: The maximum retry count SHOULD be configurable, with a sensible default (e.g., 10 attempts).

### 6.9 Multi-Database Support

**OBX-060**: When configured with multiple databases via `AddMultiSqlOutbox`, the Outbox MUST maintain separate stores for each database.

**OBX-061**: The Outbox dispatcher MUST use an `IOutboxSelectionStrategy` to determine which store to poll on each iteration.

**OBX-062**: The provided `RoundRobinOutboxSelectionStrategy` MUST cycle through all stores in order, processing one batch from each before moving to the next.

**OBX-063**: The provided `DrainFirstOutboxSelectionStrategy` MUST continue processing from the same store until it returns no messages, then move to the next store.

**OBX-064**: The `IOutboxStoreProvider` MUST return a consistent identifier for each store via `GetStoreIdentifier`.

**OBX-065**: The Outbox dispatcher MUST log the store identifier when processing messages to aid in troubleshooting.

**OBX-066**: The `IOutboxRouter.GetOutbox(key)` MUST return the `IOutbox` instance associated with the specified routing key.

**OBX-067**: The `IOutboxRouter` MUST throw an `InvalidOperationException` if no outbox exists for the specified routing key.

**OBX-068**: The `IOutboxRouter` MUST accept both string and GUID routing keys.

### 6.10 Dynamic Database Discovery

**OBX-069**: When configured with `AddDynamicMultiSqlOutbox`, the Outbox MUST periodically invoke `IOutboxDatabaseDiscovery.DiscoverDatabasesAsync` to refresh the list of databases.

**OBX-070**: The default refresh interval SHOULD be 5 minutes.

**OBX-071**: When new databases are discovered, the dynamic provider MUST create new outbox stores for those databases.

**OBX-072**: When databases are removed from discovery results, the dynamic provider MUST remove the corresponding outbox stores.

**OBX-073**: The dynamic provider MUST NOT unnecessarily recreate stores if the database configuration has not changed.

### 6.11 Join/Fan-In Coordination

**OBX-074**: `StartJoinAsync` MUST create a new join record with the specified `tenantId`, `expectedSteps`, and optional `metadata`.

**OBX-075**: `StartJoinAsync` MUST return a unique `JoinIdentifier` for the created join.

**OBX-076**: `StartJoinAsync` MUST initialize the join with `CompletedSteps` = 0 and `FailedSteps` = 0.

**OBX-077**: `AttachMessageToJoinAsync` MUST create a join member record associating the specified message with the specified join.

**OBX-078**: `AttachMessageToJoinAsync` MUST be idempotent; calling it multiple times with the same parameters MUST have no additional effect.

**OBX-079**: `ReportStepCompletedAsync` MUST increment the `CompletedSteps` counter for the specified join.

**OBX-080**: `ReportStepCompletedAsync` MUST be idempotent when called with the same `outboxMessageId`.

**OBX-081**: `ReportStepFailedAsync` MUST increment the `FailedSteps` counter for the specified join.

**OBX-082**: `ReportStepFailedAsync` MUST be idempotent when called with the same `outboxMessageId`.

**OBX-083**: Join counters (`CompletedSteps` and `FailedSteps`) SHOULD be automatically updated by the database when messages are acknowledged or failed, eliminating the need for explicit calls to `ReportStepCompletedAsync` or `ReportStepFailedAsync` in most cases.

**OBX-084**: The `JoinWaitHandler` MUST check if a join is complete by verifying that `CompletedSteps + FailedSteps = ExpectedSteps`.

**OBX-085**: If the join is not complete, the `JoinWaitHandler` MUST abandon the `join.wait` message for retry later.

**OBX-086**: If the join is complete and `FailIfAnyStepFailed` is true, the join MUST be marked as failed if `FailedSteps > 0`.

**OBX-087**: If the join is complete and succeeds, the `JoinWaitHandler` MUST enqueue the message specified by `OnCompleteTopic` and `OnCompletePayload`.

**OBX-088**: If the join is complete and fails, the `JoinWaitHandler` MUST enqueue the message specified by `OnFailTopic` and `OnFailPayload`.

**OBX-089**: Joins MUST be scoped to a single tenant (identified by `tenantId`).

**OBX-090**: A single message MAY participate in multiple joins.

### 6.12 Concurrency and Consistency

**OBX-091**: All database operations within a single Outbox method call MUST execute within a single transaction to ensure atomicity.

**OBX-092**: The Outbox MUST use appropriate database isolation levels to prevent dirty reads, non-repeatable reads, and phantom reads during claim operations.

**OBX-093**: The Outbox MUST handle database deadlocks gracefully by retrying the operation or propagating the exception to the caller.

**OBX-094**: Multiple worker processes MAY safely operate on the same Outbox table concurrently.

**OBX-095**: The Outbox MUST ensure that a message is never claimed by more than one worker at the same time.

### 6.13 Observability

**OBX-096**: The Outbox SHOULD log all enqueue operations at INFO level, including topic and correlation ID.

**OBX-097**: The Outbox SHOULD log all claim operations at DEBUG level, including the number of messages claimed.

**OBX-098**: The Outbox SHOULD log all handler invocations at INFO level, including topic and message ID.

**OBX-099**: The Outbox MUST log handler exceptions at ERROR level, including the exception details and message ID.

**OBX-100**: The Outbox SHOULD log reap operations at INFO level, including the number of messages reaped.

**OBX-101**: For multi-database scenarios, all log messages SHOULD include the store identifier to aid in troubleshooting.

### 6.14 Schema Deployment

**OBX-102**: When `EnableSchemaDeployment` is true, the Outbox MUST create the necessary database tables, indexes, and stored procedures if they do not already exist.

**OBX-103**: When `EnableSchemaDeployment` is false, the Outbox MUST assume the schema exists and MUST NOT attempt to create it.

**OBX-104**: Schema deployment operations SHOULD be idempotent; running them multiple times MUST NOT cause errors.

**OBX-105**: The Outbox schema MUST include the following tables: `Outbox`, `OutboxJoin`, `OutboxJoinMember`.

**OBX-106**: The Outbox schema MUST include stored procedures for claim, ack, abandon, fail, and reap operations.

## 7. Configuration and Limits

### 7.1 Configuration Options

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ConnectionString` | string | (required) | SQL Server connection string |
| `SchemaName` | string | "dbo" | Database schema name |
| `TableName` | string | "Outbox" | Outbox table name |
| `EnableSchemaDeployment` | bool | false | Automatically create schema objects |
| `PollingIntervalSeconds` | double | 0.5 | Interval between polling iterations |
| `BatchSize` | int | 50 | Maximum messages to claim per iteration |
| `LeaseSeconds` | int | 30 | Duration to hold message leases |

### 7.2 Limits and Constraints

**OBX-107**: The `topic` parameter MUST NOT exceed 255 characters.

**OBX-108**: The `payload` parameter MAY be arbitrarily large, subject to database column limits (NVARCHAR(MAX)).

**OBX-109**: The `correlationId` parameter MUST NOT exceed 255 characters.

**OBX-110**: The `leaseSeconds` parameter SHOULD be between 10 and 300 seconds for optimal performance.

**OBX-111**: The `batchSize` parameter SHOULD be between 1 and 100 for optimal performance.

**OBX-112**: A single Outbox instance MAY process thousands of messages per second, depending on handler complexity and database performance.

### 7.3 Performance Considerations

**OBX-113**: The Outbox SHOULD use database indexes on the `Status` and `CreatedAt` columns to optimize claim queries.

**OBX-114**: The Outbox SHOULD use stored procedures for claim, ack, abandon, and fail operations to minimize round trips.

**OBX-115**: For multi-database scenarios, the Outbox SHOULD cache `IOutbox` instances to avoid recreating them on every operation.

**OBX-116**: The polling service SHOULD implement a backoff mechanism when no messages are available to reduce database load.

### 7.4 Security Considerations

**OBX-117**: The database user configured in `ConnectionString` MUST have SELECT, INSERT, UPDATE, and DELETE permissions on the Outbox tables.

**OBX-118**: The database user MUST have EXECUTE permissions on all Outbox stored procedures.

**OBX-119**: The Outbox MUST NOT log sensitive information from message payloads.

**OBX-120**: The Outbox SHOULD support encrypted connections to the database via the connection string.

## 8. Open Questions / Inconsistencies

### 8.1 Join Store Singleton Limitation

**Observation**: The current `SqlOutboxJoinStore` implementation is registered as a singleton and connects to a single database. In multi-database scenarios, joins only work within the configured database. This is inconsistent with the multi-database support for the main Outbox.

**Impact**: Users cannot create joins that span multiple tenant databases. Each tenant's joins are isolated.

**Recommendation**: Consider implementing an `IOutboxJoinStoreProvider` pattern similar to `IOutboxStoreProvider` to support joins across multiple databases in future versions.

### 8.2 Automatic vs. Manual Join Reporting

**Observation**: The documentation states that join completion is reported automatically by database stored procedures (`Outbox_Ack` and `Outbox_Fail`), but the `IOutbox` interface still exposes `ReportStepCompletedAsync` and `ReportStepFailedAsync` methods.

**Impact**: This dual mechanism may confuse users about when to use manual vs. automatic reporting.

**Recommendation**: Clarify in documentation that manual methods are provided for backward compatibility and edge cases only. Most users should rely on automatic reporting.

### 8.3 Message Ordering

**Observation**: The specification explicitly states that message ordering is not guaranteed, but some users may expect FIFO ordering within a topic.

**Impact**: Users who require ordering must implement their own sequencing logic in handlers.

**Recommendation**: Consider adding an optional "sequence number" or "partition key" feature in a future version to support ordered processing within partitions.

### 8.4 Dead Letter Queue

**Observation**: The current implementation permanently fails messages after max retries but does not provide a dedicated dead letter queue or storage mechanism.

**Impact**: Failed messages remain in the Outbox table with a failed status. Users must query the table directly to find and investigate failed messages.

**Recommendation**: Consider adding a configurable dead letter queue mechanism or separate table for permanently failed messages in a future version.

### 8.5 Cross-Database Transactions

**Observation**: The Outbox does not support distributed transactions across multiple databases.

**Impact**: Users cannot atomically enqueue messages to multiple tenant databases in a single operation.

**Recommendation**: This is a known limitation and consistent with the single-database transaction model. Document that users should use saga patterns or compensating transactions for cross-database coordination.

### 8.6 Message TTL (Time-to-Live)

**Observation**: The current implementation does not provide a message expiration or TTL mechanism.

**Impact**: Messages that become irrelevant (e.g., due to time-sensitive business logic) may still be processed.

**Recommendation**: Consider adding an optional `ExpiresAt` field in a future version to automatically fail messages past their expiration time.

---

## Appendix A: Database Schema Reference

### A.1 Outbox Table

```sql
CREATE TABLE [dbo].[Outbox] (
    Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Work queue state
    Status TINYINT NOT NULL DEFAULT(0),        -- 0=Ready, 1=InProgress, 2=Done, 3=Failed
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL,
    
    -- Processing metadata
    IsProcessed BIT NOT NULL DEFAULT 0,
    ProcessedAt DATETIMEOFFSET NULL,
    ProcessedBy NVARCHAR(100) NULL,
    
    -- Retry logic
    RetryCount INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    
    -- Message tracking
    MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    CorrelationId NVARCHAR(255) NULL,
    DueTimeUtc DATETIMEOFFSET NULL
);

CREATE INDEX IX_Outbox_WorkQueue ON [dbo].[Outbox](Status, CreatedAt) INCLUDE(Id, OwnerToken);
CREATE INDEX IX_Outbox_DueTime ON [dbo].[Outbox](DueTimeUtc) WHERE DueTimeUtc IS NOT NULL;
```

### A.2 OutboxJoin Table

```sql
CREATE TABLE [dbo].[OutboxJoin] (
    JoinId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    PayeWaiveTenantId BIGINT NOT NULL,
    ExpectedSteps INT NOT NULL,
    CompletedSteps INT NOT NULL DEFAULT 0,
    FailedSteps INT NOT NULL DEFAULT 0,
    Status TINYINT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Completed, 2=Failed, 3=Cancelled
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastUpdatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Metadata NVARCHAR(MAX) NULL
);
```

### A.3 OutboxJoinMember Table

```sql
CREATE TABLE [dbo].[OutboxJoinMember] (
    JoinId UNIQUEIDENTIFIER NOT NULL,
    OutboxMessageId UNIQUEIDENTIFIER NOT NULL,
    Status TINYINT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Completed, 2=Failed
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_OutboxJoinMember PRIMARY KEY (JoinId, OutboxMessageId),
    CONSTRAINT FK_OutboxJoinMember_Join FOREIGN KEY (JoinId) 
        REFERENCES [dbo].[OutboxJoin](JoinId) ON DELETE CASCADE
);
```

## Appendix B: Handler Implementation Example

```csharp
public class EmailOutboxHandler : IOutboxHandler
{
    private readonly IEmailService _emailService;
    private readonly ILogger<EmailOutboxHandler> _logger;

    public EmailOutboxHandler(IEmailService emailService, ILogger<EmailOutboxHandler> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public string Topic => "email.send";

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var emailData = JsonSerializer.Deserialize<EmailData>(message.Payload);
        
        // Idempotency check (recommended)
        if (await _emailService.HasBeenSentAsync(message.MessageId))
        {
            _logger.LogInformation("Email {MessageId} already sent, skipping", message.MessageId);
            return;
        }
        
        _logger.LogInformation("Sending email to {Recipient}", emailData.To);
        
        await _emailService.SendAsync(emailData, cancellationToken);
        
        // Record that we sent it (for idempotency)
        await _emailService.RecordSentAsync(message.MessageId, cancellationToken);
    }
}
```

## Appendix C: Multi-Tenant Usage Example

```csharp
// Service configuration
public void ConfigureServices(IServiceCollection services)
{
    // Register dynamic discovery
    services.AddSingleton<IOutboxDatabaseDiscovery, TenantDatabaseDiscovery>();
    
    // Register multi-outbox with round-robin strategy
    services.AddDynamicMultiSqlOutbox(refreshInterval: TimeSpan.FromMinutes(5));
    
    // Register handlers
    services.AddOutboxHandler<OrderCreatedHandler>();
}

// Application code
public class OrderService
{
    private readonly IOutboxRouter _outboxRouter;
    
    public OrderService(IOutboxRouter outboxRouter)
    {
        _outboxRouter = outboxRouter;
    }
    
    public async Task CreateOrderAsync(string tenantId, Order order)
    {
        // Get the outbox for this specific tenant
        var outbox = _outboxRouter.GetOutbox(tenantId);
        
        // Enqueue message to the tenant's database
        await outbox.EnqueueAsync(
            "order.created",
            JsonSerializer.Serialize(order),
            order.Id.ToString(),
            cancellationToken: CancellationToken.None);
    }
}
```

## Appendix D: Join/Fan-In Example

```csharp
// Start a join for 3 parallel extraction tasks
var joinId = await outbox.StartJoinAsync(
    tenantId: customerId,
    expectedSteps: 3,
    metadata: """{"type": "etl", "phase": "extract"}""",
    cancellationToken);

// Enqueue extraction messages and attach to join
var msg1 = await outbox.EnqueueAsync("extract.customers", payload1, cancellationToken);
await outbox.AttachMessageToJoinAsync(joinId, msg1, cancellationToken);

var msg2 = await outbox.EnqueueAsync("extract.orders", payload2, cancellationToken);
await outbox.AttachMessageToJoinAsync(joinId, msg2, cancellationToken);

var msg3 = await outbox.EnqueueAsync("extract.products", payload3, cancellationToken);
await outbox.AttachMessageToJoinAsync(joinId, msg3, cancellationToken);

// Set up fan-in to start transformation when all extractions complete
await outbox.EnqueueJoinWaitAsync(
    joinId: joinId,
    failIfAnyStepFailed: true,
    onCompleteTopic: "etl.transform",
    onCompletePayload: JsonSerializer.Serialize(new { CustomerId = customerId }),
    cancellationToken: cancellationToken);

// Handlers don't need any join-specific logic - automatic reporting handles it
public class ExtractCustomersHandler : IOutboxHandler
{
    public string Topic => "extract.customers";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Just do the work - join completion is automatic!
        await ExtractCustomersAsync(cancellationToken);
    }
}
```

---

**End of Specification**
