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

ALTER TABLE "$SchemaName$"."OutboxJoin"
    ADD COLUMN IF NOT EXISTS "JoinId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "PayeWaiveTenantId" bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "ExpectedSteps" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "CompletedSteps" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "FailedSteps" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "Status" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "CreatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "LastUpdatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "Metadata" text NULL;

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

ALTER TABLE "$SchemaName$"."OutboxJoinMember"
    ADD COLUMN IF NOT EXISTS "JoinId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "OutboxMessageId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "CreatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "CompletedAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "FailedAt" timestamptz NULL;

CREATE INDEX IF NOT EXISTS "IX_OutboxJoinMember_MessageId"
    ON "$SchemaName$"."OutboxJoinMember" ("OutboxMessageId");
