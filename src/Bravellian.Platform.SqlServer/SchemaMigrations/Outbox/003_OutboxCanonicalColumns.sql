IF OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U') IS NULL
BEGIN
    RETURN;
END
GO

IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'CreatedOn') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD CreatedOn DATETIMEOFFSET(3) NULL;
END

IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'ProcessedOn') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD ProcessedOn DATETIMEOFFSET(3) NULL;
END

IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'AttemptCount') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD AttemptCount INT NULL;
END

IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'DueOn') IS NULL
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD DueOn DATETIMEOFFSET(3) NULL;
END
GO

UPDATE [$SchemaName$].[$OutboxTable$]
SET CreatedOn = COALESCE(CreatedOn, CreatedAt)
WHERE CreatedOn IS NULL;

UPDATE [$SchemaName$].[$OutboxTable$]
SET ProcessedOn = COALESCE(ProcessedOn, ProcessedAt)
WHERE ProcessedOn IS NULL AND ProcessedAt IS NOT NULL;

UPDATE [$SchemaName$].[$OutboxTable$]
SET AttemptCount = COALESCE(AttemptCount, RetryCount)
WHERE AttemptCount IS NULL;

UPDATE [$SchemaName$].[$OutboxTable$]
SET DueOn = COALESCE(DueOn, DueTimeUtc)
WHERE DueOn IS NULL AND DueTimeUtc IS NOT NULL;
GO

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U')
      AND name = 'CreatedOn'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$]
        ALTER COLUMN CreatedOn DATETIMEOFFSET(3) NOT NULL;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U')
      AND c.name = 'CreatedOn')
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$]
        ADD CONSTRAINT DF_$OutboxTable$_CreatedOn DEFAULT SYSUTCDATETIME() FOR CreatedOn;
END

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U')
      AND name = 'AttemptCount'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$]
        ALTER COLUMN AttemptCount INT NOT NULL;
END

IF NOT EXISTS (
    SELECT 1
    FROM sys.default_constraints dc
    INNER JOIN sys.columns c
        ON c.object_id = dc.parent_object_id
        AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U')
      AND c.name = 'AttemptCount')
BEGIN
    ALTER TABLE [$SchemaName$].[$OutboxTable$]
        ADD CONSTRAINT DF_$OutboxTable$_AttemptCount DEFAULT 0 FOR AttemptCount;
END
GO
