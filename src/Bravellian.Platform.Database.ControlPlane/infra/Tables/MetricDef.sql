CREATE TABLE [infra].[MetricDef] (
    [MetricDefId] INT            IDENTITY (1, 1) NOT NULL,
    [Name]        NVARCHAR (128) NOT NULL,
    [Unit]        NVARCHAR (32)  NOT NULL,
    [AggKind]     NVARCHAR (16)  NOT NULL,
    [Description] NVARCHAR (512) NOT NULL,
    PRIMARY KEY CLUSTERED ([MetricDefId] ASC),
    UNIQUE NONCLUSTERED ([Name] ASC)
);

