CREATE TABLE [infra].[Lease] (
    [Name]           [sysname]          NOT NULL,
    [Owner]          [sysname]          NULL,
    [LeaseUntilUtc]  DATETIMEOFFSET (3) NULL,
    [LastGrantedUtc] DATETIMEOFFSET (3) NULL,
    [Version]        ROWVERSION         NOT NULL,
    CONSTRAINT [PK_Lease] PRIMARY KEY CLUSTERED ([Name] ASC)
);

