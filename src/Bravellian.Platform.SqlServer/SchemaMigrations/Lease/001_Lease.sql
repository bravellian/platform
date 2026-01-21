IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$LeaseTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$LeaseTable$](
        [Name] SYSNAME NOT NULL CONSTRAINT PK_$LeaseTable$ PRIMARY KEY,
        [Owner] SYSNAME NULL,
        [LeaseUntilUtc] DATETIMEOFFSET(3) NULL,
        [LastGrantedUtc] DATETIMEOFFSET(3) NULL,
        [Version] ROWVERSION NOT NULL
    );
END
GO
