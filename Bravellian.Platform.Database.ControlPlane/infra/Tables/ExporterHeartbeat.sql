CREATE TABLE [infra].[ExporterHeartbeat] (
    [InstanceId]   NVARCHAR (100)     NOT NULL,
    [LastFlushUtc] DATETIMEOFFSET (3) NOT NULL,
    [LastError]    NVARCHAR (512)     NULL,
    PRIMARY KEY CLUSTERED ([InstanceId] ASC)
);

