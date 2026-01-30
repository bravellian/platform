IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF OBJECT_ID(N'[$SchemaName$].Semaphore', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].Semaphore (
        [Name] NVARCHAR(200) NOT NULL CONSTRAINT PK_Semaphore PRIMARY KEY,
        [Limit] INT NOT NULL,
        [NextFencingCounter] BIGINT NOT NULL DEFAULT 1,
        [UpdatedUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET()
    );
END
GO

IF OBJECT_ID(N'[$SchemaName$].Semaphore', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].Semaphore', 'Name') IS NULL
        ALTER TABLE [$SchemaName$].Semaphore ADD [Name] NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].Semaphore', 'Limit') IS NULL
        ALTER TABLE [$SchemaName$].Semaphore ADD [Limit] INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].Semaphore', 'NextFencingCounter') IS NULL
        ALTER TABLE [$SchemaName$].Semaphore ADD [NextFencingCounter] BIGINT NOT NULL DEFAULT 1;
    IF COL_LENGTH('[$SchemaName$].Semaphore', 'UpdatedUtc') IS NULL
        ALTER TABLE [$SchemaName$].Semaphore ADD [UpdatedUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
END
GO

IF OBJECT_ID(N'[$SchemaName$].SemaphoreLease', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].SemaphoreLease (
        [Name] NVARCHAR(200) NOT NULL,
        [Token] UNIQUEIDENTIFIER NOT NULL,
        [Fencing] BIGINT NOT NULL,
        [OwnerId] NVARCHAR(200) NOT NULL,
        [LeaseUntilUtc] DATETIMEOFFSET(3) NOT NULL,
        [CreatedUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        [RenewedUtc] DATETIMEOFFSET(3) NULL,
        [ClientRequestId] NVARCHAR(100) NULL,
        CONSTRAINT PK_SemaphoreLease PRIMARY KEY ([Name], [Token])
    );
END
GO

IF OBJECT_ID(N'[$SchemaName$].SemaphoreLease', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'Name') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [Name] NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'Token') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [Token] UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'Fencing') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [Fencing] BIGINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'OwnerId') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [OwnerId] NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'LeaseUntilUtc') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [LeaseUntilUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'CreatedUtc') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [CreatedUtc] DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'RenewedUtc') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [RenewedUtc] DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].SemaphoreLease', 'ClientRequestId') IS NULL
        ALTER TABLE [$SchemaName$].SemaphoreLease ADD [ClientRequestId] NVARCHAR(100) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SemaphoreLease_Name_LeaseUntilUtc'
      AND object_id = OBJECT_ID(N'[$SchemaName$].SemaphoreLease', N'U'))
BEGIN
    CREATE INDEX IX_SemaphoreLease_Name_LeaseUntilUtc
        ON [$SchemaName$].SemaphoreLease([Name], [LeaseUntilUtc])
        INCLUDE([Token]);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SemaphoreLease_LeaseUntilUtc'
      AND object_id = OBJECT_ID(N'[$SchemaName$].SemaphoreLease', N'U'))
BEGIN
    CREATE INDEX IX_SemaphoreLease_LeaseUntilUtc
        ON [$SchemaName$].SemaphoreLease([LeaseUntilUtc]);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'IX_SemaphoreLease_ClientRequestId'
      AND object_id = OBJECT_ID(N'[$SchemaName$].SemaphoreLease', N'U'))
BEGIN
    CREATE INDEX IX_SemaphoreLease_ClientRequestId
        ON [$SchemaName$].SemaphoreLease([ClientRequestId])
        WHERE [ClientRequestId] IS NOT NULL;
END
GO
