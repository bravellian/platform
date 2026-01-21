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

### 2.3 Scope

**In Scope:**
- Transactional message enqueueing (standalone and within existing transactions)
- Work queue message processing with leases
- Handler-based message routing and processing
- Automatic retry with configurable backoff policies
- Multi-database/multi-tenant message processing
- Message scheduling with due times
- SQL Server backend implementation

**Out of Scope:**
- Direct message broker integration (handled by user-provided handlers)
- Message transformation or enrichment (handled by user-provided handlers)
- Exactly-once delivery semantics (provides at-least-once; handlers must be idempotent)
- Message ordering guarantees within a topic
- Non-SQL Server backends (future consideration)
- Message priority queuing (future consideration)

**Related Components (documented separately):**
- Join / fan-in coordination built on top of Outbox using `OutboxMessageIdentifier` and Outbox stored procedures. The Outbox component itself remains join-agnostic.

## 3. Non-Goals

This component does NOT:

1. **Replace Message Brokers**: The Outbox is not a general-purpose message broker. It coordinates local database writes with eventual message delivery.
2. **Guarantee Exactly-Once Processing**: The component provides at-least-once delivery semantics. Handlers must be idempotent.
3. **Provide Ordered Message Delivery**: Messages may be processed in any order, regardless of creation time or topic.
4. **Support Cross-Database Transactions**: Each message is enqueued in a single database; distributed transactions across databases are not supported.
5. **Implement Business Logic**: The Outbox dispatches messages; all business logic lives in handler implementations.

## 4. Key Concepts and Terms

### 4.1 Core Entities

- **Message**: A unit of work to be processed asynchronously, consisting of a topic, payload, and metadata
- **Topic**: A string identifier used to route messages to appropriate handlers (e.g., "order.created", "email.send")
- **Payload**: The message content, typically serialized as JSON
- **Correlation ID**: An optional identifier to trace messages back to their source or group related messages
- **Handler**: An implementation of `IOutboxHandler` that processes messages for a specific topic

### 4.2 Strongly-Typed Identifiers

The Outbox component uses strongly-typed identifiers (implemented as `readonly record struct` wrappers around `Guid`) to prevent mixing up different types of IDs and provide type safety:

- **OutboxWorkItemIdentifier**: The primary key of an outbox message in the work queue table. This is the identifier used by workers to claim, acknowledge, abandon, or fail messages. It represents a specific instance of a message in the queue with its processing state.

- **OutboxMessageIdentifier**: The logical message identifier that remains constant across retries. While `OutboxWorkItemIdentifier` represents the work item in the queue, `OutboxMessageIdentifier` represents the semantic message itself. This is useful for idempotency checks and correlation across multiple processing attempts.

- **OwnerToken**: A unique identifier for a worker process or instance. When a worker claims messages, it provides its `OwnerToken` to establish ownership. Only the owning worker can acknowledge, abandon, or fail messages it has claimed. This prevents conflicts when multiple workers are processing messages concurrently.

### 4.3 Work Queue Semantics

- **Claim**: Atomically reserve messages for processing with a time-bounded lease
- **Lease**: A time-limited lock on a message, preventing other workers from processing it
- **Acknowledge (Ack)**: Mark a message as successfully processed and remove it from the queue
- **Abandon**: Release a message's lease and return it to ready state for retry (used for transient failures)
- **Fail**: Permanently mark a message as failed (used for permanent errors after max retries)
- **Reap**: Recover messages whose leases have expired due to worker crashes or timeouts

### 4.4 Multi-Database Concepts

- **Outbox Store**: A single database instance containing an Outbox table
- **Store Provider**: Manages access to multiple outbox stores (one per database/tenant)
- **Selection Strategy**: Algorithm for choosing which store to poll next (e.g., round-robin, drain-first)
- **Router**: Routes write operations to the correct outbox store based on a routing key (e.g., tenant ID)

### 4.5 Scheduling Concepts

- **Due Time**: An optional UTC timestamp indicating when a message should become eligible for processing
- **Deferred Message**: A message with a future due time that won't be claimed until that time arrives
- **Next Attempt Time**: The earliest UTC time when a message (including previously failed or abandoned messages) becomes eligible to be claimed again

## 5. Public API Surface

### 5.1 Core Interfaces

#### 5.1.1 IOutbox

The primary interface for enqueuing and managing outbox messages.

**Enqueue Operations:**

```csharp
Task EnqueueAsync(
    string topic,
    string payload,
    IDbTransaction? transaction,
    string? correlationId,
    DateTimeOffset? dueTimeUtc,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`topic`** (required, non-null): The message topic used for routing to handlers.
  - **Type**: `string`
  - **Constraints**: 
    - MUST NOT be null or empty string
    - MUST NOT exceed 255 characters
    - Case-sensitive (e.g., "Order.Created" ≠ "order.created")
    - No specific character restrictions, but recommend using alphanumeric characters, dots, hyphens, and underscores for clarity
  - **Purpose**: Routes the message to the appropriate `IOutboxHandler` implementation

- **`payload`** (required, non-null): The message content.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null
    - MAY be empty string (valid for messages with no body)
    - Maximum size limited by database NVARCHAR(MAX) (approximately 2GB)
  - **Format**: Typically JSON, but the Outbox does not enforce or validate format. Handlers are responsible for deserialization.
  - **Purpose**: Contains the message data to be processed by handlers

- **`transaction`** (optional): Database transaction to participate in.
  - **Type**: `IDbTransaction?`
  - **Constraints**:
    - If null: Method creates its own connection and transaction
    - If non-null: Method participates in the provided transaction and does NOT commit/rollback it
  - **Purpose**: Enables atomic enqueueing with other database operations

- **`correlationId`** (optional): Identifier for tracing and correlation.
  - **Type**: `string?`
  - **Constraints**:
    - MAY be null
    - Empty string ("") is treated as null and normalized to null
    - MUST NOT exceed 255 characters when non-null
  - **Purpose**: Links related messages or traces messages back to originating requests

- **`dueTimeUtc`** (optional): Scheduled processing time.
  - **Type**: `DateTimeOffset?`
  - **Constraints**:
    - MAY be null (message is immediately eligible for processing)
    - If non-null, MUST be in UTC (enforcement is caller's responsibility)
    - Past dates are treated as immediate eligibility
  - **Purpose**: Defers message processing until the specified time

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`
  - **Purpose**: Allows cancellation of the async operation

_Implementation note_: The runtime MAY expose convenience overloads of `EnqueueAsync` that omit `transaction`, `correlationId`, and/or `dueTimeUtc`. For the purposes of this specification, all overloads are defined in terms of this canonical signature. Omitted arguments are treated as if `null` (or `CancellationToken.None`) was passed.

**Work Queue Operations:**

```csharp
Task<IReadOnlyList<OutboxWorkItemIdentifier>> ClaimAsync(
    OwnerToken ownerToken,
    int leaseSeconds,
    int batchSize,
    CancellationToken cancellationToken)
```

**Parameters:**

- `ownerToken` (required): A stable identifier for the worker instance.
  - Type: `OwnerToken` (strongly-typed GUID).
  - Constraints:
    - MUST NOT be the default/empty value.
    - SHOULD remain constant for the lifetime of the worker process instance.
  - Purpose: Used to enforce that only the owning worker can ack/abandon/fail the messages it claimed.

- `leaseSeconds` (required): Duration in seconds for which the claim is valid.
  - Type: `int`
  - Constraints:
    - MUST be greater than 0.
    - SHOULD be between 10 and 300 seconds for typical workloads.
    - If `leaseSeconds <= 0`, the method MUST throw an `ArgumentOutOfRangeException`.
  - Purpose: Controls how long messages are locked before they can be reaped or reclaimed.

- `batchSize` (required): Maximum number of messages to claim.
  - Type: `int`
  - Constraints:
    - MUST be greater than 0.
    - SHOULD be between 1 and 100 for typical workloads.
    - If `batchSize <= 0`, the method MUST throw an `ArgumentOutOfRangeException`.
  - Purpose: Batches work for efficiency while bounding per-iteration load.

- `cancellationToken` (required): Cancellation token for the operation.
  - Type: `CancellationToken`
  - Purpose: Allows cancelling the claim operation.

```csharp
Task AckAsync(
    OwnerToken ownerToken,
    IEnumerable<OutboxWorkItemIdentifier> ids,
    CancellationToken cancellationToken)
```

**Parameters:**

- `ids` (required): The set of work item identifiers to acknowledge.
  - Type: `IEnumerable<OutboxWorkItemIdentifier>`
  - Constraints:
    - MUST NOT be null.
    - MAY be empty; an empty sequence is treated as a no-op.
    - Duplicate IDs within the sequence MUST be tolerated and MUST NOT cause errors.
  - Purpose: Indicates which claimed messages to transition to the next state.

- `ownerToken` (required): The worker identity that previously claimed the messages.
  - Constraints:
    - MUST match the `OwnerToken` currently stored on each message to be updated.
  - Behavior:
    - Messages whose `OwnerToken` does not match are silently ignored and MUST NOT cause the method to fail.

- `cancellationToken` (required): Cancellation token for the operation.

```csharp
Task AbandonAsync(
    OwnerToken ownerToken,
    IEnumerable<OutboxWorkItemIdentifier> ids,
    CancellationToken cancellationToken)
```

**Parameters:**

- `ids` (required): The set of work item identifiers to abandon.
  - Type: `IEnumerable<OutboxWorkItemIdentifier>`
  - Constraints:
    - MUST NOT be null.
    - MAY be empty; an empty sequence is treated as a no-op.
    - Duplicate IDs within the sequence MUST be tolerated and MUST NOT cause errors.
  - Purpose: Indicates which claimed messages to transition to the next state.

- `ownerToken` (required): The worker identity that previously claimed the messages.
  - Constraints:
    - MUST match the `OwnerToken` currently stored on each message to be updated.
  - Behavior:
    - Messages whose `OwnerToken` does not match are silently ignored and MUST NOT cause the method to fail.

- `cancellationToken` (required): Cancellation token for the operation.

```csharp
Task FailAsync(
    OwnerToken ownerToken,
    IEnumerable<OutboxWorkItemIdentifier> ids,
    CancellationToken cancellationToken)
```

**Parameters:**

- `ids` (required): The set of work item identifiers to fail.
  - Type: `IEnumerable<OutboxWorkItemIdentifier>`
  - Constraints:
    - MUST NOT be null.
    - MAY be empty; an empty sequence is treated as a no-op.
    - Duplicate IDs within the sequence MUST be tolerated and MUST NOT cause errors.
  - Purpose: Indicates which claimed messages to transition to the next state.

- `ownerToken` (required): The worker identity that previously claimed the messages.
  - Constraints:
    - MUST match the `OwnerToken` currently stored on each message to be updated.
  - Behavior:
    - Messages whose `OwnerToken` does not match are silently ignored and MUST NOT cause the method to fail.

- `cancellationToken` (required): Cancellation token for the operation.

```csharp
Task ReapExpiredAsync(CancellationToken cancellationToken)
```

**Parameters:**

- `cancellationToken` (required): Cancellation token for the operation.
  - Purpose: Allows cancellation of the reap operation. If cancellation is requested, the method MUST stop processing as soon as practical.

#### 5.1.2 IOutboxHandler

Interface for implementing message handlers.

```csharp
string Topic { get; }  // The topic this handler processes
Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
```

**Topic Property Constraints:**

- `Topic` MUST be non-null and non-empty.
- The handler's `Topic` MUST match the `topic` value used in `EnqueueAsync` calls in a case-sensitive manner.
- The same length and character recommendations as `topic` (see §5.1.1) apply.

**HandleAsync Parameters:**

- `message`: The claimed outbox message to process. MUST NOT be null.
- `cancellationToken`: Standard cancellation token. Handlers SHOULD honor cancellation where practical.

#### 5.1.3 IOutboxRouter

Interface for routing write operations to specific databases in multi-tenant scenarios.

```csharp
IOutbox GetOutbox(string routingKey)
```

**Parameters:**

- `routingKey` (required): Opaque routing identifier used to choose a tenant/database.
  - Type: `string`
  - Constraints:
    - MUST NOT be null or empty.
    - SHOULD be stable and unique per outbox store (e.g., tenant ID, database name).
  - Behavior:
    - If no outbox exists for the specified `routingKey`, the method MUST throw `InvalidOperationException`.

_Implementation note_: Some implementations MAY provide additional overloads (e.g., `GetOutbox(Guid tenantId)`) that internally convert other key types to strings before delegating to this canonical method. Such overloads are convenience APIs and do not change the specified behavior.

#### 5.1.4 IOutboxStore

Low-level storage interface for message persistence and retrieval.

```csharp
Task<IReadOnlyList<OutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
Task MarkDispatchedAsync(OutboxWorkItemIdentifier id, CancellationToken cancellationToken)
Task RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, CancellationToken cancellationToken)
Task FailAsync(OutboxWorkItemIdentifier id, string lastError, CancellationToken cancellationToken)
```

**Parameter Semantics:**

- `ClaimDueAsync(int limit, ...)`
  - `limit` MUST be greater than 0. If `limit <= 0`, the store MUST throw an `ArgumentOutOfRangeException`.
  - The store MUST NOT return more than `limit` messages.

- `RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, ...)`
  - `delay` MUST be greater than zero; zero or negative values MUST result in an `ArgumentOutOfRangeException`.
  - `lastError` MAY be null or empty. Implementations SHOULD normalize empty strings to null to avoid noise.

- `FailAsync(OutboxWorkItemIdentifier id, string lastError, ...)`
  - `lastError` MAY be null or empty; same normalization rule as above.

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
    public string SchemaName { get; set; } = "infra";
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

**OBX-006**: The Outbox MUST accept empty string ("") as a valid `payload` value.

**OBX-007**: If `topic` exceeds 255 characters, the Outbox MUST throw an `ArgumentException` or allow the database to reject it with a SQL exception.

**OBX-008**: The Outbox MUST treat an empty string `correlationId` as equivalent to null and normalize it to null before storage.

**OBX-009**: If `correlationId` is non-null and non-empty, it MUST NOT exceed 255 characters.

**OBX-010**: The `topic` parameter is case-sensitive. "Order.Created" and "order.created" are treated as different topics.

**OBX-011**: The Outbox does NOT validate or enforce any format for `payload`. Handlers are responsible for deserializing and validating payload content.

**OBX-012**: If `dueTimeUtc` is provided and is in the future, the Outbox MUST NOT make the message available for claiming until that time has passed.

**OBX-013**: If `dueTimeUtc` is null or in the past, the Outbox MUST make the message immediately available for claiming.

**OBX-014**: The Outbox MUST assign each message a unique `OutboxWorkItemIdentifier` upon insertion.

**OBX-015**: The Outbox MUST record the `CreatedAt` timestamp using the database server's UTC time upon insertion.

**OBX-016**: The Outbox MUST initialize newly enqueued messages with `RetryCount` = 0 and `IsProcessed` = false.

### 6.2 Message Claiming

**OBX-017**: `ClaimAsync` MUST atomically select and lock up to `batchSize` ready messages using database-level locking mechanisms (e.g., `WITH (UPDLOCK, READPAST, ROWLOCK)`).

**OBX-018**: `ClaimAsync` MUST only claim messages where `DueTimeUtc` is null or less than or equal to the current UTC time (if `DueTimeUtc` is used).

**OBX-019**: `ClaimAsync` MUST only claim messages that are not currently leased by another worker (i.e., `LockedUntil` is null or in the past).

**OBX-020**: `ClaimAsync` MUST set `LockedUntil` to the current UTC time plus `leaseSeconds`.

**OBX-021**: `ClaimAsync` MUST set `OwnerToken` to the provided `ownerToken` value.

**OBX-022**: `ClaimAsync` MUST return a list of `OutboxWorkItemIdentifier` for all successfully claimed messages.

**OBX-023**: If no messages are ready, `ClaimAsync` MUST return an empty list without throwing an exception.

**OBX-024**: `ClaimAsync` MUST NOT claim messages that are marked as processed (`IsProcessed` = true).

**OBX-025**: `ClaimAsync` MUST NOT claim messages that are marked as permanently failed.

**OBX-026**: `ClaimAsync` MUST respect the `batchSize` limit and MUST NOT claim more messages than requested.

**OBX-027**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `leaseSeconds <= 0`.

**OBX-028**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `batchSize <= 0`.

**OBX-029**: `ClaimAsync` MUST only claim messages where `NextAttemptAt` is less than or equal to the current UTC time.

### 6.3 Message Acknowledgment

**OBX-030**: `AckAsync` MUST mark the specified messages as successfully processed by setting `IsProcessed` = true.

**OBX-031**: `AckAsync` MUST set `ProcessedAt` to the current UTC timestamp.

**OBX-032**: `AckAsync` SHOULD set `ProcessedBy` to identify the worker that processed the message.

**OBX-033**: `AckAsync` MUST only acknowledge messages whose `OwnerToken` matches the provided `ownerToken`.

**OBX-034**: `AckAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**OBX-035**: After `AckAsync` completes, the acknowledged messages MUST NOT be returned by subsequent `ClaimAsync` calls.

**OBX-036**: `AckAsync`, `AbandonAsync`, and `FailAsync` MUST throw an `ArgumentNullException` if `ids` is null, and MUST treat an empty `ids` collection as a no-op.

### 6.4 Message Abandonment

**OBX-037**: `AbandonAsync` MUST release the lease on the specified messages by setting `LockedUntil` to null and `OwnerToken` to null.

**OBX-038**: `AbandonAsync` MUST increment the `RetryCount` for each abandoned message.

**OBX-039**: `AbandonAsync` MUST calculate a new `NextAttemptAt` time using exponential backoff based on `RetryCount`.

**OBX-040**: `AbandonAsync` MUST only abandon messages whose `OwnerToken` matches the provided `ownerToken`.

**OBX-041**: `AbandonAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**OBX-042**: After `AbandonAsync` completes, the abandoned messages MUST become available for claiming again after the backoff period expires.

**OBX-043**: `AbandonAsync` SHOULD record the last error message if provided by the caller.

### 6.5 Message Failure

**OBX-044**: `FailAsync` MUST mark the specified messages as permanently failed and prevent them from being claimed again.

**OBX-045**: `FailAsync` MUST record the `lastError` message provided by the caller.

**OBX-046**: `FailAsync` MUST only fail messages whose `OwnerToken` matches the provided `ownerToken`.

**OBX-047**: `FailAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**OBX-048**: After `FailAsync` completes, the failed messages MUST NOT be returned by subsequent `ClaimAsync` calls.

### 6.6 Lease Expiration and Reaping

**OBX-049**: `ReapExpiredAsync` MUST identify all messages where `LockedUntil` is not null and is less than the current UTC time.

**OBX-050**: `ReapExpiredAsync` MUST release the lease on expired messages by setting `LockedUntil` to null and `OwnerToken` to null.

**OBX-051**: `ReapExpiredAsync` MUST make reaped messages available for claiming by subsequent `ClaimAsync` calls.

**OBX-052**: `ReapExpiredAsync` MUST NOT modify messages that have been acknowledged or permanently failed.

**OBX-053**: The Outbox polling service SHOULD call `ReapExpiredAsync` periodically to recover from worker crashes.

### 6.7 Message Handlers

**OBX-054**: The Outbox dispatcher MUST route each claimed message to the handler whose `Topic` property matches the message's topic.

**OBX-055**: If no handler is registered for a message's topic, the Outbox dispatcher MUST log a warning and SHOULD abandon the message for retry.

**OBX-056**: If a handler throws an exception, the Outbox dispatcher MUST catch the exception and determine whether to abandon or fail the message based on a backoff policy.

**OBX-057**: Handlers MUST be invoked with the full `OutboxMessage` object and a `CancellationToken`.

**OBX-058**: Handlers SHOULD be idempotent, as messages may be delivered more than once due to retries or worker failures.

**OBX-059**: The Outbox dispatcher MUST NOT call handlers concurrently for the same message.

**OBX-060**: The Outbox dispatcher MAY call handlers concurrently for different messages.

### 6.8 Retry and Backoff

**OBX-061**: The Outbox MUST implement exponential backoff for retrying failed messages.

**OBX-062**: The default backoff policy SHOULD use the formula: `delay = min(2^retryCount seconds, 60 seconds)`.

**OBX-063**: The backoff policy MAY be customizable via configuration or dependency injection.

**OBX-064**: After the maximum retry count is reached, the Outbox SHOULD permanently fail the message by calling `FailAsync`.

**OBX-065**: The maximum retry count SHOULD be configurable, with a sensible default (e.g., 10 attempts).

### 6.9 Multi-Database Support

**OBX-066**: When configured with multiple databases via `AddMultiSqlOutbox`, the Outbox MUST maintain separate stores for each database.

**OBX-067**: The Outbox dispatcher MUST use an `IOutboxSelectionStrategy` to determine which store to poll on each iteration.

**OBX-068**: The provided `RoundRobinOutboxSelectionStrategy` MUST cycle through all stores in order, processing one batch from each before moving to the next.

**OBX-069**: The provided `DrainFirstOutboxSelectionStrategy` MUST continue processing from the same store until it returns no messages, then move to the next store.

**OBX-070**: The `IOutboxStoreProvider` MUST return a consistent identifier for each store via `GetStoreIdentifier`.

**OBX-071**: The Outbox dispatcher MUST log the store identifier when processing messages to aid in troubleshooting.

**OBX-072**: The `IOutboxRouter.GetOutbox(key)` MUST return the `IOutbox` instance associated with the specified routing key.

**OBX-073**: The `IOutboxRouter` MUST throw an `InvalidOperationException` if no outbox exists for the specified routing key.

**OBX-074**: The `IOutboxRouter` MUST accept both string and GUID routing keys.

### 6.10 Dynamic Database Discovery

**OBX-075**: When configured with `AddDynamicMultiSqlOutbox`, the Outbox MUST periodically invoke `IOutboxDatabaseDiscovery.DiscoverDatabasesAsync` to refresh the list of databases.

**OBX-076**: The default refresh interval SHOULD be 5 minutes.

**OBX-077**: When new databases are discovered, the dynamic provider MUST create new outbox stores for those databases.

**OBX-078**: When databases are removed from discovery results, the dynamic provider MUST remove the corresponding outbox stores.

**OBX-079**: The dynamic provider MUST NOT unnecessarily recreate stores if the database configuration has not changed.

### 6.12 Concurrency and Consistency

**OBX-080**: All database operations within a single Outbox method call MUST execute within a single transaction to ensure atomicity.

**OBX-081**: The Outbox MUST use appropriate database isolation levels to prevent dirty reads, non-repeatable reads, and phantom reads during claim operations.

**OBX-082**: The Outbox MUST handle database deadlocks gracefully by retrying the operation or propagating the exception to the caller.

**OBX-083**: Multiple worker processes MAY safely operate on the same Outbox table concurrently.

**OBX-084**: The Outbox MUST ensure that a message is never claimed by more than one worker at the same time.

### 6.13 Observability

**OBX-085**: The Outbox SHOULD log all enqueue operations at INFO level, including topic and correlation ID.

**OBX-086**: The Outbox SHOULD log all claim operations at DEBUG level, including the number of messages claimed.

**OBX-087**: The Outbox SHOULD log all handler invocations at INFO level, including topic and message ID.

**OBX-088**: The Outbox MUST log handler exceptions at ERROR level, including the exception details and message ID.

**OBX-089**: The Outbox SHOULD log reap operations at INFO level, including the number of messages reaped.

**OBX-090**: For multi-database scenarios, all log messages SHOULD include the store identifier to aid in troubleshooting.

### 6.14 Schema Deployment

**OBX-091**: When `EnableSchemaDeployment` is true, the Outbox MUST create the necessary database tables, indexes, and stored procedures if they do not already exist.

**OBX-092**: When `EnableSchemaDeployment` is false, the Outbox MUST assume the schema exists and MUST NOT attempt to create it.

**OBX-093**: Schema deployment operations SHOULD be idempotent; running them multiple times MUST NOT cause errors.

**OBX-094**: The Outbox schema MUST include the `Outbox` table and associated indexes needed for claiming and processing messages.

> Join-related tables (e.g., `OutboxJoin`, `OutboxJoinMember`) are owned by the Join component and are specified in the Join Coordination specification.

**OBX-095**: The Outbox schema MUST include stored procedures for claim, ack, abandon, fail, and reap operations.

## 7. Configuration and Limits

### 7.1 Configuration Options

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ConnectionString` | string | (required) | SQL Server connection string |
| `SchemaName` | string | "infra" | Database schema name |
| `TableName` | string | "Outbox" | Outbox table name |
| `EnableSchemaDeployment` | bool | false | Automatically create schema objects |
| `PollingIntervalSeconds` | double | 0.5 | Interval between polling iterations |
| `BatchSize` | int | 50 | Maximum messages to claim per iteration |
| `LeaseSeconds` | int | 30 | Duration to hold message leases |

### 7.2 Limits and Constraints

The following constraints are enforced by the Outbox component (also documented in section 6.1 behavioral requirements):

- **Topic**: Maximum 255 characters (OBX-007)
- **Payload**: Maximum ~2GB (NVARCHAR(MAX) limit) (OBX-114)
- **CorrelationId**: Maximum 255 characters when non-null (OBX-009)
- **LeaseSeconds**: Recommended 10-300 seconds (OBX-116)
- **BatchSize**: Recommended 1-100 (OBX-117)

**OBX-096**: The `payload` parameter MAY be arbitrarily large, subject to database column limits (NVARCHAR(MAX), approximately 2GB).

**OBX-097**: The `leaseSeconds` parameter SHOULD be between 10 and 300 seconds for optimal performance.

**OBX-098**: The `batchSize` parameter SHOULD be between 1 and 100 for optimal performance.

**OBX-099**: A single Outbox instance MAY process thousands of messages per second, depending on handler complexity and database performance.

### 7.3 Performance Considerations

**OBX-100**: The Outbox SHOULD use database indexes on the `Status` and `CreatedAt` columns to optimize claim queries.

**OBX-101**: The Outbox SHOULD use stored procedures for claim, ack, abandon, and fail operations to minimize round trips.

**OBX-102**: For multi-database scenarios, the Outbox SHOULD cache `IOutbox` instances to avoid recreating them on every operation.

**OBX-103**: The polling service SHOULD implement a backoff mechanism when no messages are available to reduce database load.

### 7.4 Security Considerations

**OBX-104**: The database user configured in `ConnectionString` MUST have SELECT, INSERT, UPDATE, and DELETE permissions on the Outbox tables.

**OBX-105**: The database user MUST have EXECUTE permissions on all Outbox stored procedures.

**OBX-106**: The Outbox MUST NOT log sensitive information from message payloads.

**OBX-107**: The Outbox SHOULD support encrypted connections to the database via the connection string.

## 8. Open Questions / Inconsistencies

### 8.1 Message Ordering

**Observation**: The specification explicitly states that message ordering is not guaranteed, but some users may expect FIFO ordering within a topic.

**Impact**: Users who require ordering must implement their own sequencing logic in handlers.

**Recommendation**: Consider adding an optional "sequence number" or "partition key" feature in a future version to support ordered processing within partitions.

### 8.2 Dead Letter Queue

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
CREATE TABLE [infra].[Outbox] (
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

CREATE INDEX IX_Outbox_WorkQueue ON [infra].[Outbox](Status, CreatedAt) INCLUDE(Id, OwnerToken);
CREATE INDEX IX_Outbox_DueTime ON [infra].[Outbox](DueTimeUtc) WHERE DueTimeUtc IS NOT NULL;
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
            topic: "order.created",
            payload: JsonSerializer.Serialize(order),
            transaction: null,
            correlationId: order.Id.ToString(),
            dueTimeUtc: null,
            cancellationToken: CancellationToken.None);
    }
}
```

---

**End of Specification**
