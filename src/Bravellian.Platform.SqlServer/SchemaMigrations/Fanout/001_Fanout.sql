IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
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
