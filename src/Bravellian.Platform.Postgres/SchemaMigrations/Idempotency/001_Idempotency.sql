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
