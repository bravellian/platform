# Outbox Due Time Feature Implementation

## Overview
This document summarizes the changes made to support delayed message processing in the Outbox pattern through the addition of an optional `DueTimeUtc` parameter.

## Feature Description
Messages can now be enqueued with an optional due time, which ensures they will not be processed until at least that specified time. This enables scenarios such as:
- Delayed notifications or reminders
- Scheduled task execution
- Rate limiting and backoff strategies
- Time-based message routing

## Changes Made

### 1. Database Schema Changes

#### Table Schema (`DatabaseSchemaManager.cs` - GetOutboxCreateScript)
Added new nullable column to the Outbox table:
- **`DueTimeUtc DATETIME2(3) NULL`** - Optional timestamp indicating when the message should become eligible for processing

#### Migration Script (`DatabaseSchemaManager.cs` - GetWorkQueueMigrationInlineScript)
Added migration logic to add the column to existing Outbox tables:
```sql
IF COL_LENGTH('[{schemaName}].[Outbox]', 'DueTimeUtc') IS NULL
    ALTER TABLE [{schemaName}].[Outbox] ADD DueTimeUtc DATETIME2(3) NULL;
```

### 2. Stored Procedure Changes

#### Outbox_Claim Procedure (`DatabaseSchemaManager.cs` - CreateOutboxProceduresAsync)
Updated the WHERE clause to filter out messages that haven't reached their due time:
```sql
WHERE Status = 0 
    AND (LockedUntil IS NULL OR LockedUntil <= @now)
    AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)  -- NEW
```

This ensures that:
- Messages with no due time (`DueTimeUtc IS NULL`) are processed immediately
- Messages with a due time are only claimed when `DueTimeUtc <= current time`

### 3. C# Code Changes

#### OutboxMessage Class (`OutboxMessage.cs`)
Added property to expose the due time:
```csharp
public DateTimeOffset? DueTimeUtc { get; internal init; }
```

#### IOutbox Interface (`IOutbox.cs`)
Updated `EnqueueAsync` method signatures to accept the new parameter:
```csharp
Task EnqueueAsync(
    string topic,
    string payload,
    string? correlationId,
    DateTimeOffset? dueTimeUtc = null);  // NEW

Task EnqueueAsync(
    string topic,
    string payload,
    IDbTransaction transaction,
    string? correlationId = null,
    DateTimeOffset? dueTimeUtc = null);  // NEW
```

#### SqlOutboxService (`SqlOutboxService.cs`)
1. Updated the INSERT SQL statement to include `DueTimeUtc`:
   - Added to the column list in INSERT statement
   - Added to the VALUES clause

2. Updated `EnqueueAsync` implementations:
   - Added `dueTimeUtc` parameter to both overloads
   - Pass `dueTimeUtc?.UtcDateTime` to the SQL query

#### SqlOutboxStore (`SqlOutboxStore.cs`)
Updated `ClaimDueAsync` method to filter by `DueTimeUtc`:
```sql
WHERE IsProcessed = 0 
  AND NextAttemptAt <= SYSDATETIMEOFFSET()
  AND (DueTimeUtc IS NULL OR DueTimeUtc <= SYSUTCDATETIME())  -- NEW
```

## Usage Examples

### Immediate Processing (Default Behavior)
```csharp
await _outbox.EnqueueAsync(
    topic: "payment.processed",
    payload: JsonSerializer.Serialize(payment),
    correlationId: correlationId);
```

### Delayed Processing
```csharp
// Process after 10 minutes
await _outbox.EnqueueAsync(
    topic: "order.reminder",
    payload: JsonSerializer.Serialize(order),
    correlationId: orderId,
    dueTimeUtc: DateTimeOffset.UtcNow.AddMinutes(10));

// Process at specific time
await _outbox.EnqueueAsync(
    topic: "scheduled.notification",
    payload: JsonSerializer.Serialize(notification),
    correlationId: notificationId,
    dueTimeUtc: new DateTimeOffset(2025, 11, 20, 9, 0, 0, TimeSpan.Zero));

// Using with a transaction
using var transaction = connection.BeginTransaction();
await _outbox.EnqueueAsync(
    topic: "data.export",
    payload: JsonSerializer.Serialize(exportRequest),
    transaction: transaction,
    correlationId: exportId,
    dueTimeUtc: DateTimeOffset.UtcNow.AddHours(1));
await transaction.CommitAsync();
```

## Backward Compatibility

All changes are **fully backward compatible**:
- The `DueTimeUtc` column is nullable
- The `dueTimeUtc` parameter is optional (defaults to `null`)
- Existing code without the parameter will continue to work as before
- Messages without a due time (`NULL`) are processed immediately
- The migration script safely adds the column to existing tables

## Testing Notes

The solution builds successfully with all changes. Integration tests require Docker for SQL Server, which was not available in the test environment. However:
- All code compiles without errors or warnings related to these changes
- The SQL logic follows the same pattern as the existing `LockedUntil` filtering
- Type safety is maintained throughout with nullable types

## Files Modified

1. `src/Bravellian.Platform/DatabaseSchemaManager.cs` - Schema, migration, and stored procedure updates
2. `src/Bravellian.Platform/Outbox/IOutbox.cs` - Interface signature updates
3. `src/Bravellian.Platform/Outbox/OutboxMessage.cs` - Added DueTimeUtc property
4. `src/Bravellian.Platform/Outbox/SqlOutboxService.cs` - Implementation updates
5. `src/Bravellian.Platform/Outbox/SqlOutboxStore.cs` - Data access updates

## Consistency with Inbox Pattern

This implementation follows the exact same pattern used for the Inbox due time feature:
- Same nullable column type (`DATETIME2(3) NULL`)
- Same filtering logic in claim procedures
- Same parameter naming and positioning
- Same backward compatibility approach

## Future Enhancements

Potential future improvements:
- Add index on `DueTimeUtc` for better query performance if needed
- Add metrics/observability for delayed message processing
- Add admin APIs to view/manage scheduled messages
- Add monitoring for messages with due times that are far in the future
