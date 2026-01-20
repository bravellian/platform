CREATE TABLE [infra].[SemaphoreLease] (
    [Name]            NVARCHAR (200)     NOT NULL,
    [Token]           UNIQUEIDENTIFIER   NOT NULL,
    [Fencing]         BIGINT             NOT NULL,
    [OwnerId]         NVARCHAR (200)     NOT NULL,
    [LeaseUntilUtc]   DATETIMEOFFSET (3) NOT NULL,
    [CreatedUtc]      DATETIMEOFFSET (3) DEFAULT (sysdatetimeoffset()) NOT NULL,
    [RenewedUtc]      DATETIMEOFFSET (3) NULL,
    [ClientRequestId] NVARCHAR (100)     NULL,
    CONSTRAINT [PK_SemaphoreLease] PRIMARY KEY CLUSTERED ([Name] ASC, [Token] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_SemaphoreLease_Name_LeaseUntilUtc]
    ON [infra].[SemaphoreLease]([Name] ASC, [LeaseUntilUtc] ASC)
    INCLUDE([Token]);


GO
CREATE NONCLUSTERED INDEX [IX_SemaphoreLease_LeaseUntilUtc]
    ON [infra].[SemaphoreLease]([LeaseUntilUtc] ASC);


GO
CREATE NONCLUSTERED INDEX [IX_SemaphoreLease_ClientRequestId]
    ON [infra].[SemaphoreLease]([ClientRequestId] ASC) WHERE ([ClientRequestId] IS NOT NULL);

