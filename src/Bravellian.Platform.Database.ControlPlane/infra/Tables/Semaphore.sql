CREATE TABLE [infra].[Semaphore] (
    [Name]               NVARCHAR (200)     NOT NULL,
    [Limit]              INT                NOT NULL,
    [NextFencingCounter] BIGINT             DEFAULT ((1)) NOT NULL,
    [UpdatedUtc]         DATETIMEOFFSET (3) DEFAULT (sysdatetimeoffset()) NOT NULL,
    CONSTRAINT [PK_Semaphore] PRIMARY KEY CLUSTERED ([Name] ASC)
);

