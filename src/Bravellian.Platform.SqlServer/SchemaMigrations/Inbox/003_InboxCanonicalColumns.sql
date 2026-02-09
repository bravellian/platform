IF OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U') IS NULL
BEGIN
    RETURN;
END
GO

IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'CreatedOn') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$] ADD CreatedOn DATETIMEOFFSET(3) NULL;
END

IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'ProcessedOn') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$] ADD ProcessedOn DATETIMEOFFSET(3) NULL;
END

IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'AttemptCount') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$] ADD AttemptCount INT NULL;
END

IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'DueOn') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$] ADD DueOn DATETIMEOFFSET(3) NULL;
END

IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'CorrelationId') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$] ADD CorrelationId NVARCHAR(255) NULL;
END

IF COL_LENGTH('[$SchemaName$].[$InboxTable$]', 'ProcessedBy') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$] ADD ProcessedBy NVARCHAR(100) NULL;
END
GO

UPDATE [$SchemaName$].[$InboxTable$]
SET CreatedOn = COALESCE(CreatedOn, FirstSeenUtc)
WHERE CreatedOn IS NULL;

UPDATE [$SchemaName$].[$InboxTable$]
SET ProcessedOn = COALESCE(ProcessedOn, ProcessedUtc)
WHERE ProcessedOn IS NULL AND ProcessedUtc IS NOT NULL;

UPDATE [$SchemaName$].[$InboxTable$]
SET AttemptCount = COALESCE(AttemptCount, Attempts)
WHERE AttemptCount IS NULL;

UPDATE [$SchemaName$].[$InboxTable$]
SET DueOn = COALESCE(DueOn, DueTimeUtc)
WHERE DueOn IS NULL AND DueTimeUtc IS NOT NULL;
GO

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U')
      AND name = 'CreatedOn'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$]
        ALTER COLUMN CreatedOn DATETIMEOFFSET(3) NOT NULL;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U')
      AND c.name = 'CreatedOn')
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$]
        ADD CONSTRAINT DF_$InboxTable$_CreatedOn DEFAULT SYSUTCDATETIME() FOR CreatedOn;
END

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U')
      AND name = 'AttemptCount'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$]
        ALTER COLUMN AttemptCount INT NOT NULL;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[$SchemaName$].[$InboxTable$]', N'U')
      AND c.name = 'AttemptCount')
BEGIN
    ALTER TABLE [$SchemaName$].[$InboxTable$]
        ADD CONSTRAINT DF_$InboxTable$_AttemptCount DEFAULT 0 FOR AttemptCount;
END
GO
