IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$EmailOutboxTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$EmailOutboxTable$] (
        EmailOutboxId UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        ProviderName NVARCHAR(200) NOT NULL,
        MessageKey NVARCHAR(450) NOT NULL,
        Payload NVARCHAR(MAX) NOT NULL,
        EnqueuedAtUtc DATETIMEOFFSET(3) NOT NULL,
        DueTimeUtc DATETIMEOFFSET(3) NULL,
        AttemptCount INT NOT NULL DEFAULT 0,
        Status TINYINT NOT NULL DEFAULT 0,
        FailureReason NVARCHAR(1024) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'UX_$EmailOutboxTable$_Provider_MessageKey'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$EmailOutboxTable$]', N'U'))
BEGIN
    CREATE UNIQUE INDEX UX_$EmailOutboxTable$_Provider_MessageKey
        ON [$SchemaName$].[$EmailOutboxTable$](ProviderName, MessageKey);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_$EmailOutboxTable$_Pending'
      AND object_id = OBJECT_ID(N'[$SchemaName$].[$EmailOutboxTable$]', N'U'))
BEGIN
    CREATE INDEX IX_$EmailOutboxTable$_Pending
        ON [$SchemaName$].[$EmailOutboxTable$](Status, DueTimeUtc, EnqueuedAtUtc)
        INCLUDE (EmailOutboxId, ProviderName, MessageKey, AttemptCount)
        WHERE Status = 0;
END
GO
