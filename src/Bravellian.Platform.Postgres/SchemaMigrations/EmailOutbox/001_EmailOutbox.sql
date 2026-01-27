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

CREATE UNIQUE INDEX IF NOT EXISTS "UX_$EmailOutboxTable$_Provider_MessageKey"
    ON "$SchemaName$"."$EmailOutboxTable$" ("ProviderName", "MessageKey");

CREATE INDEX IF NOT EXISTS "IX_$EmailOutboxTable$_Pending"
    ON "$SchemaName$"."$EmailOutboxTable$" ("Status", "DueTimeUtc", "EnqueuedAtUtc")
    WHERE "Status" = 0;
