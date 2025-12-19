# Inbox Due Time Feature Implementation

## Overview
This document summarizes the changes made to support delayed message processing in the Inbox pattern through the addition of an optional `DueTimeUtc` parameter.

## Feature Description
Messages can now be enqueued with an optional due time, which ensures they will not be processed until at least that specified time. This enables scenarios such as:
- Delayed notifications or reminders
- Scheduled task execution
- Rate limiting and backoff strategies
- Time-based message routing

## Changes Made

### 1. Database Schema Changes

#### Table Schema (`DatabaseSchemaManager.cs` - GetInboxCreateScript)
Added new nullable column to the Inbox table:
- **`DueTimeUtc DATETIME2(3) NULL`** - Optional timestamp indicating when the message should become eligible for processing

#### Migration Script (`DatabaseSchemaManager.cs` - GetInboxWorkQueueMigrationInlineScript)
Added migration logic to add the column to existing Inbox tables:
```sql
IF COL_LENGTH('[{schemaName}].[Inbox]', 'DueTimeUtc') IS NULL
    ALTER TABLE [{schemaName}].[Inbox] ADD DueTimeUtc DATETIME2(3) NULL;
```

### 2. Stored Procedure Changes

#### Inbox_Claim Procedure (`DatabaseSchemaManager.cs`)
Updated the WHERE clause to filter out messages that haven't reached their due time:
```sql
WHERE Status IN ('Seen', 'Processing') 
    AND (LockedUntil IS NULL OR LockedUntil <= @now)
    AND (DueTimeUtc IS NULL OR DueTimeUtc <= @now)  -- NEW
```

This ensures that:
- Messages with no due time (`DueTimeUtc IS NULL`) are processed immediately
- Messages with a due time are only claimed when `DueTimeUtc <= current time`

### 3. C# Code Changes

#### InboxMessage Class (`InboxMessage.cs`)
Added property to expose the due time:
```csharp
public DateTimeOffset? DueTimeUtc { get; internal init; }
```

#### IInbox Interface (`IInbox.cs`)
Updated `EnqueueAsync` method signature to accept the new parameter:
```csharp
Task EnqueueAsync(
    string topic,
    string source,
    string messageId,
    string payload,
    byte[]? hash = null,
    DateTimeOffset? dueTimeUtc = null,  // NEW
    CancellationToken cancellationToken = default);
```

#### SqlInboxService (`SqlInboxService.cs`)
1. Updated the MERGE SQL statement to include `DueTimeUtc`:
   - Added to the source SELECT
   - Added to the WHEN MATCHED UPDATE clause
   - Added to the WHEN NOT MATCHED INSERT clause

2. Updated `EnqueueAsync` implementation:
   - Added `dueTimeUtc` parameter
   - Pass `dueTimeUtc?.UtcDateTime` to the SQL query
   - Updated logging to include due time information

#### SqlInboxWorkStore (`SqlInboxWorkStore.cs`)
1. Updated `GetAsync` method to SELECT the `DueTimeUtc` column
2. Updated the mapping to populate `DueTimeUtc` in the returned `InboxMessage`

### 4. Documentation Updates

#### API Reference (`docs/inbox-api-reference.md`)
- Updated `EnqueueAsync` method signature documentation
- Added `dueTimeUtc` parameter description
- Added example showing delayed processing use case

## Usage Examples

### Immediate Processing (Default Behavior)
```csharp
await _inbox.EnqueueAsync(
    topic: "payment.received",
    source: "StripeWebhook",
    messageId: evt.Id,
    payload: JsonSerializer.Serialize(evt),
    hash: hash);
```

### Delayed Processing
```csharp
// Process after 10 minutes
await _inbox.EnqueueAsync(
    topic: "order.reminder",
    source: "OrderService",
    messageId: orderId,
    payload: JsonSerializer.Serialize(order),
    dueTimeUtc: DateTimeOffset.UtcNow.AddMinutes(10));

// Process at specific time
await _inbox.EnqueueAsync(
    topic: "scheduled.report",
    source: "ReportingService",
    messageId: reportId,
    payload: JsonSerializer.Serialize(report),
    dueTimeUtc: new DateTimeOffset(2025, 11, 20, 9, 0, 0, TimeSpan.Zero));
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

1. `src/Bravellian.Platform/DatabaseSchemaManager.cs` - Schema and stored procedure updates
2. `src/Bravellian.Platform/Inbox/IInbox.cs` - Interface signature update
3. `src/Bravellian.Platform/Inbox/InboxMessage.cs` - Added DueTimeUtc property
4. `src/Bravellian.Platform/Inbox/SqlInboxService.cs` - Implementation updates
5. `src/Bravellian.Platform/Inbox/SqlInboxWorkStore.cs` - Data access updates
6. `docs/inbox-api-reference.md` - Documentation updates

## Future Enhancements

Potential future improvements:
- Add index on `DueTimeUtc` for better query performance if needed
- Add metrics/observability for delayed message processing
- Add admin APIs to view/manage scheduled messages
