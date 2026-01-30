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

IF OBJECT_ID(N'[$SchemaName$].[$LeaseTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$LeaseTable$]', 'Name') IS NULL
        ALTER TABLE [$SchemaName$].[$LeaseTable$] ADD [Name] SYSNAME NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$LeaseTable$]', 'Owner') IS NULL
        ALTER TABLE [$SchemaName$].[$LeaseTable$] ADD [Owner] SYSNAME NULL;
    IF COL_LENGTH('[$SchemaName$].[$LeaseTable$]', 'LeaseUntilUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$LeaseTable$] ADD [LeaseUntilUtc] DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$LeaseTable$]', 'LastGrantedUtc') IS NULL
        ALTER TABLE [$SchemaName$].[$LeaseTable$] ADD [LastGrantedUtc] DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$LeaseTable$]', 'Version') IS NULL
        ALTER TABLE [$SchemaName$].[$LeaseTable$] ADD [Version] ROWVERSION;
END
GO
