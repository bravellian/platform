IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    JOIN sys.schemas s ON t.schema_id = s.schema_id
    WHERE t.name = '$IdempotencyTable$'
      AND s.name = '$SchemaName$')
BEGIN
    CREATE TABLE [$SchemaName$].[$IdempotencyTable$] (
        IdempotencyKey NVARCHAR(200) NOT NULL PRIMARY KEY,
        Status TINYINT NOT NULL,
        LockedUntil DATETIMEOFFSET(3) NULL,
        LockedBy UNIQUEIDENTIFIER NULL,
        FailureCount INT NOT NULL DEFAULT 0,
        CreatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        CompletedAt DATETIMEOFFSET(3) NULL
    );
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$IdempotencyTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'IdempotencyKey') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD IdempotencyKey NVARCHAR(200) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD Status TINYINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'LockedUntil') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD LockedUntil DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'LockedBy') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD LockedBy UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'FailureCount') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD FailureCount INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'CreatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD CreatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSUTCDATETIME();
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'UpdatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD UpdatedAt DATETIMEOFFSET(3) NOT NULL DEFAULT SYSUTCDATETIME();
    IF COL_LENGTH('[$SchemaName$].[$IdempotencyTable$]', 'CompletedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$IdempotencyTable$] ADD CompletedAt DATETIMEOFFSET(3) NULL;
END
GO
