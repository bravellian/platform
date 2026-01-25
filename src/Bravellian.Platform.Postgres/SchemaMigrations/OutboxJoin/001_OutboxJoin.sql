CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."OutboxJoin" (
    "JoinId" uuid NOT NULL,
    "PayeWaiveTenantId" bigint NOT NULL,
    "ExpectedSteps" integer NOT NULL,
    "CompletedSteps" integer NOT NULL DEFAULT 0,
    "FailedSteps" integer NOT NULL DEFAULT 0,
    "Status" smallint NOT NULL DEFAULT 0,
    "CreatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "LastUpdatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "Metadata" text NULL,
    CONSTRAINT "PK_OutboxJoin" PRIMARY KEY ("JoinId")
);

CREATE INDEX IF NOT EXISTS "IX_OutboxJoin_TenantStatus"
    ON "$SchemaName$"."OutboxJoin" ("PayeWaiveTenantId", "Status");

CREATE TABLE IF NOT EXISTS "$SchemaName$"."OutboxJoinMember" (
    "JoinId" uuid NOT NULL,
    "OutboxMessageId" uuid NOT NULL,
    "CreatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "CompletedAt" timestamptz NULL,
    "FailedAt" timestamptz NULL,
    CONSTRAINT "PK_OutboxJoinMember" PRIMARY KEY ("JoinId", "OutboxMessageId"),
    CONSTRAINT "FK_OutboxJoinMember_Join" FOREIGN KEY ("JoinId")
        REFERENCES "$SchemaName$"."OutboxJoin" ("JoinId") ON DELETE CASCADE,
    CONSTRAINT "FK_OutboxJoinMember_Outbox" FOREIGN KEY ("OutboxMessageId")
        REFERENCES "$SchemaName$"."$OutboxTable$" ("Id") ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_OutboxJoinMember_MessageId"
    ON "$SchemaName$"."OutboxJoinMember" ("OutboxMessageId");
