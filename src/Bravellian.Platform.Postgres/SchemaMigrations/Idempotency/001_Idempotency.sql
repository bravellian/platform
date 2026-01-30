CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$IdempotencyTable$" (
    "IdempotencyKey" text NOT NULL,
    "Status" smallint NOT NULL,
    "LockedUntil" timestamptz NULL,
    "LockedBy" uuid NULL,
    "FailureCount" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "CompletedAt" timestamptz NULL,
    CONSTRAINT "PK_$IdempotencyTable$" PRIMARY KEY ("IdempotencyKey")
);

ALTER TABLE "$SchemaName$"."$IdempotencyTable$"
    ADD COLUMN IF NOT EXISTS "IdempotencyKey" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Status" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "LockedUntil" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LockedBy" uuid NULL,
    ADD COLUMN IF NOT EXISTS "FailureCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "CompletedAt" timestamptz NULL;
