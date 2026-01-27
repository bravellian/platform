IF NOT EXISTS (
    SELECT 1
    FROM sys.tables t
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(AuditAnchorsTable)'
)
BEGIN
    CREATE TABLE [$(SchemaName)].[$(AuditAnchorsTable)]
    (
        AuditEventId NVARCHAR(64) NOT NULL,
        AnchorType NVARCHAR(100) NOT NULL,
        AnchorId NVARCHAR(128) NOT NULL,
        Role NVARCHAR(64) NOT NULL,
        CONSTRAINT PK_AuditAnchors PRIMARY KEY (AuditEventId, AnchorType, AnchorId, Role),
        CONSTRAINT FK_AuditAnchors_AuditEvents
            FOREIGN KEY (AuditEventId)
            REFERENCES [$(SchemaName)].[$(AuditEventsTable)] (AuditEventId)
            ON DELETE CASCADE
    );
END
GO

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes i
    INNER JOIN sys.tables t ON t.object_id = i.object_id
    INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
    WHERE s.name = '$(SchemaName)' AND t.name = '$(AuditAnchorsTable)' AND i.name = 'IX_AuditAnchors_Type_Id'
)
BEGIN
    CREATE INDEX IX_AuditAnchors_Type_Id
        ON [$(SchemaName)].[$(AuditAnchorsTable)] (AnchorType, AnchorId)
        INCLUDE (AuditEventId, Role);
END
GO