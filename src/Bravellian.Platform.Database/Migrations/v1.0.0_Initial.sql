-- =============================================
-- Platform Schema v1.0.0 - Initial Release
-- =============================================
--
-- This script creates the initial database schema
-- for the Bravellian Platform.
--
-- Components:
-- - Outbox (Transactional Outbox Pattern)
-- - Inbox (Idempotent Message Processing)
-- - Scheduler (Jobs, JobRuns, Timers)
-- - Lease System
-- - Distributed Lock
-- - Fanout (Multi-shard processing)
-- - Semaphore (Rate limiting)
--
-- Dependencies: None (fresh database)
-- Target: SQL Server 2019+
-- =============================================

PRINT 'Platform Schema v1.0.0 - Initial Release'
PRINT 'Starting installation...'
GO

-- NOTE: Individual object creation is handled by
-- the SQL Project build process. This migration
-- script is for reference and manual deployment.

-- All tables, procedures, and types are defined
-- in separate .sql files under Schema/ folder.

PRINT 'Installation complete!'
PRINT ''
PRINT 'Schema version 1.0.0 installed successfully.'
PRINT 'Run schema validation to verify installation.'
GO

-- Record schema version
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES 
               WHERE TABLE_SCHEMA = 'dbo' 
               AND TABLE_NAME = 'SchemaVersion')
BEGIN
    CREATE TABLE [dbo].[SchemaVersion] (
        [Component] NVARCHAR(50) NOT NULL,
        [Version] NVARCHAR(20) NOT NULL,
        [AppliedAt] DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        [AppliedBy] NVARCHAR(100) NOT NULL DEFAULT SYSTEM_USER,
        CONSTRAINT [PK_SchemaVersion] PRIMARY KEY ([Component])
    );
END
GO

-- Insert version markers
MERGE [dbo].[SchemaVersion] AS target
USING (VALUES 
    ('Platform', '1.0.0'),
    ('Outbox', '1.0.0'),
    ('Inbox', '1.0.0'),
    ('Scheduler', '1.0.0'),
    ('Lease', '1.0.0'),
    ('Lock', '1.0.0'),
    ('Fanout', '1.0.0'),
    ('Semaphore', '1.0.0')
) AS source ([Component], [Version])
ON target.[Component] = source.[Component]
WHEN MATCHED THEN
    UPDATE SET [Version] = source.[Version],
               [AppliedAt] = SYSUTCDATETIME()
WHEN NOT MATCHED THEN
    INSERT ([Component], [Version])
    VALUES (source.[Component], source.[Version]);
GO

PRINT 'Version tracking initialized.'
GO
