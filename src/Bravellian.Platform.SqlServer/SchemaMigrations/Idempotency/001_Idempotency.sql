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
