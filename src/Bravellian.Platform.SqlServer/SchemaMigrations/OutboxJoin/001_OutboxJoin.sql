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

IF OBJECT_ID(N'[$SchemaName$].OutboxJoin', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'JoinId') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD JoinId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'PayeWaiveTenantId') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD PayeWaiveTenantId BIGINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'ExpectedSteps') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD ExpectedSteps INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'CompletedSteps') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD CompletedSteps INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'FailedSteps') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD FailedSteps INT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'Status') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD Status TINYINT NOT NULL DEFAULT 0;
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'CreatedUtc') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'LastUpdatedUtc') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD LastUpdatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].OutboxJoin', 'Metadata') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoin ADD Metadata NVARCHAR(MAX) NULL;
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

IF OBJECT_ID(N'[$SchemaName$].OutboxJoinMember', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('[$SchemaName$].OutboxJoinMember', 'JoinId') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoinMember ADD JoinId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].OutboxJoinMember', 'OutboxMessageId') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoinMember ADD OutboxMessageId UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID();
    IF COL_LENGTH('[$SchemaName$].OutboxJoinMember', 'CreatedUtc') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoinMember ADD CreatedUtc DATETIMEOFFSET(3) NOT NULL DEFAULT SYSDATETIMEOFFSET();
    IF COL_LENGTH('[$SchemaName$].OutboxJoinMember', 'CompletedAt') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoinMember ADD CompletedAt DATETIMEOFFSET(3) NULL;
    IF COL_LENGTH('[$SchemaName$].OutboxJoinMember', 'FailedAt') IS NULL
        ALTER TABLE [$SchemaName$].OutboxJoinMember ADD FailedAt DATETIMEOFFSET(3) NULL;
END
GO
