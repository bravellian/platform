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

IF OBJECT_ID(N'[$SchemaName$].[$PolicyTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$PolicyTable$] (
        FanoutTopic NVARCHAR(100) NOT NULL,
        WorkKey NVARCHAR(100) NOT NULL,
        DefaultEverySeconds INT NOT NULL,
        JitterSeconds INT NOT NULL DEFAULT 60,
        CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_$PolicyTable$ PRIMARY KEY (FanoutTopic, WorkKey)
    );

    CREATE INDEX IX_$PolicyTable$_FanoutTopic ON [$SchemaName$].[$PolicyTable$](FanoutTopic);
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$PolicyTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$PolicyTable$]', 'FanoutTopic') IS NULL
        ALTER TABLE [$SchemaName$].[$PolicyTable$] ADD FanoutTopic NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$PolicyTable$]', 'WorkKey') IS NULL
        ALTER TABLE [$SchemaName$].[$PolicyTable$] ADD WorkKey NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$PolicyTable$]', 'DefaultEverySeconds') IS NULL
        ALTER TABLE [$SchemaName$].[$PolicyTable$] ADD DefaultEverySeconds INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].[$PolicyTable$]', 'JitterSeconds') IS NULL
        ALTER TABLE [$SchemaName$].[$PolicyTable$] ADD JitterSeconds INT NOT NULL DEFAULT 60;
    IF COL_LENGTH('[$SchemaName$].[$PolicyTable$]', 'CreatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$PolicyTable$] ADD CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$PolicyTable$]', 'UpdatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$PolicyTable$] ADD UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$CursorTable$]', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].[$CursorTable$] (
        FanoutTopic NVARCHAR(100) NOT NULL,
        WorkKey NVARCHAR(100) NOT NULL,
        ShardKey NVARCHAR(100) NOT NULL,
        LastCompletedAt DATETIMEOFFSET NULL,
        LastAttemptAt DATETIMEOFFSET NULL,
        LastAttemptStatus NVARCHAR(20) NULL,
        NextAttemptAt DATETIMEOFFSET NULL,
        CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT PK_$CursorTable$ PRIMARY KEY (FanoutTopic, WorkKey, ShardKey)
    );

    CREATE INDEX IX_$CursorTable$_TopicWork ON [$SchemaName$].[$CursorTable$](FanoutTopic, WorkKey);
    CREATE INDEX IX_$CursorTable$_LastCompleted ON [$SchemaName$].[$CursorTable$](LastCompletedAt)
        WHERE LastCompletedAt IS NOT NULL;
END
GO

IF OBJECT_ID(N'[$SchemaName$].[$CursorTable$]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'FanoutTopic') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD FanoutTopic NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'WorkKey') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD WorkKey NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'ShardKey') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD ShardKey NVARCHAR(100) NOT NULL DEFAULT N'';
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'LastCompletedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD LastCompletedAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'LastAttemptAt') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD LastAttemptAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'LastAttemptStatus') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD LastAttemptStatus NVARCHAR(20) NULL;
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'NextAttemptAt') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD NextAttemptAt DATETIMEOFFSET NULL;
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'CreatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].[$CursorTable$]', 'UpdatedAt') IS NULL
        ALTER TABLE [$SchemaName$].[$CursorTable$] ADD UpdatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET();
END
GO
