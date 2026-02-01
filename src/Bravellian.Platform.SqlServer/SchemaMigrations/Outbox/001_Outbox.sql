IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE LOWER(name) = LOWER('$SchemaName$'))
BEGIN
    BEGIN TRY
        DECLARE @schemaSql NVARCHAR(4000) = N'CREATE SCHEMA ' + QUOTENAME('$SchemaName$');
        EXEC sp_executesql @schemaSql;
    END TRY
    BEGIN CATCH
        IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE LOWER(name) = LOWER('$SchemaName$'))
        BEGIN
            THROW;
        END
    END CATCH
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.types t
    JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE t.is_table_type = 1
      AND LOWER(s.name) = LOWER('$SchemaName$')
      AND LOWER(t.name) = LOWER('GuidIdList'))
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

IF OBJECT_ID(N'[$SchemaName$].[$OutboxTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'Payload') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD Payload NVARCHAR(MAX) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'Topic') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD Topic NVARCHAR(255) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'CreatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'IsProcessed') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD IsProcessed BIT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'ProcessedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD ProcessedAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'ProcessedBy') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD ProcessedBy NVARCHAR(100) NULL;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'RetryCount') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD RetryCount INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'LastError') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD LastError NVARCHAR(MAX) NULL;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'MessageId') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD MessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'CorrelationId') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD CorrelationId NVARCHAR(255) NULL;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'DueTimeUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD DueTimeUtc DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD Status TINYINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'LockedUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD LockedUntil DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$OutboxTable$]', 'OwnerToken') IS NULL
        ALTER TABLE [$SchemaName$].[$OutboxTable$] ADD OwnerToken UNIQUEIDENTIFIER NULL;
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

IF OBJECT_ID(N'[$SchemaName$].OutboxState', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].OutboxState', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].OutboxState ADD Id INT NOT NULL DEFAULT 1;
    IF COL_LENGTH('[$SchemaName$].OutboxState', 'CurrentFencingToken') IS NULL
        ALTER TABLE [$SchemaName$].OutboxState ADD CurrentFencingToken BIGINT NOT NULL DEFAULT(0);
    IF COL_LENGTH('[$SchemaName$].OutboxState', 'LastDispatchAt') IS NULL
        ALTER TABLE [$SchemaName$].OutboxState ADD LastDispatchAt DATETIMEOFFSET(3) NULL;
END
GO
