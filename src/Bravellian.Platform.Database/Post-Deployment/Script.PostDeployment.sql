-- =============================================
-- Post-Deployment Script
-- =============================================
--
-- This script runs after database deployment.
-- Use for:
-- - Reference data initialization
-- - Default configuration
-- - Schema version verification
-- =============================================

PRINT 'Running post-deployment tasks...'
GO

-- Verify all required tables exist
DECLARE @missingTables TABLE ([TableName] NVARCHAR(128));

INSERT INTO @missingTables
SELECT [TableName] FROM (VALUES
    ('Outbox'),
    ('OutboxState'),
    ('Inbox'),
    ('Jobs'),
    ('JobRuns'),
    ('Timers'),
    ('SchedulerState'),
    ('Lease'),
    ('DistributedLock'),
    ('FanoutPolicy'),
    ('FanoutCursor'),
    ('Semaphore'),
    ('SemaphoreLease')
) AS Tables([TableName])
WHERE NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'dbo' 
    AND TABLE_NAME = Tables.[TableName]
);

IF EXISTS (SELECT 1 FROM @missingTables)
BEGIN
    PRINT 'ERROR: Missing required tables!'
    SELECT [TableName] FROM @missingTables;
    RAISERROR('Schema deployment incomplete. See missing tables above.', 16, 1);
END
ELSE
BEGIN
    PRINT 'All required tables present.'
END
GO

-- Initialize OutboxState if not exists
IF NOT EXISTS (SELECT 1 FROM [dbo].[OutboxState] WHERE [Id] = 1)
BEGIN
    INSERT INTO [dbo].[OutboxState] ([Id], [CurrentFencingToken], [LastDispatchAt])
    VALUES (1, 0, NULL);
    PRINT 'OutboxState initialized.'
END
GO

-- Initialize SchedulerState if not exists
IF NOT EXISTS (SELECT 1 FROM [dbo].[SchedulerState] WHERE [Id] = 1)
BEGIN
    INSERT INTO [dbo].[SchedulerState] ([Id], [CurrentFencingToken], [LastRunAt])
    VALUES (1, 0, NULL);
    PRINT 'SchedulerState initialized.'
END
GO

PRINT 'Post-deployment complete!'
GO
