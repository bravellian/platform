# Inbox Component - Functional Specification

## 1. Meta

| Property | Value |
|----------|-------|
| **Component** | Inbox |
| **Version** | 1.0 |
| **Status** | Active |
| **Owner** | Bravellian Platform Team |
| **Last Updated** | 2025-12-07 |

## 2. Purpose and Scope

### 2.1 Purpose

The Inbox component implements the [Idempotent Receiver pattern](https://www.enterpriseintegrationpatterns.com/patterns/messaging/IdempotentReceiver.html) to ensure reliable, at-most-once message processing in distributed systems. It solves the duplicate message problem by guaranteeing that inbound messages from external systems are processed exactly once, even when delivered multiple times.

### 2.2 Core Responsibilities

1. **Duplicate Detection**: Track processed messages to prevent duplicate processing
2. **Reliable Processing**: Process inbound messages asynchronously using a work queue pattern with claim-ack-abandon semantics
3. **Message Routing**: Route messages to appropriate handlers based on topic
4. **Failure Handling**: Implement automatic retry with exponential backoff for transient failures
5. **Multi-Database Support**: Process messages across multiple databases in multi-tenant scenarios

### 2.3 Scope

**In Scope:**
- Idempotency tracking for inbound messages
- Work queue message processing with leases
- Handler-based message routing and processing
- Automatic retry with configurable backoff policies
- Multi-database/multi-tenant message processing
- Message scheduling with due times
- Content-based deduplication with hash verification
- SQL Server backend implementation

**Out of Scope:**
- Direct message broker integration (handled by user-provided handlers)
- Message transformation or enrichment (handled by user-provided handlers)
- Exactly-once delivery from external systems (they may retry; Inbox provides at-most-once processing)
- Message ordering guarantees within a topic
- Non-SQL Server backends (future consideration)
- Message priority queuing (future consideration)

## 3. Non-Goals

This component does NOT:

1. **Replace Message Brokers**: The Inbox is not a general-purpose message broker. It deduplicates and processes inbound messages from external sources.
2. **Prevent External Retries**: The component cannot control how many times an external system delivers a message. It only ensures each unique message is processed at most once.
3. **Provide Ordered Message Delivery**: Messages may be processed in any order, regardless of arrival time or topic.
4. **Support Cross-Database Transactions**: Each message is tracked in a single database; distributed transactions across databases are not supported.
5. **Implement Business Logic**: The Inbox dispatches messages; all business logic lives in handler implementations.

## 4. Key Concepts and Terms

### 4.1 Core Entities

- **Message**: An inbound unit of work from an external system, consisting of a message ID, source, topic, and payload
- **Message ID**: A unique identifier provided by the external system to identify the message
- **Source**: The originating system or component that sent the message (e.g., "StripeWebhook", "ServiceBusQueue")
- **Topic**: A string identifier used to route messages to appropriate handlers (e.g., "payment.received", "order.created")
- **Payload**: The message content, typically serialized as JSON
- **Hash**: An optional content hash for additional deduplication verification beyond message ID

### 4.2 Strongly-Typed Identifiers

The Inbox component uses strongly-typed identifiers where appropriate:

- **OwnerToken**: A unique identifier for a worker process or instance (implemented as `readonly record struct` wrapper around `Guid`). When a worker claims messages, it provides its `OwnerToken` to establish ownership. Only the owning worker can acknowledge, abandon, or fail messages it has claimed. This prevents conflicts when multiple workers are processing messages concurrently.

- **InboxMessageIdentifier**: The logical message identifier representing the unique combination of `source` and `messageId`. This serves as the natural key for deduplication and is used internally by workers to claim, acknowledge, abandon, or fail messages.

### 4.3 Work Queue Semantics

- **Claim**: Atomically reserve messages for processing with a time-bounded lease
- **Lease**: A time-limited lock on a message, preventing other workers from processing it
- **Acknowledge (Ack)**: Mark a message as successfully processed and transition to Done state
- **Abandon**: Release a message's lease and return it to ready state for retry (used for transient failures)
- **Fail**: Permanently mark a message as dead/poison (used for permanent errors after max retries)
- **Reap**: Recover messages whose leases have expired due to worker crashes or timeouts

### 4.4 Message States

- **Seen**: Initial state when a message is first received, before being enqueued for processing
- **Processing**: Message has been enqueued and is eligible for or currently being processed
- **Done**: Message has been successfully processed
- **Dead**: Message has permanently failed after max retries (poison message)

### 4.5 Multi-Database Concepts

- **Inbox Store**: A single database instance containing an Inbox table
- **Store Provider**: Manages access to multiple inbox stores (one per database/tenant)
- **Selection Strategy**: Algorithm for choosing which store to poll next (e.g., round-robin, drain-first)
- **Router**: Routes operations to the correct inbox store based on a routing key (e.g., tenant ID)

### 4.6 Scheduling Concepts

- **Due Time**: An optional UTC timestamp indicating when a message should become eligible for processing
- **Deferred Message**: A message with a future due time that won't be claimed until that time arrives
- **Next Attempt Time**: The earliest UTC time when a message (including previously failed or abandoned messages) becomes eligible to be claimed again

### 4.7 Deduplication Concepts

- **Message ID Deduplication**: Primary deduplication mechanism using the combination of `source` and `messageId`
- **Hash-Based Deduplication**: Additional verification using a content hash to detect redelivered messages with the same ID but different content
- **Idempotency Window**: The time period during which duplicate detection is guaranteed (typically indefinite, until cleanup)

## 5. Public API Surface

### 5.1 Core Interfaces

#### 5.1.1 IInbox

The primary interface for checking message deduplication status and enqueuing messages for processing.

**Deduplication Operations:**

```csharp
Task<bool> AlreadyProcessedAsync(
    string messageId,
    string source,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`messageId`** (required, non-null): The unique identifier of the message from the external system.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null or empty string
    - MUST NOT exceed 255 characters
    - Case-sensitive
    - Should be unique within the context of the source system
  - **Purpose**: Primary key component for deduplication

- **`source`** (required, non-null): The originating system or component.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null or empty string
    - MUST NOT exceed 255 characters
    - Case-sensitive
    - Should uniquely identify the external system (e.g., "StripeWebhook", "AzureServiceBus")
  - **Purpose**: Namespaces message IDs to prevent collisions between different systems

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`
  - **Purpose**: Allows cancellation of the async operation

```csharp
Task<bool> AlreadyProcessedAsync(
    string messageId,
    string source,
    byte[]? hash,
    CancellationToken cancellationToken)
```

**Additional Parameter:**

- **`hash`** (optional): Content hash for additional verification.
  - **Type**: `byte[]?`
  - **Constraints**:
    - MAY be null
    - When non-null, recommended to use SHA256 (32 bytes) or similar cryptographic hash
    - Maximum size limited by database VARBINARY(MAX)
  - **Purpose**: Provides content-based deduplication to detect messages with the same ID but different content

**State Management Operations:**

```csharp
Task MarkProcessingAsync(
    string messageId,
    string source,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`messageId`** (required, non-null): The unique identifier of the message.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Part of the composite key identifying which message to transition to Processing state

- **`source`** (required, non-null): The originating system or component.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Part of the composite key identifying which message to transition to Processing state

- **`cancellationToken`** (required): Cancellation token for the operation.

```csharp
Task MarkProcessedAsync(
    string messageId,
    string source,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`messageId`** (required, non-null): The unique identifier of the message.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Part of the composite key identifying which message to mark as successfully processed (Done state)

- **`source`** (required, non-null): The originating system or component.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Part of the composite key identifying which message to mark as successfully processed (Done state)

- **`cancellationToken`** (required): Cancellation token for the operation.

```csharp
Task MarkDeadAsync(
    string messageId,
    string source,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`messageId`** (required, non-null): The unique identifier of the message.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Part of the composite key identifying which message to mark as permanently failed (Dead state)

- **`source`** (required, non-null): The originating system or component.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Part of the composite key identifying which message to mark as permanently failed (Dead state)

- **`cancellationToken`** (required): Cancellation token for the operation.

**Enqueue Operations:**

```csharp
Task EnqueueAsync(
    string topic,
    string source,
    string messageId,
    string payload,
    byte[]? hash,
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
  - **Purpose**: Routes the message to the appropriate `IInboxHandler` implementation

- **`source`** (required, non-null): The originating system or component.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Used as part of the deduplication key

- **`messageId`** (required, non-null): The unique identifier of the message.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Used as part of the deduplication key

- **`payload`** (required, non-null): The message content.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null
    - MAY be empty string (valid for messages with no body)
    - Maximum size limited by database NVARCHAR(MAX) (approximately 2GB)
  - **Format**: Typically JSON, but the Inbox does not enforce or validate format. Handlers are responsible for deserialization.
  - **Purpose**: Contains the message data to be processed by handlers

- **`hash`** (optional): Content hash for deduplication.
  - Same constraints as in `AlreadyProcessedAsync`
  - **Purpose**: Additional verification to detect duplicate messages

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

_Implementation note_: The runtime MAY expose convenience overloads of `EnqueueAsync` and `AlreadyProcessedAsync` that omit `hash`, `dueTimeUtc`, or both. For the purposes of this specification, all overloads are defined in terms of these canonical signatures. Omitted arguments are treated as if `null` (or `CancellationToken.None`) was passed.

#### 5.1.2 IInboxWorkStore

Low-level work queue interface for message processing and lifecycle management.

**Work Queue Operations:**

```csharp
Task<IReadOnlyList<string>> ClaimAsync(
    OwnerToken ownerToken,
    int leaseSeconds,
    int batchSize,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`ownerToken`** (required): A stable identifier for the worker instance.
  - **Type**: `OwnerToken` (strongly-typed GUID).
  - **Constraints**:
    - MUST NOT be the default/empty value.
    - SHOULD remain constant for the lifetime of the worker process instance.
  - **Purpose**: Used to enforce that only the owning worker can ack/abandon/fail the messages it claimed.

- **`leaseSeconds`** (required): Duration in seconds for which the claim is valid.
  - **Type**: `int`
  - **Constraints**:
    - MUST be greater than 0.
    - SHOULD be between 10 and 300 seconds for typical workloads.
    - If `leaseSeconds <= 0`, the method MUST throw an `ArgumentOutOfRangeException`.
  - **Purpose**: Controls how long messages are locked before they can be reaped or reclaimed.

- **`batchSize`** (required): Maximum number of messages to claim.
  - **Type**: `int`
  - **Constraints**:
    - MUST be greater than 0.
    - SHOULD be between 1 and 100 for typical workloads.
    - If `batchSize <= 0`, the method MUST throw an `ArgumentOutOfRangeException`.
  - **Purpose**: Batches work for efficiency while bounding per-iteration load.

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`
  - **Purpose**: Allows cancelling the claim operation.

```csharp
Task AckAsync(
    OwnerToken ownerToken,
    IEnumerable<string> messageIds,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`ownerToken`** (required): The worker identity that previously claimed the messages.
  - **Constraints**:
    - MUST match the `OwnerToken` currently stored on each message to be updated.
  - **Behavior**:
    - Messages whose `OwnerToken` does not match are silently ignored and MUST NOT cause the method to fail.

- **`messageIds`** (required): The set of message identifiers to acknowledge.
  - **Type**: `IEnumerable<string>`
  - **Constraints**:
    - MUST NOT be null.
    - MAY be empty; an empty sequence is treated as a no-op.
    - Duplicate IDs within the sequence MUST be tolerated and MUST NOT cause errors.
  - **Purpose**: Indicates which claimed messages to transition to Done state.

- **`cancellationToken`** (required): Cancellation token for the operation.

```csharp
Task AbandonAsync(
    OwnerToken ownerToken,
    IEnumerable<string> messageIds,
    string? lastError,
    TimeSpan? delay,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`ownerToken`** (required): The worker identity that previously claimed the messages.
  - Same constraints as in `AckAsync`.

- **`messageIds`** (required): The set of message identifiers to abandon.
  - Same constraints as in `AckAsync`.

- **`lastError`** (optional): Error message to record for troubleshooting.
  - **Type**: `string?`
  - **Constraints**:
    - MAY be null or empty.
    - Implementations SHOULD normalize empty strings to null to avoid noise.
  - **Purpose**: Records diagnostic information about why the message was abandoned.

- **`delay`** (optional): Delay before the message becomes eligible for retry.
  - **Type**: `TimeSpan?`
  - **Constraints**:
    - MAY be null (uses default backoff).
    - If non-null, MUST be greater than zero; zero or negative values MUST result in an `ArgumentOutOfRangeException`.
  - **Purpose**: Implements exponential backoff or custom retry delays.

- **`cancellationToken`** (required): Cancellation token for the operation.

```csharp
Task FailAsync(
    OwnerToken ownerToken,
    IEnumerable<string> messageIds,
    string error,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`ownerToken`** (required): The worker identity that previously claimed the messages.
  - Same constraints as in `AckAsync`.

- **`messageIds`** (required): The set of message identifiers to fail.
  - Same constraints as in `AckAsync`.

- **`error`** (required): Error message to record.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null.
    - MAY be empty, but SHOULD contain meaningful diagnostic information.
  - **Purpose**: Records why the message was permanently failed.

- **`cancellationToken`** (required): Cancellation token for the operation.

```csharp
Task ReapExpiredAsync(CancellationToken cancellationToken)
```

**Parameters:**

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Purpose**: Allows cancellation of the reap operation. If cancellation is requested, the method MUST stop processing as soon as practical.

```csharp
Task<InboxMessage> GetAsync(
    string messageId,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`messageId`** (required, non-null): The message identifier to retrieve.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null or empty.
  - **Purpose**: Identifies which message to retrieve.

- **`cancellationToken`** (required): Cancellation token for the operation.

#### 5.1.3 IInboxHandler

Interface for implementing message handlers.

```csharp
string Topic { get; }  // The topic this handler processes
Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
```

**Topic Property Constraints:**

- `Topic` MUST be non-null and non-empty.
- The handler's `Topic` MUST match the `topic` value used in `EnqueueAsync` calls in a case-sensitive manner.
- The same length and character recommendations as `topic` (see §5.1.1) apply.

**HandleAsync Parameters:**

- `message`: The claimed inbox message to process. MUST NOT be null.
- `cancellationToken`: Standard cancellation token. Handlers SHOULD honor cancellation where practical.

#### 5.1.4 IInboxRouter

Interface for routing operations to specific databases in multi-tenant scenarios.

```csharp
IInbox GetInbox(string routingKey)
```

**Parameters:**

- **`routingKey`** (required): Opaque routing identifier used to choose a tenant/database.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null or empty.
    - SHOULD be stable and unique per inbox store (e.g., tenant ID, database name).
  - **Behavior**:
    - If no inbox exists for the specified `routingKey`, the method MUST throw `InvalidOperationException`.

_Implementation note_: Some implementations MAY provide additional overloads (e.g., `GetInbox(Guid tenantId)`) that internally convert other key types to strings before delegating to this canonical method. Such overloads are convenience APIs and do not change the specified behavior.

### 5.2 Data Types

#### 5.2.1 InboxMessage

```csharp
public sealed record InboxMessage
{
    public string MessageId { get; internal init; }
    public string Source { get; internal init; }
    public string Topic { get; internal init; }
    public string Payload { get; internal init; }
    public byte[]? Hash { get; internal init; }
    public int Attempt { get; internal init; }
    public DateTimeOffset FirstSeenUtc { get; internal init; }
    public DateTimeOffset LastSeenUtc { get; internal init; }
    public DateTimeOffset? DueTimeUtc { get; internal init; }
    public string? LastError { get; internal init; }
}
```

#### 5.2.2 Configuration Types

```csharp
public class SqlInboxOptions
{
    public string ConnectionString { get; set; }
    public string SchemaName { get; set; } = "infra";
    public string TableName { get; set; } = "Inbox";
    public bool EnableSchemaDeployment { get; set; } = false;
}
```

### 5.3 Service Registration

```csharp
// Single database
IServiceCollection AddSqlInbox(this IServiceCollection services, SqlInboxOptions options)

// Multiple databases (static configuration)
IServiceCollection AddMultiSqlInbox(this IServiceCollection services, IEnumerable<SqlInboxOptions> options)

// Multiple databases (dynamic discovery)
IServiceCollection AddDynamicMultiSqlInbox(this IServiceCollection services, TimeSpan? refreshInterval = null)

// Handler registration
IServiceCollection AddInboxHandler<THandler>(this IServiceCollection services) where THandler : class, IInboxHandler
```

## 6. Behavioral Requirements

### 6.1 Deduplication and State Tracking

**IBX-001**: The Inbox MUST use the combination of `source` and `messageId` as the natural key for deduplication.

**IBX-002**: When `AlreadyProcessedAsync` is called, the Inbox MUST atomically check if a message with the given `source` and `messageId` exists and is in Done state.

**IBX-003**: If the message is in Done state, `AlreadyProcessedAsync` MUST return `true`.

**IBX-004**: If the message does not exist, `AlreadyProcessedAsync` MUST insert it in Seen state and return `false`.

**IBX-005**: If the message exists but is not in Done state, `AlreadyProcessedAsync` MUST return `false` and update `LastSeenUtc` to current UTC time.

**IBX-006**: `AlreadyProcessedAsync` operations MUST be implemented using MERGE/UPSERT semantics to handle concurrent calls safely.

**IBX-007**: When `hash` is provided to `AlreadyProcessedAsync`, the Inbox MUST store it for verification on subsequent calls.

**IBX-008**: If a message with the same `source` and `messageId` is received with a different hash, the Inbox SHOULD log a warning about potential content mismatch but MUST NOT fail the operation.

**IBX-009**: The Inbox MUST reject `AlreadyProcessedAsync` calls with null or empty `messageId` by throwing an `ArgumentException`.

**IBX-010**: The Inbox MUST reject `AlreadyProcessedAsync` calls with null or empty `source` by throwing an `ArgumentException`.

**IBX-011**: If `messageId` exceeds 255 characters, the Inbox MUST throw an `ArgumentException` or allow the database to reject it with a SQL exception.

**IBX-012**: If `source` exceeds 255 characters, the Inbox MUST throw an `ArgumentException` or allow the database to reject it with a SQL exception.

### 6.2 Message Enqueuing

**IBX-013**: The Inbox MUST persist messages durably to a SQL Server table when `EnqueueAsync` is called.

**IBX-014**: `EnqueueAsync` MUST use MERGE/UPSERT semantics to handle duplicate enqueue attempts safely.

**IBX-015**: The Inbox MUST reject `EnqueueAsync` calls with null or empty `topic` by throwing an `ArgumentException`.

**IBX-016**: The Inbox MUST reject `EnqueueAsync` calls with null or empty `source` by throwing an `ArgumentException`.

**IBX-017**: The Inbox MUST reject `EnqueueAsync` calls with null or empty `messageId` by throwing an `ArgumentException`.

**IBX-018**: The Inbox MUST reject `EnqueueAsync` calls with null `payload` by throwing an `ArgumentException`.

**IBX-019**: The Inbox MUST accept empty string ("") as a valid `payload` value.

**IBX-020**: If `topic` exceeds 255 characters, the Inbox MUST throw an `ArgumentException` or allow the database to reject it with a SQL exception.

**IBX-021**: The `topic` parameter is case-sensitive. "Order.Created" and "order.created" are treated as different topics.

**IBX-022**: The Inbox does NOT validate or enforce any format for `payload`. Handlers are responsible for deserializing and validating payload content.

**IBX-023**: If `dueTimeUtc` is provided and is in the future, the Inbox MUST NOT make the message available for claiming until that time has passed.

**IBX-024**: If `dueTimeUtc` is null or in the past, the Inbox MUST make the message immediately available for claiming.

**IBX-025**: The Inbox MUST record the `FirstSeenUtc` timestamp using the database server's UTC time upon first insertion.

**IBX-026**: The Inbox MUST update the `LastSeenUtc` timestamp to the database server's UTC time on subsequent MERGE operations.

**IBX-027**: The Inbox MUST initialize newly enqueued messages with `Attempt` = 0 and `Status` = Processing.

**IBX-028**: When a message is enqueued that already exists in Done state, the Inbox MUST NOT change its state.

**IBX-029**: When a message is enqueued that exists in Seen, Processing, or Dead state, the Inbox MUST update the topic, payload, hash, and dueTimeUtc to the new values.

### 6.3 Message Claiming

**IBX-030**: `ClaimAsync` MUST atomically select and lock up to `batchSize` ready messages using database-level locking mechanisms (e.g., `WITH (UPDLOCK, READPAST, ROWLOCK)`).

**IBX-031**: `ClaimAsync` MUST only claim messages in Processing status.

**IBX-032**: `ClaimAsync` MUST only claim messages where `DueTimeUtc` is null or less than or equal to the current UTC time.

**IBX-033**: `ClaimAsync` MUST only claim messages that are not currently leased by another worker (i.e., `LockedUntil` is null or in the past).

**IBX-034**: `ClaimAsync` MUST set `LockedUntil` to the current UTC time plus `leaseSeconds`.

**IBX-035**: `ClaimAsync` MUST set `OwnerToken` to the provided `ownerToken` value.

**IBX-036**: `ClaimAsync` MUST return a list of message IDs for all successfully claimed messages.

**IBX-037**: If no messages are ready, `ClaimAsync` MUST return an empty list without throwing an exception.

**IBX-038**: `ClaimAsync` MUST NOT claim messages that are in Done or Dead state.

**IBX-039**: `ClaimAsync` MUST respect the `batchSize` limit and MUST NOT claim more messages than requested.

**IBX-040**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `leaseSeconds <= 0`.

**IBX-041**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `batchSize <= 0`.

**IBX-042**: `ClaimAsync` MUST only claim messages where the next attempt time (accounting for backoff) is less than or equal to the current UTC time.

**IBX-043**: `ClaimAsync` MUST throw an `ArgumentException` if `ownerToken` is the default/empty GUID value.

### 6.4 Message Acknowledgment

**IBX-044**: `AckAsync` MUST mark the specified messages as successfully processed by transitioning them to Done state.

**IBX-045**: `AckAsync` MUST only acknowledge messages whose `OwnerToken` matches the provided `ownerToken`.

**IBX-046**: `AckAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**IBX-047**: After `AckAsync` completes, the acknowledged messages MUST NOT be returned by subsequent `ClaimAsync` calls.

**IBX-048**: `AckAsync` MUST throw an `ArgumentNullException` if `messageIds` is null, and MUST treat an empty `messageIds` collection as a no-op.

**IBX-049**: `AckAsync` MUST clear the `OwnerToken` and `LockedUntil` when transitioning to Done state.

### 6.5 Message Abandonment

**IBX-050**: `AbandonAsync` MUST release the lease on the specified messages by setting `LockedUntil` to null and `OwnerToken` to null.

**IBX-051**: `AbandonAsync` MUST increment the `Attempt` counter for each abandoned message.

**IBX-052**: `AbandonAsync` MUST calculate a new next attempt time using exponential backoff based on `Attempt`, unless a specific `delay` is provided.

**IBX-053**: If `delay` is provided and is greater than zero, `AbandonAsync` MUST use that specific delay value instead of the default backoff calculation.

**IBX-054**: `AbandonAsync` MUST only abandon messages whose `OwnerToken` matches the provided `ownerToken`.

**IBX-055**: `AbandonAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**IBX-056**: After `AbandonAsync` completes, the abandoned messages MUST become available for claiming again after the backoff/delay period expires.

**IBX-057**: `AbandonAsync` SHOULD record the `lastError` message if provided by the caller.

**IBX-058**: If `delay` is provided but is zero or negative, `AbandonAsync` MUST throw an `ArgumentOutOfRangeException`.

**IBX-059**: `AbandonAsync` MUST throw an `ArgumentNullException` if `messageIds` is null, and MUST treat an empty `messageIds` collection as a no-op.

### 6.6 Message Failure

**IBX-060**: `FailAsync` MUST mark the specified messages as permanently failed by transitioning them to Dead state.

**IBX-061**: `FailAsync` MUST record the `error` message provided by the caller.

**IBX-062**: `FailAsync` MUST only fail messages whose `OwnerToken` matches the provided `ownerToken`.

**IBX-063**: `FailAsync` MUST ignore message IDs that do not exist or do not match the owner token, without throwing an exception.

**IBX-064**: After `FailAsync` completes, the failed messages MUST NOT be returned by subsequent `ClaimAsync` calls.

**IBX-065**: `FailAsync` MUST clear the `OwnerToken` and `LockedUntil` when transitioning to Dead state.

**IBX-066**: `FailAsync` MUST throw an `ArgumentNullException` if `messageIds` is null, and MUST treat an empty `messageIds` collection as a no-op.

**IBX-067**: `FailAsync` MUST throw an `ArgumentNullException` if `error` is null.

### 6.7 Lease Expiration and Reaping

**IBX-068**: `ReapExpiredAsync` MUST identify all messages where `LockedUntil` is not null and is less than the current UTC time.

**IBX-069**: `ReapExpiredAsync` MUST release the lease on expired messages by setting `LockedUntil` to null and `OwnerToken` to null.

**IBX-070**: `ReapExpiredAsync` MUST make reaped messages available for claiming by subsequent `ClaimAsync` calls, subject to backoff constraints.

**IBX-071**: `ReapExpiredAsync` MUST NOT modify messages that are in Done or Dead state.

**IBX-072**: The Inbox polling service SHOULD call `ReapExpiredAsync` periodically to recover from worker crashes.

### 6.8 Message Handlers

**IBX-073**: The Inbox dispatcher MUST route each claimed message to the handler whose `Topic` property matches the message's topic.

**IBX-074**: If no handler is registered for a message's topic, the Inbox dispatcher MUST log a warning and SHOULD abandon the message for retry.

**IBX-075**: If a handler throws an exception, the Inbox dispatcher MUST catch the exception and determine whether to abandon or fail the message based on a backoff policy.

**IBX-076**: Handlers MUST be invoked with the full `InboxMessage` object and a `CancellationToken`.

**IBX-077**: Handlers SHOULD be idempotent, as messages may be processed more than once if abandoned and retried.

**IBX-078**: The Inbox dispatcher MUST NOT call handlers concurrently for the same message.

**IBX-079**: The Inbox dispatcher MAY call handlers concurrently for different messages.

### 6.9 Retry and Backoff

**IBX-080**: The Inbox MUST implement exponential backoff for retrying failed messages.

**IBX-081**: The default backoff policy SHOULD use the formula: `delay = min(2^attempt seconds, 60 seconds)`.

**IBX-082**: The backoff policy MAY be customizable via configuration or dependency injection.

**IBX-083**: After the maximum attempt count is reached, the Inbox SHOULD permanently fail the message by calling `FailAsync`.

**IBX-084**: The maximum attempt count SHOULD be configurable, with a sensible default (e.g., 10 attempts).

### 6.10 Multi-Database Support

**IBX-085**: When configured with multiple databases via `AddMultiSqlInbox`, the Inbox MUST maintain separate stores for each database.

**IBX-086**: The Inbox dispatcher MUST use an `IInboxSelectionStrategy` to determine which store to poll on each iteration.

**IBX-087**: The provided `RoundRobinInboxSelectionStrategy` MUST cycle through all stores in order, processing one batch from each before moving to the next.

**IBX-088**: The provided `DrainFirstInboxSelectionStrategy` MUST continue processing from the same store until it returns no messages, then move to the next store.

**IBX-089**: The `IInboxWorkStoreProvider` MUST return a consistent identifier for each store via `GetStoreIdentifier`.

**IBX-090**: The Inbox dispatcher MUST log the store identifier when processing messages to aid in troubleshooting.

**IBX-091**: The `IInboxRouter.GetInbox(key)` MUST return the `IInbox` instance associated with the specified routing key.

**IBX-092**: The `IInboxRouter` MUST throw an `InvalidOperationException` if no inbox exists for the specified routing key.

### 6.11 Dynamic Database Discovery

**IBX-093**: When configured with `AddDynamicMultiSqlInbox`, the Inbox MUST periodically invoke `IInboxDatabaseDiscovery.DiscoverDatabasesAsync` to refresh the list of databases.

**IBX-094**: The default refresh interval SHOULD be 5 minutes.

**IBX-095**: When new databases are discovered, the dynamic provider MUST create new inbox stores for those databases.

**IBX-096**: When databases are removed from discovery results, the dynamic provider MUST remove the corresponding inbox stores.

**IBX-097**: The dynamic provider MUST NOT unnecessarily recreate stores if the database configuration has not changed.

### 6.12 Cleanup Operations

**IBX-098**: The Inbox MAY provide cleanup operations to remove old Done messages from the database.

**IBX-099**: Cleanup operations MUST NOT remove messages that are in Seen, Processing, or Dead state.

**IBX-100**: Cleanup operations SHOULD be configurable with a retention period (e.g., keep messages for 30 days).

**IBX-101**: Cleanup operations MUST use batching to avoid long-running transactions.

### 6.13 Concurrency and Consistency

**IBX-102**: All database operations within a single Inbox method call MUST execute within a single transaction to ensure atomicity.

**IBX-103**: The Inbox MUST use appropriate database isolation levels to prevent dirty reads, non-repeatable reads, and phantom reads during claim operations.

**IBX-104**: The Inbox MUST handle database deadlocks gracefully by retrying the operation or propagating the exception to the caller.

**IBX-105**: Multiple worker processes MAY safely operate on the same Inbox table concurrently.

**IBX-106**: The Inbox MUST ensure that a message is never claimed by more than one worker at the same time.

### 6.14 Observability

**IBX-107**: The Inbox SHOULD log all deduplication checks at DEBUG level, including whether the message was already processed.

**IBX-108**: The Inbox SHOULD log all enqueue operations at INFO level, including topic, source, and message ID.

**IBX-109**: The Inbox SHOULD log all claim operations at DEBUG level, including the number of messages claimed.

**IBX-110**: The Inbox SHOULD log all handler invocations at INFO level, including topic and message ID.

**IBX-111**: The Inbox MUST log handler exceptions at ERROR level, including the exception details and message ID.

**IBX-112**: The Inbox SHOULD log reap operations at INFO level, including the number of messages reaped.

**IBX-113**: For multi-database scenarios, all log messages SHOULD include the store identifier to aid in troubleshooting.

### 6.15 Schema Deployment

**IBX-114**: When `EnableSchemaDeployment` is true, the Inbox MUST create the necessary database tables and indexes if they do not already exist.

**IBX-115**: When `EnableSchemaDeployment` is false, the Inbox MUST assume the schema exists and MUST NOT attempt to create it.

**IBX-116**: Schema deployment operations SHOULD be idempotent; running them multiple times MUST NOT cause errors.

**IBX-117**: The Inbox schema MUST include the `Inbox` table and associated indexes needed for claiming and processing messages.

## 7. Configuration and Limits

### 7.1 Configuration Options

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `ConnectionString` | string | (required) | SQL Server connection string |
| `SchemaName` | string | "infra" | Database schema name |
| `TableName` | string | "Inbox" | Inbox table name |
| `EnableSchemaDeployment` | bool | false | Automatically create schema objects |
| `PollingIntervalSeconds` | double | 0.5 | Interval between polling iterations |
| `BatchSize` | int | 50 | Maximum messages to claim per iteration |
| `LeaseSeconds` | int | 30 | Duration to hold message leases |
| `MaxAttempts` | int | 10 | Maximum retry attempts before failing |
| `CleanupRetentionDays` | int | 30 | Days to retain Done messages before cleanup |

### 7.2 Limits and Constraints

The following constraints are enforced by the Inbox component:

- **MessageId**: Maximum 255 characters (IBX-011)
- **Source**: Maximum 255 characters (IBX-012)
- **Topic**: Maximum 255 characters (IBX-020)
- **Payload**: Maximum ~2GB (NVARCHAR(MAX) limit) (IBX-118)
- **Hash**: Maximum ~2GB (VARBINARY(MAX) limit), recommended SHA256 (32 bytes)
- **LeaseSeconds**: Recommended 10-300 seconds (IBX-119)
- **BatchSize**: Recommended 1-100 (IBX-120)

**IBX-118**: The `payload` parameter MAY be arbitrarily large, subject to database column limits (NVARCHAR(MAX), approximately 2GB).

**IBX-119**: The `leaseSeconds` parameter SHOULD be between 10 and 300 seconds for optimal performance.

**IBX-120**: The `batchSize` parameter SHOULD be between 1 and 100 for optimal performance.

**IBX-121**: A single Inbox instance MAY process thousands of messages per second, depending on handler complexity and database performance.

### 7.3 Performance Considerations

**IBX-122**: The Inbox SHOULD use database indexes on the `Status`, `LockedUntil`, and `DueTimeUtc` columns to optimize claim queries.

**IBX-123**: For multi-database scenarios, the Inbox SHOULD cache `IInbox` instances to avoid recreating them on every operation.

**IBX-124**: The polling service SHOULD implement a backoff mechanism when no messages are available to reduce database load.

**IBX-125**: The Inbox SHOULD use composite indexes on (`Source`, `MessageId`) for efficient deduplication lookups.

### 7.4 Security Considerations

**IBX-126**: The database user configured in `ConnectionString` MUST have SELECT, INSERT, UPDATE, and DELETE permissions on the Inbox tables.

**IBX-127**: The Inbox MUST NOT log sensitive information from message payloads.

**IBX-128**: The Inbox SHOULD support encrypted connections to the database via the connection string.

**IBX-129**: Content hashes SHOULD use cryptographic hash functions (e.g., SHA256) to prevent collision attacks.

**IBX-130**: If `ownerToken` is the default/empty GUID value, methods that accept it MUST throw an `ArgumentException`.

## 8. Open Questions / Inconsistencies

### 8.1 Message Ordering

**Observation**: The specification explicitly states that message ordering is not guaranteed, but some users may expect FIFO ordering within a source or topic.

**Impact**: Users who require ordering must implement their own sequencing logic in handlers.

**Recommendation**: Consider adding an optional "sequence number" or "partition key" feature in a future version to support ordered processing within partitions.

### 8.2 Dead Letter Handling

**Observation**: The current implementation marks messages as Dead after max retries but does not provide dedicated monitoring or replay mechanisms.

**Impact**: Dead messages remain in the Inbox table. Users must query the table directly to find and investigate failed messages.

**Recommendation**: Consider adding a configurable dead letter notification mechanism or administrative API for reviewing and replaying dead messages in a future version.

### 8.3 Message TTL (Time-to-Live)

**Observation**: The current implementation does not provide a message expiration or TTL mechanism.

**Impact**: Messages that become irrelevant (e.g., due to time-sensitive business logic) may still be processed.

**Recommendation**: Consider adding an optional `ExpiresAt` field in a future version to automatically fail messages past their expiration time.

### 8.4 Hash Algorithm Specification

**Observation**: The specification recommends SHA256 for content hashing but does not enforce or validate the hash algorithm used.

**Impact**: Different systems might use different hash algorithms, leading to incompatibility.

**Recommendation**: Consider documenting the expected hash algorithm in integration contracts or adding an optional hash algorithm identifier in a future version.

---

## Appendix A: Database Schema Reference

### A.1 Inbox Table

```sql
CREATE TABLE [infra].[Inbox] (
    -- Natural key for deduplication
    Source NVARCHAR(255) NOT NULL,
    MessageId NVARCHAR(255) NOT NULL,
    
    -- Message content and routing
    Topic NVARCHAR(255) NOT NULL,
    Payload NVARCHAR(MAX) NOT NULL,
    Hash VARBINARY(MAX) NULL,
    
    -- Tracking timestamps
    FirstSeenUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastSeenUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    
    -- Work queue state
    Status VARCHAR(20) NOT NULL DEFAULT 'Seen',  -- Seen, Processing, Done, Dead
    LockedUntil DATETIME2(3) NULL,
    OwnerToken UNIQUEIDENTIFIER NULL,
    
    -- Retry logic
    Attempt INT NOT NULL DEFAULT 0,
    LastError NVARCHAR(MAX) NULL,
    NextAttemptAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    
    -- Scheduling
    DueTimeUtc DATETIME2(3) NULL,
    
    -- Primary key
    CONSTRAINT PK_Inbox PRIMARY KEY (Source, MessageId)
);

-- Index for work queue claims
CREATE INDEX IX_Inbox_WorkQueue 
    ON [infra].[Inbox](Status, NextAttemptAt, DueTimeUtc) 
    INCLUDE(Source, MessageId, OwnerToken, LockedUntil)
    WHERE Status = 'Processing';

-- Index for due time queries
CREATE INDEX IX_Inbox_DueTime 
    ON [infra].[Inbox](DueTimeUtc) 
    WHERE DueTimeUtc IS NOT NULL AND Status = 'Processing';

-- Index for cleanup operations
CREATE INDEX IX_Inbox_Cleanup 
    ON [infra].[Inbox](Status, LastSeenUtc) 
    WHERE Status = 'Done';
```

## Appendix B: Handler Implementation Example

```csharp
public class PaymentReceivedHandler : IInboxHandler
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentReceivedHandler> _logger;

    public PaymentReceivedHandler(
        IPaymentService paymentService, 
        ILogger<PaymentReceivedHandler> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    public string Topic => "payment.received";

    public async Task HandleAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        var paymentData = JsonSerializer.Deserialize<PaymentData>(message.Payload);
        
        _logger.LogInformation(
            "Processing payment {PaymentId} from {Source}, attempt {Attempt}",
            message.MessageId, 
            message.Source, 
            message.Attempt);
        
        // Handler should be idempotent - the message may be retried
        await _paymentService.ProcessPaymentAsync(
            paymentData, 
            message.MessageId,  // Use message ID for idempotency
            cancellationToken);
    }
}
```

## Appendix C: Webhook Integration Example

```csharp
// Webhook controller
[ApiController]
[Route("webhooks/stripe")]
public class StripeWebhookController : ControllerBase
{
    private readonly IInbox _inbox;
    private readonly ILogger<StripeWebhookController> _logger;
    private readonly IConfiguration _configuration;

    public StripeWebhookController(
        IInbox inbox, 
        ILogger<StripeWebhookController> logger,
        IConfiguration configuration)
    {
        _inbox = inbox;
        _logger = logger;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> HandleWebhook()
    {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        
        // IMPORTANT: Verify webhook signature before processing
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();
        if (string.IsNullOrEmpty(signature))
        {
            _logger.LogWarning("Webhook received without Stripe-Signature header");
            return Unauthorized("Missing signature");
        }
        
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (!VerifyStripeSignature(json, signature, webhookSecret))
        {
            _logger.LogWarning("Webhook signature verification failed");
            return Unauthorized("Invalid signature");
        }
        
        var stripeEvent = JsonSerializer.Deserialize<StripeEvent>(json);
        
        // Calculate content hash for deduplication
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        
        // Check if we've already processed this webhook
        var alreadyProcessed = await _inbox.AlreadyProcessedAsync(
            messageId: stripeEvent.Id,
            source: "StripeWebhook",
            hash: hash,
            cancellationToken: HttpContext.RequestAborted);
        
        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Webhook {EventId} already processed, returning 200", 
                stripeEvent.Id);
            return Ok();
        }
        
        // Enqueue for async processing
        await _inbox.EnqueueAsync(
            topic: $"stripe.{stripeEvent.Type}",
            source: "StripeWebhook",
            messageId: stripeEvent.Id,
            payload: json,
            hash: hash,
            dueTimeUtc: null,
            cancellationToken: HttpContext.RequestAborted);
        
        _logger.LogInformation(
            "Webhook {EventId} enqueued for processing", 
            stripeEvent.Id);
        
        return Ok();
    }
    
    private bool VerifyStripeSignature(string payload, string signature, string secret)
    {
        // Stripe signature format: t=timestamp,v1=signature
        // This is a simplified example - use Stripe SDK in production
        var parts = signature.Split(',');
        var timestamp = parts.FirstOrDefault(p => p.StartsWith("t="))?.Substring(2);
        var sig = parts.FirstOrDefault(p => p.StartsWith("v1="))?.Substring(3);
        
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(sig))
            return false;
        
        // Verify timestamp is recent (within 5 minutes) to prevent replay attacks
        if (long.TryParse(timestamp, out var ts))
        {
            var webhookTime = DateTimeOffset.FromUnixTimeSeconds(ts);
            if (DateTimeOffset.UtcNow - webhookTime > TimeSpan.FromMinutes(5))
            {
                _logger.LogWarning("Webhook timestamp too old: {Timestamp}", webhookTime);
                return false;
            }
        }
        
        // Compute expected signature: HMAC-SHA256 of "timestamp.payload" with secret
        var signedPayload = $"{timestamp}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
        var expectedSig = Convert.ToHexString(hash).ToLowerInvariant();
        
        return sig == expectedSig;
    }
}
```

## Appendix D: Multi-Tenant Usage Example

```csharp
// Service configuration
public void ConfigureServices(IServiceCollection services)
{
    // Register dynamic discovery
    services.AddSingleton<IInboxDatabaseDiscovery, TenantDatabaseDiscovery>();
    
    // Register multi-inbox with round-robin strategy
    services.AddDynamicMultiSqlInbox(refreshInterval: TimeSpan.FromMinutes(5));
    
    // Register handlers
    services.AddInboxHandler<PaymentReceivedHandler>();
    services.AddInboxHandler<OrderCreatedHandler>();
}

// Application code
public class WebhookService
{
    private readonly IInboxRouter _inboxRouter;
    
    public WebhookService(IInboxRouter inboxRouter)
    {
        _inboxRouter = inboxRouter;
    }
    
    public async Task ProcessWebhookAsync(
        string tenantId, 
        string eventId, 
        string eventType, 
        string payload)
    {
        // Get the inbox for this specific tenant
        var inbox = _inboxRouter.GetInbox(tenantId);
        
        // Check for duplicates
        var alreadyProcessed = await inbox.AlreadyProcessedAsync(
            messageId: eventId,
            source: "ExternalWebhook",
            cancellationToken: CancellationToken.None);
        
        if (!alreadyProcessed)
        {
            // Enqueue message to the tenant's database
            await inbox.EnqueueAsync(
                topic: eventType,
                source: "ExternalWebhook",
                messageId: eventId,
                payload: payload,
                hash: null,
                dueTimeUtc: null,
                cancellationToken: CancellationToken.None);
        }
    }
}
```

---

## End of Specification
