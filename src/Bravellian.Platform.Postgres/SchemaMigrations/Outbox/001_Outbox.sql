CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$OutboxTable$" (
    "Id" uuid NOT NULL,
    "Payload" text NOT NULL,
    "Topic" text NOT NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "IsProcessed" boolean NOT NULL DEFAULT FALSE,
    "ProcessedAt" timestamptz NULL,
    "ProcessedBy" text NULL,
    "RetryCount" integer NOT NULL DEFAULT 0,
    "LastError" text NULL,
    "MessageId" uuid NOT NULL,
    "CorrelationId" text NULL,
    "DueTimeUtc" timestamptz NULL,
    "Status" smallint NOT NULL DEFAULT 0,
    "LockedUntil" timestamptz NULL,
    "OwnerToken" uuid NULL,
    CONSTRAINT "PK_$OutboxTable$" PRIMARY KEY ("Id")
);

CREATE INDEX IF NOT EXISTS "IX_$OutboxTable$_WorkQueue"
    ON "$SchemaName$"."$OutboxTable$" ("Status", "CreatedAt")
    INCLUDE ("LockedUntil", "DueTimeUtc")
    WHERE "Status" = 0;

CREATE TABLE IF NOT EXISTS "$SchemaName$"."OutboxState" (
    "Id" integer NOT NULL,
    "CurrentFencingToken" bigint NOT NULL DEFAULT 0,
    "LastDispatchAt" timestamptz NULL,
    CONSTRAINT "PK_OutboxState" PRIMARY KEY ("Id")
);

INSERT INTO "$SchemaName$"."OutboxState" ("Id", "CurrentFencingToken", "LastDispatchAt")
VALUES (1, 0, NULL)
ON CONFLICT ("Id") DO NOTHING;
