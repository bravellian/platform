IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
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
