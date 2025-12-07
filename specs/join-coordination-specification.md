# Join Coordination Component - Functional Specification

## 1. Meta

| Property | Value |
|----------|-------|
| **Component** | Join Coordination |
| **Version** | 1.0 |
| **Status** | Active |
| **Owner** | Bravellian Platform Team |
| **Last Updated** | 2025-12-07 |
| **Dependencies** | Outbox Component (v1.0) |

## 2. Purpose and Scope

### 2.1 Purpose

The Join Coordination component provides fan-in coordination for workflow orchestration, enabling systems to wait for the completion of multiple related messages before proceeding to the next step. It implements the fan-in pattern as a primitive built on top of the Outbox component, tracking groups of related Outbox messages (steps) and coordinating their completion so that follow-up actions can run once all steps are either completed or failed.

### 2.2 Architecture and Integration with Outbox

Join coordination is built **on top of** the Outbox component:

- **Joins use `OutboxMessageIdentifier`** to track message relationships
- **Join tables are separate**: `OutboxJoin` and `OutboxJoinMember` tables are independent from the `Outbox` table
- **Outbox messages remain join-agnostic**: Outbox messages do not contain join identifiers; the association is maintained in the `OutboxJoinMember` table
- **Integration via stored procedures**: The Join component hooks into the same stored procedures (`Outbox_Ack` and `Outbox_Fail`) that mark messages as completed or failed, automatically updating join counters
- **Database-driven automation**: Join counters are incremented automatically by the database when Outbox messages are acknowledged or failed
- **Architectural separation**: The Outbox component itself has no knowledge of joins and remains completely join-agnostic

This architectural approach ensures that:
1. The Outbox component can be used independently without join coordination
2. Join coordination can be added incrementally to existing Outbox deployments
3. Outbox message processing requires no join-specific logic in handlers
4. Join state updates happen atomically with message acknowledgment/failure

### 2.3 Core Responsibilities

1. **Join Creation** – Create a join with an expected number of steps and optional grouping/metadata
2. **Membership Tracking** – Attach Outbox messages to a join idempotently
3. **Progress Accounting** – Track completed and failed steps per join
4. **Join Completion Evaluation** – Determine when `CompletedSteps + FailedSteps` reaches `ExpectedSteps` and compute overall join status
5. **Continuation Dispatch** – On completion, enqueue follow-up Outbox messages (success or failure path)
6. **Automatic Integration** – Hook into Outbox completion/failure operations so join counters update automatically without handler involvement

### 2.4 Scope

**In Scope:**
- SQL-backed join tracking (`OutboxJoin`, `OutboxJoinMember`) in the same database as the Outbox
- Attaching Outbox messages (by `OutboxMessageIdentifier`) to joins
- Join wait messages and a `JoinWaitHandler` that fans-in to continuation messages
- Integration with Outbox stored procedures (ack/fail) for automatic join updates
- Grouping keys for scoping joins to specific contexts (e.g., customer, tenant, workflow)
- Metadata storage for domain-specific join information

**Out of Scope:**
- Cross-database joins (joins spanning Outbox messages from multiple databases)
- Aggregating or merging payloads from join members
- Workflow modeling beyond simple "all steps done => emit continuation" patterns
- Exactly-once semantics for join continuations (joins inherit Outbox at-least-once behavior)
- Ordered step execution within a join
- Enforcement that all expected steps were actually enqueued

## 3. Non-Goals

This component does **not**:

1. **Enforce ordering of steps within a join**: Steps may complete in any order
2. **Guarantee that all expected steps were actually enqueued**: It only counts steps that are attached to the join
3. **Enforce any particular payload schema**: Join metadata and continuation payloads are opaque strings
4. **Provide cross-tenant or cross-database transactional joins**: Joins are scoped to a single database
5. **Replace higher-level workflow engines**: This is a low-level fan-in primitive, not a comprehensive workflow orchestrator

## 4. Key Concepts and Terms

### 4.1 Core Entities

- **Join**: A coordination record representing a group of related Outbox messages (steps) that must all complete. Persisted in `OutboxJoin`.

- **Join Member**: An association between a join and a specific Outbox message, identified by `OutboxMessageIdentifier`. Persisted in `OutboxJoinMember`.

- **Step**: A single Outbox message participating in a join, represented by an `OutboxMessageIdentifier`.

- **Join Status**: A small integer status for the join:
  - `0` – Pending
  - `1` – Completed
  - `2` – Failed
  - `3` – Cancelled

- **Join Member Status**: A small integer status for the join member:
  - `0` – Pending
  - `1` – Completed
  - `2` – Failed

- **Expected Steps**: The total number of messages that must complete (or fail) for a join to be considered finished.

- **Completed Steps**: Count of messages that have been successfully processed and acknowledged.

- **Failed Steps**: Count of messages that have permanently failed.

### 4.2 Strongly-Typed Identifiers

The Join Coordination component uses strongly-typed identifiers (implemented as `readonly record struct` wrappers) to prevent mixing up different types of IDs and provide type safety:

- **JoinIdentifier**: A unique identifier for a join coordination primitive. Joins use this ID to track groups of related messages and coordinate fan-in operations. Typically wraps a `Guid` value.

- **OutboxMessageIdentifier**: The logical message identifier from the Outbox component that remains constant across retries. This is the identifier used to link joins to Outbox messages.

### 4.3 Grouping and Metadata

- **Grouping Key**: An optional string identifier used to scope joins to a specific context (e.g., customer ID, tenant ID, workflow ID). Joins with the same grouping key are logically related. Null or empty grouping keys mean "unscoped".
  - **Type**: `string?`
  - **Max Length**: 255 characters
  - **Normalization**: Empty strings are treated as null

- **Metadata**: Optional opaque string (typically JSON) attached to the join for domain-specific information.
  - **Type**: `string?`
  - **Storage**: NVARCHAR(MAX) in the database
  - **Format**: No enforced schema; typically JSON describing the join context

### 4.4 Join Wait and Continuations

- **Join Wait Message**: An Outbox message (e.g., topic `join.wait`) that instructs the system to wait for a specific join to complete and then enqueue a continuation message.

- **Join Wait Handler**: A handler (`JoinWaitHandler`) that processes join wait messages, checks join progress, and either:
  - Abandons the wait message if the join is not complete yet (for retry later), or
  - Enqueues the success or failure continuation when the join is complete

- **Continuation Message**: An Outbox message enqueued by the join wait handler when a join completes, allowing the workflow to proceed to the next step.

- **Success Continuation**: The message enqueued when a join completes successfully (specified by `onCompleteTopic` and `onCompletePayload`).

- **Failure Continuation**: The optional message enqueued when a join fails (specified by `onFailTopic` and `onFailPayload`).

## 5. Public API Surface

> **Note**: The operations below may be implemented as extension methods over `IOutbox` or as methods on a dedicated service (e.g., `IJoinCoordinator`). The behavior is the same either way. Names and signatures match existing usage patterns.

### 5.1 Join Operations

#### 5.1.1 StartJoinAsync

Creates a new join with the specified parameters.

```csharp
Task<JoinIdentifier> StartJoinAsync(
    string? groupingKey,
    int expectedSteps,
    string? metadata,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`groupingKey`** (optional): Scoping identifier for the join.
  - **Type**: `string?`
  - **Constraints**:
    - MAY be null
    - Empty string (`""`) MUST be treated as null and normalized to null
    - MUST NOT exceed 255 characters when non-null
  - **Purpose**: Used to scope joins for querying and analysis (e.g., per customer, tenant, or workflow)

- **`expectedSteps`** (required): Number of steps required for the join to finish.
  - **Type**: `int`
  - **Constraints**:
    - MUST be greater than 0
    - If `expectedSteps <= 0`, the method MUST throw `ArgumentOutOfRangeException`
  - **Purpose**: Defines the total number of steps that must complete or fail for the join to be considered finished

- **`metadata`** (optional): Domain-specific information about the join.
  - **Type**: `string?`
  - **Constraints**:
    - MAY be null or empty
    - Stored as NVARCHAR(MAX); no enforced schema
  - **Purpose**: Typically JSON describing the join context for logging, debugging, or business logic

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`
  - **Purpose**: Standard cancellation semantics

**Return Value:**
- **Type**: `Task<JoinIdentifier>`
- **Value**: The identifier of the created join

**Behavior:**
- Creates a new `OutboxJoin` record with `CompletedSteps = 0`, `FailedSteps = 0`, and `Status = Pending`
- Sets `CreatedUtc` and `LastUpdatedUtc` to the current UTC time
- Returns a `JoinIdentifier` corresponding to the persisted `JoinId`

#### 5.1.2 AttachMessageToJoinAsync

Associates an Outbox message with a join, making it a member (step) of that join.

```csharp
Task AttachMessageToJoinAsync(
    JoinIdentifier joinId,
    OutboxMessageIdentifier outboxMessageId,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`joinId`** (required): The join to which the message should be attached.
  - **Type**: `JoinIdentifier`
  - **Constraints**:
    - MUST NOT be the default/empty identifier
    - MUST reference an existing join; if not, the implementation MUST throw `InvalidOperationException`
  - **Purpose**: Identifies the target join

- **`outboxMessageId`** (required): The Outbox message being attached as a step.
  - **Type**: `OutboxMessageIdentifier`
  - **Constraints**:
    - MUST NOT be default/empty
  - **Purpose**: Identifies the Outbox message to attach

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`

**Behavior:**
- Creates an `OutboxJoinMember` row for `(joinId, outboxMessageId)` if it does not already exist
- MUST be idempotent: calling with the same `(joinId, outboxMessageId)` multiple times MUST NOT create duplicates or change counters
- Does NOT modify `CompletedSteps` or `FailedSteps`

#### 5.1.3 ReportStepCompletedAsync

Manually reports that a step has completed successfully.

```csharp
Task ReportStepCompletedAsync(
    JoinIdentifier joinId,
    OutboxMessageIdentifier outboxMessageId,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`joinId`** (required): The join containing the step.
  - **Type**: `JoinIdentifier`
  - **Constraints**: Same as in `AttachMessageToJoinAsync`

- **`outboxMessageId`** (required): The Outbox message being reported as completed.
  - **Type**: `OutboxMessageIdentifier`
  - **Constraints**: Same as in `AttachMessageToJoinAsync`

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`

**Behavior:**
- Marks the `OutboxJoinMember.Status` as Completed for the given `(joinId, outboxMessageId)`
- Increments `CompletedSteps` for the join (first time only)
- MUST be idempotent: repeating the same call MUST NOT double-increment counters

> **Implementation Note**: In typical deployments, this method is **not called directly**. Join state is updated automatically when Outbox messages are acknowledged by the `Outbox_Ack` stored procedure. This method remains available for edge cases and troubleshooting.

#### 5.1.4 ReportStepFailedAsync

Manually reports that a step has failed permanently.

```csharp
Task ReportStepFailedAsync(
    JoinIdentifier joinId,
    OutboxMessageIdentifier outboxMessageId,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`joinId`** (required): The join containing the step.
  - **Type**: `JoinIdentifier`
  - **Constraints**: Same as in `AttachMessageToJoinAsync`

- **`outboxMessageId`** (required): The Outbox message being reported as failed.
  - **Type**: `OutboxMessageIdentifier`
  - **Constraints**: Same as in `AttachMessageToJoinAsync`

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`

**Behavior:**
- Marks the `OutboxJoinMember.Status` as Failed for the given `(joinId, outboxMessageId)`
- Increments `FailedSteps` for the join (first time only)
- MUST be idempotent: repeating the same call MUST NOT double-increment counters

> **Implementation Note**: Like `ReportStepCompletedAsync`, this is typically **not called directly**. Join state is updated automatically when Outbox messages are failed by the `Outbox_Fail` stored procedure.

### 5.2 Join Wait / Continuation Operations

#### 5.2.1 EnqueueJoinWaitAsync

Enqueues a join wait message that will trigger continuation messages when the join completes.

```csharp
Task EnqueueJoinWaitAsync(
    JoinIdentifier joinId,
    bool failIfAnyStepFailed,
    string onCompleteTopic,
    string onCompletePayload,
    string? onFailTopic,
    string? onFailPayload,
    CancellationToken cancellationToken)
```

**Parameters:**

- **`joinId`** (required): The join to wait for.
  - **Type**: `JoinIdentifier`
  - **Constraints**:
    - MUST NOT be default/empty
    - MUST refer to an existing join; otherwise the method MUST throw `InvalidOperationException`

- **`failIfAnyStepFailed`** (required): Determines join failure semantics.
  - **Type**: `bool`
  - **Semantics**:
    - If `true`: The join is considered failed when any step fails (`FailedSteps > 0`)
    - If `false`: The join is considered completed as long as all steps are either completed or failed

- **`onCompleteTopic`** (required): Topic for the success continuation message.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null or empty
    - MUST follow the same constraints as Outbox `topic`:
      - Case-sensitive
      - Length ≤ 255 characters

- **`onCompletePayload`** (required): Payload for the success continuation message.
  - **Type**: `string`
  - **Constraints**:
    - MUST NOT be null
    - MAY be empty
  - **Format**: Treated as opaque string (typically JSON)

- **`onFailTopic`** (optional): Topic for the failure continuation message.
  - **Type**: `string?`
  - **Constraints**:
    - MAY be null if no failure continuation is desired
    - If non-null, MUST be non-empty and respect the same topic constraints
    - If `failIfAnyStepFailed` is true and `onFailTopic` is null, a failed join will not enqueue any continuation

- **`onFailPayload`** (optional): Payload for the failure continuation message.
  - **Type**: `string?`
  - **Constraints**:
    - MAY be null or empty
  - **Format**: Treated as opaque string

- **`cancellationToken`** (required): Cancellation token for the operation.
  - **Type**: `CancellationToken`

**Behavior:**
- Enqueues a "join wait" Outbox message (e.g., topic `join.wait`) that encodes the above parameters
- The join wait message will be processed by `JoinWaitHandler`
- The wait message may be processed multiple times (abandoned and retried) until the join completes

### 5.3 JoinWaitHandler

The handler that processes join wait messages and enqueues continuation messages.

```csharp
public sealed class JoinWaitHandler : IOutboxHandler
{
    public string Topic { get; }  // e.g., "join.wait"
    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}
```

**Topic:**
- MUST be a constant string (e.g., `"join.wait"`)
- MUST conform to the Outbox topic rules

**HandleAsync:**
- `message.Payload` MUST contain the serialized form of the `EnqueueJoinWaitAsync` parameters (including at least `joinId`, `failIfAnyStepFailed`, and continuation info)
- The handler:
  - Reads join state from `OutboxJoin`
  - Decides whether to abandon (join not complete) or process (join complete) the wait message
  - Enqueues continuation messages via Outbox when appropriate

## 6. Behavioral Requirements

This section uses RFC 2119 language (MUST, SHOULD, MAY). Requirement IDs use the `JOIN-###` format.

### 6.1 Join Creation

- **JOIN-001**: `StartJoinAsync` MUST insert a new row into `OutboxJoin` with:
  - `CompletedSteps = 0`
  - `FailedSteps = 0`
  - `Status = Pending` (0)

- **JOIN-002**: `StartJoinAsync` MUST throw `ArgumentOutOfRangeException` if `expectedSteps <= 0`.

- **JOIN-003**: `StartJoinAsync` MUST store the `groupingKey` normalized such that empty strings are persisted as `NULL`.

- **JOIN-004**: If `groupingKey` is non-null and its length exceeds 255 characters, `StartJoinAsync` MUST throw `ArgumentException` or allow the database to reject the insert.

- **JOIN-005**: `StartJoinAsync` MUST set `CreatedUtc` and `LastUpdatedUtc` to the current UTC time.

- **JOIN-006**: `StartJoinAsync` MUST return a `JoinIdentifier` corresponding to the persisted `JoinId` value.

### 6.2 Join Membership

- **JOIN-010**: `AttachMessageToJoinAsync` MUST create an `OutboxJoinMember` row with:
  - `(JoinId, OutboxMessageId) = (joinId, outboxMessageId)`
  - `Status = Pending` (0)
  - `CreatedUtc = current UTC time`
  
  if no such row already exists.

- **JOIN-011**: `AttachMessageToJoinAsync` MUST be idempotent. Calling it multiple times with the same `(joinId, outboxMessageId)` MUST NOT create duplicate rows or increment any join counters.

- **JOIN-012**: `AttachMessageToJoinAsync` MUST throw `InvalidOperationException` if `joinId` does not exist in `OutboxJoin`.

- **JOIN-013**: `AttachMessageToJoinAsync` MUST NOT modify `CompletedSteps` or `FailedSteps`.

- **JOIN-014**: `OutboxJoinMember` MUST NOT introduce any foreign key from `Outbox` back into joins; joins remain discoverable only via `OutboxMessageIdentifier` and the join tables.

### 6.3 Step Completion and Failure

- **JOIN-020**: `ReportStepCompletedAsync` MUST:
  - Mark the `OutboxJoinMember.Status` as Completed (1) for the given `(joinId, outboxMessageId)`
  - Increment `CompletedSteps` for the join if this is the first time the step is being marked as completed

- **JOIN-021**: `ReportStepFailedAsync` MUST:
  - Mark the `OutboxJoinMember.Status` as Failed (2) for the given `(joinId, outboxMessageId)`
  - Increment `FailedSteps` for the join if this is the first time the step is being marked as failed

- **JOIN-022**: `ReportStepCompletedAsync` and `ReportStepFailedAsync` MUST be idempotent. Repeated calls with the same `(joinId, outboxMessageId)` and same status MUST NOT increment counters again.

- **JOIN-023**: For any join, the invariant `CompletedSteps + FailedSteps <= ExpectedSteps` MUST hold at all times.

- **JOIN-024**: All updates to `OutboxJoinMember` and `OutboxJoin` counters for a single call to `ReportStepCompletedAsync` or `ReportStepFailedAsync` MUST occur within a single database transaction.

### 6.4 Integration with Outbox

- **JOIN-030**: When an Outbox message is acknowledged as successfully processed (via `Outbox_Ack`), the Join component MUST treat that as a step completion for all joins that have a member row referencing that message's `OutboxMessageIdentifier`.

- **JOIN-031**: When an Outbox message is permanently failed (via `Outbox_Fail`), the Join component MUST treat that as a step failure for all joins that have a member row referencing that message's `OutboxMessageIdentifier`.

- **JOIN-032**: The updates described in JOIN-030 and JOIN-031 MUST be performed atomically with the Outbox ack/fail operation (within the same transaction) to avoid inconsistent join counters.

- **JOIN-033**: The integration in JOIN-030 and JOIN-031 MUST NOT require any join-specific columns on the Outbox table; it MUST operate solely via `OutboxJoinMember` and `OutboxMessageIdentifier`.

- **JOIN-034**: Manual calls to `ReportStepCompletedAsync` and `ReportStepFailedAsync` MUST produce the same observable result as the automatic updates in JOIN-030 and JOIN-031.

### 6.5 Join Completion and Status

- **JOIN-040**: A join MUST be considered "complete" when `CompletedSteps + FailedSteps == ExpectedSteps`.

- **JOIN-041**: When a join transitions from incomplete to complete:
  - If `FailedSteps > 0`, the join's `Status` MUST be set to Failed (2)
  - If `FailedSteps == 0`, the join's `Status` MUST be set to Completed (1)

- **JOIN-042**: Once a join is in Completed or Failed status, `CompletedSteps`, `FailedSteps`, and `Status` MUST NOT change further.

- **JOIN-043**: The `LastUpdatedUtc` field on `OutboxJoin` MUST be updated each time `CompletedSteps`, `FailedSteps`, or `Status` changes.

### 6.6 Join Wait Handler

- **JOIN-050**: `JoinWaitHandler.Topic` MUST be the configured join wait topic (e.g., `"join.wait"`). All join wait messages MUST use this topic.

- **JOIN-051**: `JoinWaitHandler.HandleAsync` MUST deserialize `message.Payload` into a structure that provides at least:
  - `JoinIdentifier joinId`
  - `bool failIfAnyStepFailed`
  - `string onCompleteTopic`
  - `string onCompletePayload`
  - `string? onFailTopic`
  - `string? onFailPayload`

- **JOIN-052**: If the join referenced by `joinId` does not exist, `JoinWaitHandler` MUST fail the join wait message (not retry indefinitely), typically by marking it as failed via Outbox.

- **JOIN-053**: If the join exists but is not yet complete (`CompletedSteps + FailedSteps < ExpectedSteps`), `JoinWaitHandler` MUST abandon the join wait message so it can be retried later.

- **JOIN-054**: If the join is complete and `failIfAnyStepFailed == true` and `FailedSteps > 0`:
  - If `onFailTopic` is non-null/non-empty, `JoinWaitHandler` MUST enqueue a failure continuation message using `onFailTopic`/`onFailPayload`
  - If `onFailTopic` is null/empty, `JoinWaitHandler` MUST NOT enqueue any continuation

- **JOIN-055**: If the join is complete and either `failIfAnyStepFailed == false` or `FailedSteps == 0`, `JoinWaitHandler` MUST enqueue a success continuation message using `onCompleteTopic`/`onCompletePayload`.

- **JOIN-056**: After enqueueing the appropriate continuation (or none), `JoinWaitHandler` MUST acknowledge the join wait message so it is not processed again.

- **JOIN-057**: `JoinWaitHandler` SHOULD perform all decisions based only on the current state of `OutboxJoin` and not on Outbox message contents.

### 6.7 Concurrency and Consistency

- **JOIN-060**: Multiple workers MAY operate on joins concurrently. The implementation MUST ensure that join counters and statuses remain consistent under concurrent updates.

- **JOIN-061**: All updates to `OutboxJoin` and `OutboxJoinMember` MUST use appropriate locking or optimistic concurrency (e.g., row-level locks or version columns) to prevent lost updates.

- **JOIN-062**: The Join component MUST handle database deadlocks by either retrying the update or propagating the exception so the caller can retry.

### 6.8 Observability

- **JOIN-070**: Join creation (`StartJoinAsync`) SHOULD be logged at INFO level, including `JoinId`, `groupingKey`, and `expectedSteps`.

- **JOIN-071**: Join membership changes (`AttachMessageToJoinAsync`) SHOULD be logged at DEBUG level, including `JoinId` and `OutboxMessageId`.

- **JOIN-072**: Join counters reaching completion (JOIN-040) SHOULD be logged at INFO level, including join status and step counts.

- **JOIN-073**: Join wait handling decisions (abandon vs success vs failure continuation) SHOULD be logged at DEBUG or INFO level.

### 6.9 Grouping and Multi-Join Support

- **JOIN-080**: A single Outbox message MAY participate in multiple joins. The Join component MUST support multiple `OutboxJoinMember` rows for the same `OutboxMessageId` with different `JoinId` values.

- **JOIN-081**: When an Outbox message completes or fails, all joins that reference that message MUST have their counters updated according to JOIN-030 and JOIN-031.

- **JOIN-082**: The `GroupingKey` field SHOULD be indexed to support efficient queries for joins within a specific context.

## 7. Database Schema

The Join component assumes the following SQL schema (names may be configurable; defaults shown).

### 7.1 OutboxJoin Table

```sql
CREATE TABLE [dbo].[OutboxJoin] (
    JoinId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    GroupingKey NVARCHAR(255) NULL,
    ExpectedSteps INT NOT NULL,
    CompletedSteps INT NOT NULL DEFAULT 0,
    FailedSteps INT NOT NULL DEFAULT 0,
    Status TINYINT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Completed, 2=Failed, 3=Cancelled
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastUpdatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Metadata NVARCHAR(MAX) NULL
);

CREATE INDEX IX_OutboxJoin_GroupingKey
    ON [dbo].[OutboxJoin](GroupingKey)
    WHERE GroupingKey IS NOT NULL;
```

**Column Descriptions:**

- **JoinId**: Primary key uniquely identifying the join
- **GroupingKey**: Optional scoping identifier (e.g., customer ID, tenant ID, workflow ID) for filtering and analysis
- **ExpectedSteps**: The total number of steps that must complete or fail for this join to finish
- **CompletedSteps**: Running count of successfully completed steps
- **FailedSteps**: Running count of permanently failed steps
- **Status**: Current state of the join (0=Pending, 1=Completed, 2=Failed, 3=Cancelled)
- **CreatedUtc**: UTC timestamp when the join was created
- **LastUpdatedUtc**: UTC timestamp of the last update to this join (updated when counters or status change)
- **Metadata**: Optional JSON or other metadata describing the join

**Indexes:**

- Primary key on `JoinId` for efficient lookup
- Filtered index on `GroupingKey` (where non-null) for grouping queries

### 7.2 OutboxJoinMember Table

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

CREATE INDEX IX_OutboxJoinMember_OutboxMessageId
    ON [dbo].[OutboxJoinMember](OutboxMessageId);
```

**Column Descriptions:**

- **JoinId**: Foreign key to the `OutboxJoin` table
- **OutboxMessageId**: The logical message identifier (`OutboxMessageIdentifier`) from the Outbox component
- **Status**: Current state of this step (0=Pending, 1=Completed, 2=Failed)
- **CreatedUtc**: UTC timestamp when this membership was created

**Constraints:**

- Composite primary key on `(JoinId, OutboxMessageId)` ensures each message can appear at most once per join
- Foreign key to `OutboxJoin` with CASCADE delete ensures orphaned members are cleaned up
- No foreign key to `Outbox` table (maintains architectural separation)

**Indexes:**

- Primary key on `(JoinId, OutboxMessageId)` for efficient membership lookups
- Index on `OutboxMessageId` to support reverse lookup (finding all joins a message belongs to)

> **Note**: The Outbox table itself is defined in the Outbox Component specification and MUST NOT contain any join-specific columns.

### 7.3 Schema Deployment

- **JOIN-090**: If join schema deployment is enabled, the Join component MUST create `OutboxJoin` and `OutboxJoinMember` tables and their indexes if they do not exist.

- **JOIN-091**: Schema deployment MUST be idempotent; repeated runs MUST NOT fail if the schema already exists.

- **JOIN-092**: Join schema deployment MAY be coupled with Outbox schema deployment or configured separately, but MUST NOT modify the Outbox table structure.

## 8. Configuration and Limits

### 8.1 Configuration Parameters

The Join component should support the following configuration options:

- **Join Wait Topic**: The topic used for join wait messages (default: `"join.wait"`)
- **Schema Deployment**: Enable/disable automatic schema creation (default: true in development, false in production)
- **Table Names**: Allow customization of `OutboxJoin` and `OutboxJoinMember` table names

### 8.2 Limits and Constraints

- **Grouping Key Length**: Maximum 255 characters
- **Expected Steps**: MUST be > 0; recommended practical upper bound depends on workload (very large joins may impact performance)
- **Metadata Size**: Stored as NVARCHAR(MAX); subject to SQL Server limits (approximately 2GB)
- **Number of Joins**: No hard limit; storage and performance are constrained by database size and indexing
- **Messages Per Join**: No hard limit; constrained by `ExpectedSteps` value and database capacity

## 9. Open Questions

### 9.1 Join Store and Multi-Database Scenarios

**Current State**: The current `SqlOutboxJoinStore` implementation is registered as a singleton and connects to a single database. In multi-database Outbox scenarios (e.g., multi-tenant with database-per-tenant), this means:
- Each database has its own join tables
- Joins do not span multiple databases
- The join store connects to only one configured database

**Impact**: Users cannot create joins that span multiple databases. Each database's joins are isolated to that database, determined by the grouping key.

**Open Question**: Should there be a `IJoinStoreProvider` (analogous to `IOutboxStoreProvider`) to support multi-database join coordination, or should joins remain strictly per-database?

**Recommendation for v1.0**: Keep joins per-database for simplicity. Document this limitation clearly. Consider `IJoinStoreProvider` for future versions if cross-database coordination becomes a requirement.

### 9.2 Automatic vs Manual Step Reporting

**Current State**: Join counters are primarily updated automatically by Outbox ack/fail stored procedures. The API also exposes manual reporting methods:
- `ReportStepCompletedAsync`
- `ReportStepFailedAsync`

**Impact**: This dual mechanism may confuse users about when to use manual vs. automatic reporting.

**Open Questions**:
- Are these manual methods required for any planned workflows, or can they be marked as advanced/diagnostic only?
- Should the default guidance explicitly discourage manual calls unless Outbox integration is unavailable?

**Recommendation for v1.0**: 
- Document that automatic reporting is the default and preferred mechanism
- Mark manual methods as "advanced" or "for edge cases only"
- Provide clear guidance that handlers should NOT call manual reporting methods
- Consider deprecating manual methods in future versions if they prove unnecessary

### 9.3 Cross-Database Joins

**Current State**: The Join component intentionally does not support cross-database joins.

**Risk**: Some workflows may try to coordinate steps that span multiple tenant or shard databases.

**Guidance**: Use higher-level workflows, sagas, or compensating actions for cross-database coordination. Each database should have independent joins.

### 9.4 Retention and Cleanup

**Current State**: The specification does not define a retention policy for:
- Completed/failed joins
- Join member records

**Open Question**: Should there be a background cleanup mechanism (e.g., delete joins older than N days) or leave retention policies to the host application?

**Recommendation for v1.0**:
- Leave cleanup to the application layer
- Document that applications should implement periodic cleanup of old joins
- Provide guidance on safe cleanup (only delete joins in terminal states: Completed, Failed, Cancelled)
- Consider adding a built-in cleanup mechanism in future versions

## 10. Usage Examples

### 10.1 Basic Fan-In Pattern

This example shows a typical ETL workflow where multiple extraction tasks must complete before transformation begins.

```csharp
// Start a join for 3 parallel extraction tasks
// The grouping key scopes this join to a specific customer
var joinId = await outbox.StartJoinAsync(
    groupingKey: customerId,
    expectedSteps: 3,
    metadata: """{"type": "etl", "phase": "extract", "customerId": "CUST-123"}""",
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
    onFailTopic: "etl.extract.failed",
    onFailPayload: JsonSerializer.Serialize(new { CustomerId = customerId, Reason = "One or more extractions failed" }),
    cancellationToken: cancellationToken);
```

**Handler Implementation (No Join Logic Required):**

```csharp
// Handlers don't need any join-specific logic - automatic reporting handles it!
public class ExtractCustomersHandler : IOutboxHandler
{
    public string Topic => "extract.customers";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Just do the work - join completion is automatic!
        await ExtractCustomersAsync(cancellationToken);
        
        // No need to call ReportStepCompletedAsync - the Outbox ack automatically updates join counters
    }
}

public class ExtractOrdersHandler : IOutboxHandler
{
    public string Topic => "extract.orders";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await ExtractOrdersAsync(cancellationToken);
        // Join counter automatically updated on ack
    }
}

public class ExtractProductsHandler : IOutboxHandler
{
    public string Topic => "extract.products";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        await ExtractProductsAsync(cancellationToken);
        // Join counter automatically updated on ack
    }
}

// Transformation starts automatically when all extractions complete
public class TransformHandler : IOutboxHandler
{
    public string Topic => "etl.transform";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<TransformRequest>(message.Payload);
        await TransformDataAsync(request.CustomerId, cancellationToken);
    }
}
```

### 10.2 Partial Failure Handling

This example shows how to handle joins where some failures are acceptable.

```csharp
// Start a join that will succeed even if some steps fail
var joinId = await outbox.StartJoinAsync(
    groupingKey: $"report-{reportId}",
    expectedSteps: 5,
    metadata: """{"type": "report-generation", "reportId": "RPT-456"}""",
    cancellationToken);

// Enqueue 5 report section generation tasks
for (int i = 1; i <= 5; i++)
{
    var msgId = await outbox.EnqueueAsync(
        $"report.generate.section{i}",
        JsonSerializer.Serialize(new { ReportId = reportId, SectionId = i }),
        cancellationToken);
    await outbox.AttachMessageToJoinAsync(joinId, msgId, cancellationToken);
}

// Set up fan-in with failIfAnyStepFailed = false
// This means the join completes successfully as long as all steps finish (even if some failed)
await outbox.EnqueueJoinWaitAsync(
    joinId: joinId,
    failIfAnyStepFailed: false,  // Allow partial failures
    onCompleteTopic: "report.assemble",
    onCompletePayload: JsonSerializer.Serialize(new { ReportId = reportId }),
    onFailTopic: null,  // No failure continuation needed
    onFailPayload: null,
    cancellationToken: cancellationToken);

// The report assembler can check which sections completed and handle missing ones
public class ReportAssembler : IOutboxHandler
{
    public string Topic => "report.assemble";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<AssembleRequest>(message.Payload);
        
        // Query which sections actually succeeded
        var availableSections = await GetCompletedSectionsAsync(request.ReportId, cancellationToken);
        
        // Assemble report with available sections, marking missing ones
        await AssembleReportAsync(request.ReportId, availableSections, cancellationToken);
    }
}
```

### 10.3 Multi-Stage Workflow

This example shows how joins can be chained for multi-stage workflows.

```csharp
// Stage 1: Data ingestion (3 sources)
var ingestionJoinId = await outbox.StartJoinAsync(
    groupingKey: workflowId,
    expectedSteps: 3,
    metadata: """{"stage": "ingestion"}""",
    cancellationToken);

var source1Msg = await outbox.EnqueueAsync("ingest.source1", payload1, cancellationToken);
await outbox.AttachMessageToJoinAsync(ingestionJoinId, source1Msg, cancellationToken);

var source2Msg = await outbox.EnqueueAsync("ingest.source2", payload2, cancellationToken);
await outbox.AttachMessageToJoinAsync(ingestionJoinId, source2Msg, cancellationToken);

var source3Msg = await outbox.EnqueueAsync("ingest.source3", payload3, cancellationToken);
await outbox.AttachMessageToJoinAsync(ingestionJoinId, source3Msg, cancellationToken);

// When ingestion completes, start validation stage
await outbox.EnqueueJoinWaitAsync(
    joinId: ingestionJoinId,
    failIfAnyStepFailed: true,
    onCompleteTopic: "workflow.start-validation",
    onCompletePayload: JsonSerializer.Serialize(new { WorkflowId = workflowId }),
    cancellationToken: cancellationToken);

// Validation handler starts a new join for validation tasks
public class StartValidationHandler : IOutboxHandler
{
    public string Topic => "workflow.start-validation";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<StartValidationRequest>(message.Payload);
        
        // Stage 2: Create validation join
        var validationJoinId = await outbox.StartJoinAsync(
            groupingKey: request.WorkflowId,
            expectedSteps: 2,
            metadata: """{"stage": "validation"}""",
            cancellationToken);
        
        var val1Msg = await outbox.EnqueueAsync("validate.schema", /*...*/, cancellationToken);
        await outbox.AttachMessageToJoinAsync(validationJoinId, val1Msg, cancellationToken);
        
        var val2Msg = await outbox.EnqueueAsync("validate.business-rules", /*...*/, cancellationToken);
        await outbox.AttachMessageToJoinAsync(validationJoinId, val2Msg, cancellationToken);
        
        // When validation completes, publish the data
        await outbox.EnqueueJoinWaitAsync(
            joinId: validationJoinId,
            failIfAnyStepFailed: true,
            onCompleteTopic: "workflow.publish",
            onCompletePayload: JsonSerializer.Serialize(new { WorkflowId = request.WorkflowId }),
            cancellationToken: cancellationToken);
    }
}
```

---

**End of Join Coordination Component Specification**
