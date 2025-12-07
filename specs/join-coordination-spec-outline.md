# Join Coordination Component - Specification Outline

This document outlines the content that should be included in the separate Join Coordination specification, as extracted from the original Outbox specification.

---

## Document Header

```markdown
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
```

---

## 2. Purpose and Architecture

### 2.1 Purpose

The Join Coordination component provides fan-in coordination for workflow orchestration, enabling systems to wait for the completion of multiple related messages before proceeding to the next step.

### 2.2 Architecture and Integration

Join coordination is built **on top of** the Outbox component:

- Joins use `OutboxMessageIdentifier` to track message relationships
- Join tables (`OutboxJoin`, `OutboxJoinMember`) are separate from the `Outbox` table
- Outbox messages do not contain join identifiers; the association is maintained in the `OutboxJoinMember` table
- The Join component integrates with Outbox by hooking into the same stored procedures that mark messages as completed or failed
- Join counters are automatically updated when Outbox messages are acknowledged or failed
- The Outbox component itself has no knowledge of joins and remains join-agnostic

---

## 3. Key Concepts

### 3.1 Strongly-Typed Identifiers

- **JoinIdentifier**: A unique identifier for a join coordination primitive. Joins use this ID to track groups of related messages and coordinate fan-in operations.

### 3.2 Join/Fan-In Concepts

- **Join**: A coordination primitive that tracks completion of multiple related messages
- **Join Member**: An association between a join and a specific outbox message
- **Expected Steps**: The total number of messages that must complete for a join to finish
- **Completed Steps**: Count of messages that have been successfully processed
- **Failed Steps**: Count of messages that have permanently failed
- **Join Status**: Current state of the join (Pending, Completed, Failed, Cancelled)
- **Grouping Key**: An optional string identifier used to scope joins to a specific context (e.g., customer ID, tenant ID, workflow ID). Joins with the same grouping key are logically related.

---

## 4. Public API Surface

### 4.1 Join Operations (typically exposed via IOutbox extension or separate interface)

```csharp
Task<JoinIdentifier> StartJoinAsync(
    string? groupingKey,
    int expectedSteps,
    string? metadata,
    CancellationToken cancellationToken)

Task AttachMessageToJoinAsync(
    JoinIdentifier joinId,
    OutboxMessageIdentifier outboxMessageId,
    CancellationToken cancellationToken)

Task ReportStepCompletedAsync(
    JoinIdentifier joinId,
    OutboxMessageIdentifier outboxMessageId,
    CancellationToken cancellationToken)

Task ReportStepFailedAsync(
    JoinIdentifier joinId,
    OutboxMessageIdentifier outboxMessageId,
    CancellationToken cancellationToken)
```

---

## 5. Behavioral Requirements

### 5.1 Integration with Outbox Message Lifecycle

**JOIN-001**: When an Outbox message is acknowledged via `Outbox_Ack`, if that message is part of any joins, the stored procedure MUST increment the `CompletedSteps` counter for each associated join.

**JOIN-002**: The increment of join counters in the ack operation MUST occur atomically with marking the message as processed.

**JOIN-003**: When an Outbox message is failed via `Outbox_Fail`, if that message is part of any joins, the stored procedure MUST increment the `FailedSteps` counter for each associated join.

**JOIN-004**: The increment of join counters in the fail operation MUST occur atomically with marking the message as failed.

### 5.2 Join Lifecycle

**JOIN-005**: `StartJoinAsync` MUST create a new join record with the specified `groupingKey`, `expectedSteps`, and optional `metadata`.

**JOIN-006**: `StartJoinAsync` MUST return a unique `JoinIdentifier` for the created join.

**JOIN-007**: `StartJoinAsync` MUST initialize the join with `CompletedSteps` = 0 and `FailedSteps` = 0.

**JOIN-008**: `AttachMessageToJoinAsync` MUST create a join member record associating the specified message with the specified join.

**JOIN-009**: `AttachMessageToJoinAsync` MUST be idempotent; calling it multiple times with the same parameters MUST have no additional effect.

**JOIN-010**: `ReportStepCompletedAsync` MUST increment the `CompletedSteps` counter for the specified join.

**JOIN-011**: `ReportStepCompletedAsync` MUST be idempotent when called with the same `outboxMessageId`.

**JOIN-012**: `ReportStepFailedAsync` MUST increment the `FailedSteps` counter for the specified join.

**JOIN-013**: `ReportStepFailedAsync` MUST be idempotent when called with the same `outboxMessageId`.

**JOIN-014**: Join counters (`CompletedSteps` and `FailedSteps`) SHOULD be automatically updated by the database when messages are acknowledged or failed, eliminating the need for explicit calls to `ReportStepCompletedAsync` or `ReportStepFailedAsync` in most cases.

### 5.3 Join Completion Handling

**JOIN-015**: The `JoinWaitHandler` MUST check if a join is complete by verifying that `CompletedSteps + FailedSteps = ExpectedSteps`.

**JOIN-016**: If the join is not complete, the `JoinWaitHandler` MUST abandon the `join.wait` message for retry later.

**JOIN-017**: If the join is complete and `FailIfAnyStepFailed` is true, the join MUST be marked as failed if `FailedSteps > 0`.

**JOIN-018**: If the join is complete and succeeds, the `JoinWaitHandler` MUST enqueue the message specified by `OnCompleteTopic` and `OnCompletePayload`.

**JOIN-019**: If the join is complete and fails, the `JoinWaitHandler` MUST enqueue the message specified by `OnFailTopic` and `OnFailPayload`.

### 5.4 Grouping and Multi-Join Support

**JOIN-020**: Joins MAY be scoped to a logical grouping using the optional `groupingKey` parameter. Joins with the same grouping key are logically related and can be used to isolate coordination within specific contexts (e.g., per customer, per tenant, per workflow).

**JOIN-021**: A single message MAY participate in multiple joins.

---

## 6. Database Schema

### 6.1 OutboxJoin Table

```sql
CREATE TABLE [dbo].[OutboxJoin] (
    JoinId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    GroupingKey NVARCHAR(255) NULL,  -- Optional scoping identifier (e.g., customer ID, tenant ID, workflow ID)
    ExpectedSteps INT NOT NULL,
    CompletedSteps INT NOT NULL DEFAULT 0,
    FailedSteps INT NOT NULL DEFAULT 0,
    Status TINYINT NOT NULL DEFAULT 0,  -- 0=Pending, 1=Completed, 2=Failed, 3=Cancelled
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastUpdatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Metadata NVARCHAR(MAX) NULL
);

CREATE INDEX IX_OutboxJoin_GroupingKey ON [dbo].[OutboxJoin](GroupingKey) WHERE GroupingKey IS NOT NULL;
```

### 6.2 OutboxJoinMember Table

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

---

## 7. Open Questions

### 7.1 Join Store Singleton Limitation

**Observation**: The current `SqlOutboxJoinStore` implementation is registered as a singleton and connects to a single database. In multi-database scenarios, joins only work within the configured database. This is inconsistent with the multi-database support for the main Outbox.

**Impact**: Users cannot create joins that span multiple databases. Each database's joins are isolated to that database, determined by the grouping key.

**Recommendation**: Consider implementing an `IOutboxJoinStoreProvider` pattern similar to `IOutboxStoreProvider` to support joins across multiple databases in future versions.

### 7.2 Automatic vs. Manual Join Reporting

**Observation**: The documentation states that join completion is reported automatically by database stored procedures (`Outbox_Ack` and `Outbox_Fail`), but the API still exposes `ReportStepCompletedAsync` and `ReportStepFailedAsync` methods.

**Impact**: This dual mechanism may confuse users about when to use manual vs. automatic reporting.

**Recommendation**: Clarify in documentation that manual methods are provided for backward compatibility and edge cases only. Most users should rely on automatic reporting.

---

## 8. Usage Example

### 8.1 Basic Fan-In Pattern

```csharp
// Start a join for 3 parallel extraction tasks
// The grouping key scopes this join to a specific customer
var joinId = await outbox.StartJoinAsync(
    groupingKey: customerId,
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

**End of Join Coordination Specification Outline**
