IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$(SchemaName)')
BEGIN
    EXEC('CREATE SCHEMA [$(SchemaName)]')
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(OperationsTable)'
)
BEGIN
    CREATE TABLE [$(SchemaName)].[$(OperationsTable)]
    (
        OperationId NVARCHAR(64) NOT NULL PRIMARY KEY,
        Name NVARCHAR(200) NOT NULL,
        Status TINYINT NOT NULL,
        StartedAtUtc DATETIMEOFFSET(3) NOT NULL,
        UpdatedAtUtc DATETIMEOFFSET(3) NOT NULL,
        CompletedAtUtc DATETIMEOFFSET(3) NULL,
        PercentComplete DECIMAL(5,2) NULL,
        Message NVARCHAR(512) NULL,
        CorrelationId NVARCHAR(64) NULL,
        CausationId NVARCHAR(64) NULL,
        TraceId NVARCHAR(128) NULL,
        SpanId NVARCHAR(128) NULL,
        CorrelationCreatedAtUtc DATETIMEOFFSET(3) NULL,
        CorrelationTagsJson NVARCHAR(MAX) NULL,
        ParentOperationId NVARCHAR(64) NULL,
        TagsJson NVARCHAR(MAX) NULL,
        RowVersion ROWVERSION NOT NULL
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(OperationsTable)' AND i.name = 'IX_Operations_Status_UpdatedAtUtc'
)
BEGIN
    CREATE INDEX IX_Operations_Status_UpdatedAtUtc
        ON [$(SchemaName)].[$(OperationsTable)] (Status, UpdatedAtUtc)
        INCLUDE (OperationId, CompletedAtUtc);
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(OperationsTable)' AND i.name = 'IX_Operations_ParentOperationId'
)
BEGIN
    CREATE INDEX IX_Operations_ParentOperationId
        ON [$(SchemaName)].[$(OperationsTable)] (ParentOperationId);
END
GO