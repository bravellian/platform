IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(OperationEventsTable)'
)
BEGIN
    CREATE TABLE [$(SchemaName)].[$(OperationEventsTable)]
    (
        EventId BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OperationId NVARCHAR(64) NOT NULL,
        OccurredAtUtc DATETIMEOFFSET(3) NOT NULL,
        Kind NVARCHAR(64) NOT NULL,
        Message NVARCHAR(1024) NOT NULL,
        DataJson NVARCHAR(MAX) NULL,
        CONSTRAINT FK_OperationEvents_Operations
            FOREIGN KEY (OperationId)
            REFERENCES [$(SchemaName)].[$(OperationsTable)] (OperationId)
            ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(OperationEventsTable)' AND i.name = 'IX_OperationEvents_OperationId_OccurredAtUtc'
)
BEGIN
    CREATE INDEX IX_OperationEvents_OperationId_OccurredAtUtc
        ON [$(SchemaName)].[$(OperationEventsTable)] (OperationId, OccurredAtUtc);
END
GO