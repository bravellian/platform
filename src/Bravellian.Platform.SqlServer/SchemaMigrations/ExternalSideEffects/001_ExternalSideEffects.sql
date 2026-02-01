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

IF OBJECT_ID(N'[$SchemaName$].[$ExternalSideEffectTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$ExternalSideEffectTable$] (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
        OperationName NVARCHAR(200) NOT NULL,
        IdempotencyKey NVARCHAR(200) NOT NULL,
        Status TINYINT NOT NULL DEFAULT 0,
        AttemptCount INT NOT NULL DEFAULT 0,
        CreatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        LastUpdatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        LastAttemptAt DATETIMEOFFSET(3) NULL,
        LastExternalCheckAt DATETIMEOFFSET(3) NULL,
        LockedUntil DATETIMEOFFSET(3) NULL,
        LockedBy UNIQUEIDENTIFIER NULL,
        CorrelationId NVARCHAR(255) NULL,
        OutboxMessageId UNIQUEIDENTIFIER NULL,
        ExternalReferenceId NVARCHAR(255) NULL,
        ExternalStatus NVARCHAR(100) NULL,
        LastError NVARCHAR(MAX) NULL,
        PayloadHash NVARCHAR(128) NULL
    );
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$ExternalSideEffectTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'Id') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD Id UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'OperationName') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD OperationName NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'IdempotencyKey') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD IdempotencyKey NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD Status TINYINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'AttemptCount') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD AttemptCount INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'CreatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD CreatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'LastUpdatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD LastUpdatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'LastAttemptAt') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD LastAttemptAt DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'LastExternalCheckAt') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD LastExternalCheckAt DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'LockedUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD LockedUntil DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'LockedBy') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD LockedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'CorrelationId') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD CorrelationId NVARCHAR(255) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'OutboxMessageId') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD OutboxMessageId UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'ExternalReferenceId') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD ExternalReferenceId NVARCHAR(255) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'ExternalStatus') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD ExternalStatus NVARCHAR(100) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'LastError') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD LastError NVARCHAR(MAX) NULL;
    IF COL_LENGTH('[$SchemaName$].[$ExternalSideEffectTable$]', 'PayloadHash') IS NULL
        ALTER TABLE [$SchemaName$].[$ExternalSideEffectTable$] ADD PayloadHash NVARCHAR(128) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UQ_$ExternalSideEffectTable$_OperationKey'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$ExternalSideEffectTable$]', N'U'))
BEGIN
    CREATE UNIQUE INDEX UQ_$ExternalSideEffectTable$_OperationKey
        ON [$SchemaName$].[$ExternalSideEffectTable$] (OperationName, IdempotencyKey);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_$ExternalSideEffectTable$_Status'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$ExternalSideEffectTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$ExternalSideEffectTable$_Status
        ON [$SchemaName$].[$ExternalSideEffectTable$] (Status, LastUpdatedAt)
        INCLUDE (OperationName, IdempotencyKey, LockedUntil);
END
GO
