IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$LockTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$LockTable$](
        [ResourceName] SYSNAME NOT NULL CONSTRAINT PK_$LockTable$ PRIMARY KEY,
        [OwnerToken] UNIQUEIDENTIFIER NULL,
        [LeaseUntil] DATETIMEOFFSET(3) NULL,
        [FencingToken] BIGINT NOT NULL CONSTRAINT DF_$LockTable$_Fence DEFAULT(0),
        [ContextJson] NVARCHAR(MAX) NULL,
        [Version] ROWVERSION NOT NULL
    );

    CREATE INDEX IX_$LockTable$_OwnerToken ON [$SchemaName$].[$LockTable$]([OwnerToken])
        WHERE [OwnerToken] IS NOT NULL;
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$LockTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$LockTable$]', 'ResourceName') IS NULL
        ALTER TABLE [$SchemaName$].[$LockTable$] ADD [ResourceName] SYSNAME NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$LockTable$]', 'OwnerToken') IS NULL
        ALTER TABLE [$SchemaName$].[$LockTable$] ADD [OwnerToken] UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$LockTable$]', 'LeaseUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$LockTable$] ADD [LeaseUntil] DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$LockTable$]', 'FencingToken') IS NULL
        ALTER TABLE [$SchemaName$].[$LockTable$] ADD [FencingToken] BIGINT NOT NULL DEFAULT(0);
    IF COL_LENGTH('[$SchemaName$].[$LockTable$]', 'ContextJson') IS NULL
        ALTER TABLE [$SchemaName$].[$LockTable$] ADD [ContextJson] NVARCHAR(MAX) NULL;
    IF COL_LENGTH('[$SchemaName$].[$LockTable$]', 'Version') IS NULL
        ALTER TABLE [$SchemaName$].[$LockTable$] ADD [Version] ROWVERSION;
END
GO
