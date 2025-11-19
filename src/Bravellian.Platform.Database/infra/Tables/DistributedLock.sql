CREATE TABLE [infra].[DistributedLock] (
    [ResourceName] [sysname]        NOT NULL,
    [OwnerToken]   UNIQUEIDENTIFIER NULL,
    [LeaseUntil]   DATETIME2 (3)    NULL,
    [FencingToken] BIGINT           CONSTRAINT [DF_DistributedLock_Fence] DEFAULT ((0)) NOT NULL,
    [ContextJson]  NVARCHAR (MAX)   NULL,
    [Version]      ROWVERSION       NOT NULL,
    CONSTRAINT [PK_DistributedLock] PRIMARY KEY CLUSTERED ([ResourceName] ASC)
);


GO
CREATE NONCLUSTERED INDEX [IX_DistributedLock_OwnerToken]
    ON [infra].[DistributedLock]([OwnerToken] ASC) WHERE ([OwnerToken] IS NOT NULL);

