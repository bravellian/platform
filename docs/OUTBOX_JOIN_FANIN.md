# Outbox Join / Fan-In Support

This document describes the join/fan-in functionality added to the Bravellian Platform outbox framework.

## Overview

The join/fan-in feature enables coordination of multiple outbox messages, allowing you to execute a follow-up action only after all related messages have completed (or failed) according to defined rules.

This is useful for scenarios like:
- **ETL workflows**: Fire N parallel data extraction jobs, then start transformation only when all extractions complete
- **Multi-step processing**: Coordinate multiple independent operations before proceeding to the next phase
- **Aggregation tasks**: Collect results from multiple workers before generating a summary

## Core Concepts

### Join (OutboxJoin)

A **join** represents a group of related outbox messages. It tracks:

- `JoinId`: Unique identifier for the join
- `PayeWaiveTenantId`: Tenant scoping
- `ExpectedSteps`: Total number of steps expected to complete
- `CompletedSteps`: Count of steps that completed successfully
- `FailedSteps`: Count of steps that failed
- `Status`: Current state (Pending, Completed, Failed, Cancelled)
- `Metadata`: Optional JSON metadata for join configuration

### Join Member (OutboxJoinMember)

A **join member** represents the association between a join and an outbox message. This many-to-many relationship allows:
- One join to track multiple messages
- One message to participate in multiple joins

## Usage

### 1. Starting a Join

```csharp
// Create a join expecting 3 steps
var joinId = await outbox.StartJoinAsync(
    tenantId: 12345,
    expectedSteps: 3,
    metadata: """{"workflow": "customer-import"}""",
    cancellationToken);
```

### 2. Attaching Messages to a Join

```csharp
// Enqueue messages and attach them to the join
var messageId1 = await outbox.EnqueueAsync("extract.customers", payload1, cancellationToken);
await outbox.AttachMessageToJoinAsync(joinId, messageId1, cancellationToken);

var messageId2 = await outbox.EnqueueAsync("extract.orders", payload2, cancellationToken);
await outbox.AttachMessageToJoinAsync(joinId, messageId2, cancellationToken);

var messageId3 = await outbox.EnqueueAsync("extract.products", payload3, cancellationToken);
await outbox.AttachMessageToJoinAsync(joinId, messageId3, cancellationToken);
```

### 3. Reporting Step Completion

In your message handlers, report when steps complete or fail:

```csharp
public class ExtractCustomersHandler : IOutboxHandler
{
    private readonly IOutbox outbox;
    
    public string Topic => "extract.customers";
    
    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // Parse payload to get joinId
            var payload = JsonSerializer.Deserialize<ExtractPayload>(message.Payload);
            
            // Do the work
            await ExtractCustomersAsync(cancellationToken);
            
            // Report success
            await outbox.ReportStepCompletedAsync(
                payload.JoinId,
                message.Id,
                cancellationToken);
        }
        catch (Exception)
        {
            // Report failure
            await outbox.ReportStepFailedAsync(
                payload.JoinId,
                message.Id,
                cancellationToken);
            throw;
        }
    }
}
```

### 4. Setting up Fan-In Orchestration

Use the `EnqueueJoinWaitAsync` extension method to orchestrate the fan-in:

```csharp
// Simple approach using extension method
await outbox.EnqueueJoinWaitAsync(
    joinId: joinId,
    failIfAnyStepFailed: true,
    onCompleteTopic: "transform.start",
    onCompletePayload: """{"transformId": "customer-import-123"}""",
    onFailTopic: "notify.failure",
    onFailPayload: """{"reason": "Some extractions failed"}""",
    cancellationToken: cancellationToken);
```

Alternatively, you can manually create the payload if needed:

```csharp
var waitPayload = new JoinWaitPayload
{
    JoinId = joinId,
    FailIfAnyStepFailed = true,
    OnCompleteTopic = "transform.start",
    OnCompletePayload = """{"transformId": "customer-import-123"}""",
    OnFailTopic = "notify.failure",
    OnFailPayload = """{"reason": "Some extractions failed"}"""
};

await outbox.EnqueueAsync(
    "join.wait",
    JsonSerializer.Serialize(waitPayload),
    cancellationToken);
```

The `JoinWaitHandler` will:
1. Check if all steps are finished (CompletedSteps + FailedSteps = ExpectedSteps)
2. If not, abandon the message for retry later
3. If yes, determine if the join succeeded or failed
4. Update join status
5. Enqueue the appropriate follow-up message

## Configuration Options

### JoinWaitPayload

- `JoinId`: The join to wait for
- `FailIfAnyStepFailed`: If true (default), join fails if any step failed. If false, join completes successfully even with failures.
- `OnCompleteTopic` / `OnCompletePayload`: Message to enqueue when join completes successfully
- `OnFailTopic` / `OnFailPayload`: Message to enqueue when join fails

## Database Schema

The join functionality uses two tables:

### OutboxJoin

```sql
CREATE TABLE [dbo].[OutboxJoin] (
    JoinId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    PayeWaiveTenantId BIGINT NOT NULL,
    ExpectedSteps INT NOT NULL,
    CompletedSteps INT NOT NULL DEFAULT 0,
    FailedSteps INT NOT NULL DEFAULT 0,
    Status TINYINT NOT NULL DEFAULT 0,
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    LastUpdatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    Metadata NVARCHAR(MAX) NULL
);
```

### OutboxJoinMember

```sql
CREATE TABLE [dbo].[OutboxJoinMember] (
    JoinId UNIQUEIDENTIFIER NOT NULL,
    OutboxMessageId UNIQUEIDENTIFIER NOT NULL,
    CreatedUtc DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_OutboxJoinMember PRIMARY KEY (JoinId, OutboxMessageId),
    CONSTRAINT FK_OutboxJoinMember_Join FOREIGN KEY (JoinId) 
        REFERENCES [dbo].[OutboxJoin](JoinId) ON DELETE CASCADE
);
```

## Registration

The join functionality is automatically registered when using the platform services:

```csharp
services.AddPlatformMultiDatabaseWithList(databases, enableSchemaDeployment: true);
```

This registers:
- `IOutboxJoinStore` implementation
- `JoinWaitHandler` for processing `join.wait` messages
- Schema deployment for join tables (when `enableSchemaDeployment` is true)

## Best Practices

1. **Always set ExpectedSteps correctly**: The join will not complete until CompletedSteps + FailedSteps equals ExpectedSteps
2. **Make step completion reporting idempotent**: Handlers should be safe to retry
3. **Use metadata for debugging**: Store workflow information in join metadata for easier troubleshooting
4. **Monitor join status**: Query OutboxJoin table to track long-running joins
5. **Set appropriate retry delays**: The `join.wait` message will be retried according to standard outbox backoff settings

## Limitations

1. **No cross-tenant joins**: Joins are scoped to a single PayeWaive tenant
2. **No timeout mechanism**: Joins don't automatically fail after a timeout (implement separately if needed)
3. **Application-level idempotency**: The current implementation relies on application logic to prevent double-counting completed steps

## Example: ETL Workflow

```csharp
// 1. Start join for 3 extraction tasks
var joinId = await outbox.StartJoinAsync(
    tenantId: customerId,
    expectedSteps: 3,
    metadata: """{"type": "etl", "phase": "extract"}""",
    cancellationToken);

// 2. Enqueue extraction messages
var extractPayload = new { JoinId = joinId, CustomerId = customerId };
await outbox.EnqueueAsync("extract.customers", JsonSerializer.Serialize(extractPayload), cancellationToken);
await outbox.EnqueueAsync("extract.orders", JsonSerializer.Serialize(extractPayload), cancellationToken);
await outbox.EnqueueAsync("extract.products", JsonSerializer.Serialize(extractPayload), cancellationToken);

// 3. Set up fan-in to start transformation when all extractions complete
await outbox.EnqueueJoinWaitAsync(
    joinId: joinId,
    failIfAnyStepFailed: true,
    onCompleteTopic: "etl.transform",
    onCompletePayload: JsonSerializer.Serialize(new { CustomerId = customerId }),
    cancellationToken: cancellationToken);
```

The handlers for `extract.*` topics would call `ReportStepCompletedAsync` or `ReportStepFailedAsync` as appropriate, and the `JoinWaitHandler` would automatically trigger the transformation once all extractions complete.
