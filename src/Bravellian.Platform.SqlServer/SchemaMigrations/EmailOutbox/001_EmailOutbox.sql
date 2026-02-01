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

IF OBJECT_ID(N'[$SchemaName$].[$EmailOutboxTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'EmailOutboxId') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD EmailOutboxId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'ProviderName') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD ProviderName NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'MessageKey') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD MessageKey NVARCHAR(450) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'Payload') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD Payload NVARCHAR(MAX) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'EnqueuedAtUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD EnqueuedAtUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'DueTimeUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD DueTimeUtc DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'AttemptCount') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD AttemptCount INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD Status TINYINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$EmailOutboxTable$]', 'FailureReason') IS NULL
        ALTER TABLE [$SchemaName$].[$EmailOutboxTable$] ADD FailureReason NVARCHAR(1024) NULL;
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
