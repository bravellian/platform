CREATE TABLE [infra].[Lease] (
    [Name]           [sysname]     NOT NULL,
    [Owner]          [sysname]     NULL,
    [LeaseUntilUtc]  DATETIME2 (3) NULL,
    [LastGrantedUtc] DATETIME2 (3) NULL,
    [Version]        ROWVERSION    NOT NULL,
    CONSTRAINT [PK_Lease] PRIMARY KEY CLUSTERED ([Name] ASC)
);

