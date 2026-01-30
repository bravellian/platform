CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$EmailOutboxTable$" (
    "EmailOutboxId" uuid NOT NULL,
    "ProviderName" text NOT NULL,
    "MessageKey" text NOT NULL,
    "Payload" text NOT NULL,
    "EnqueuedAtUtc" timestamptz NOT NULL,
    "DueTimeUtc" timestamptz NULL,
    "AttemptCount" integer NOT NULL DEFAULT 0,
    "Status" smallint NOT NULL DEFAULT 0,
    "FailureReason" text NULL,
    CONSTRAINT "PK_$EmailOutboxTable$" PRIMARY KEY ("EmailOutboxId")
);

ALTER TABLE "$SchemaName$"."$EmailOutboxTable$"
    ADD COLUMN IF NOT EXISTS "EmailOutboxId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "ProviderName" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "MessageKey" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Payload" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "EnqueuedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "DueTimeUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "AttemptCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "Status" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "FailureReason" text NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UX_$EmailOutboxTable$_Provider_MessageKey"
    ON "$SchemaName$"."$EmailOutboxTable$" ("ProviderName", "MessageKey");

CREATE INDEX IF NOT EXISTS "IX_$EmailOutboxTable$_Pending"
    ON "$SchemaName$"."$EmailOutboxTable$" ("Status", "DueTimeUtc", "EnqueuedAtUtc")
    WHERE "Status" = 0;
