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
