IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = '$SchemaName$')
BEGIN
    EXEC('CREATE SCHEMA [$SchemaName$]');
END
GO

IF OBJECT_ID(N'[$SchemaName$].OutboxJoin', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].OutboxJoin (
        JoinId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
        PayeWaiveTenantId BIGINT NOT NULL,
        ExpectedSteps INT NOT NULL,
        CompletedSteps INT NOT NULL DEFAULT 0,
        FailedSteps INT NOT NULL DEFAULT 0,
        Status TINYINT NOT NULL DEFAULT 0,
        CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        LastUpdatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        Metadata NVARCHAR(MAX) NULL
    );

    CREATE INDEX IX_OutboxJoin_TenantStatus ON [$SchemaName$].OutboxJoin(PayeWaiveTenantId, Status);
END
GO

IF OBJECT_ID(N'[$SchemaName$].OutboxJoinMember', N'U') IS NULL
BEGIN
    CREATE TABLE [$SchemaName$].OutboxJoinMember (
        JoinId UNIQUEIDENTIFIER NOT NULL,
        OutboxMessageId UNIQUEIDENTIFIER NOT NULL,
        CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        CompletedAt DATETIMEOFFSET(3) NULL,
        FailedAt DATETIMEOFFSET(3) NULL,
        CONSTRAINT PK_OutboxJoinMember PRIMARY KEY (JoinId, OutboxMessageId),
        CONSTRAINT FK_OutboxJoinMember_Join FOREIGN KEY (JoinId)
            REFERENCES [$SchemaName$].OutboxJoin(JoinId) ON DELETE CASCADE,
        CONSTRAINT FK_OutboxJoinMember_Outbox FOREIGN KEY (OutboxMessageId)
            REFERENCES [$SchemaName$].Outbox(Id) ON DELETE CASCADE
    );

    CREATE INDEX IX_OutboxJoinMember_MessageId ON [$SchemaName$].OutboxJoinMember(OutboxMessageId);
END
GO
