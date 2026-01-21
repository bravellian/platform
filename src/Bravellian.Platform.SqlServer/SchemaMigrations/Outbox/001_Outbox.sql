IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF TYPE_ID(N'[$SchemaName$].GuidIdList') IS NULL
BEGIN
    CREATE TYPE [$SchemaName$].GuidIdList AS TABLE (Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY);
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$OutboxTable$] (
        -- Core Fields
        Id UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        Payload NVARCHAR(MAX) NOT NULL,
        Topic NVARCHAR(255) NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

        -- Processing Status & Auditing
        IsProcessed BIT NOT NULL DEFAULT 0,
        ProcessedAt DATETIMEOFFSET NULL,
        ProcessedBy NVARCHAR(100) NULL,

        -- For Robustness & Error Handling
        RetryCount INT NOT NULL DEFAULT 0,
        LastError NVARCHAR(MAX) NULL,

        -- For Idempotency & Tracing
        MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        CorrelationId NVARCHAR(255) NULL,

        -- For Delayed Processing
        DueTimeUtc DATETIMEOFFSET(3) NULL,

        -- Work Queue Pattern Columns
        Status TINYINT NOT NULL DEFAULT 0,
        LockedUntil DATETIMEOFFSET(3) NULL,
        OwnerToken UNIQUEIDENTIFIER NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_$OutboxTable$_WorkQueue'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$OutboxTable$_WorkQueue ON [$SchemaName$].[$OutboxTable$](Status, CreatedAt)
        INCLUDE(Id, LockedUntil, DueTimeUtc)
        WHERE Status = 0;
END
GO

IF OBJECT_ID(N'[$SchemaName$].OutboxState', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].OutboxState (
        Id INT NOT NULL CONSTRAINT PK_OutboxState PRIMARY KEY,
        CurrentFencingToken BIGINT NOT NULL DEFAULT(0),
        LastDispatchAt DATETIMEOFFSET(3) NULL
    );

    INSERT [$SchemaName$].OutboxState (Id, CurrentFencingToken, LastDispatchAt)
    VALUES (1, 0, NULL);
END
GO
