IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$(SchemaName)')
BEGIN
    EXEC('CREATE SCHEMA [$(SchemaName)]')
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(AuditEventsTable)'
)
BEGIN
    CREATE TABLE [$(SchemaName)].[$(AuditEventsTable)]
    (
        AuditEventId NVARCHAR(64) NOT NULL PRIMARY KEY,
        OccurredAtUtc DATETIMEOFFSET(3) NOT NULL,
        Name NVARCHAR(200) NOT NULL,
        DisplayMessage NVARCHAR(1024) NOT NULL,
        Outcome TINYINT NOT NULL,
        DataJson NVARCHAR(MAX) NULL,
        ActorType NVARCHAR(100) NULL,
        ActorId NVARCHAR(128) NULL,
        ActorDisplay NVARCHAR(256) NULL,
        CorrelationId NVARCHAR(64) NULL,
        CausationId NVARCHAR(64) NULL,
        TraceId NVARCHAR(128) NULL,
        SpanId NVARCHAR(128) NULL,
        CorrelationCreatedAtUtc DATETIMEOFFSET(3) NULL,
        CorrelationTagsJson NVARCHAR(MAX) NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(AuditEventsTable)' AND i.name = 'IX_AuditEvents_OccurredAtUtc'
)
BEGIN
    CREATE INDEX IX_AuditEvents_OccurredAtUtc
        ON [$(SchemaName)].[$(AuditEventsTable)] (OccurredAtUtc DESC);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(AuditEventsTable)' AND i.name = 'IX_AuditEvents_Name_OccurredAtUtc'
)
BEGIN
    CREATE INDEX IX_AuditEvents_Name_OccurredAtUtc
        ON [$(SchemaName)].[$(AuditEventsTable)] (Name, OccurredAtUtc DESC);
END
GO