CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$LockTable$" (
    "ResourceName" text NOT NULL,
    "OwnerToken" uuid NULL,
    "LeaseUntil" timestamptz NULL,
    "FencingToken" bigint NOT NULL DEFAULT 0,
    "ContextJson" text NULL,
    CONSTRAINT "PK_$LockTable$" PRIMARY KEY ("ResourceName")
);

ALTER TABLE "$SchemaName$"."$LockTable$"
    ADD COLUMN IF NOT EXISTS "ResourceName" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "OwnerToken" uuid NULL,
    ADD COLUMN IF NOT EXISTS "LeaseUntil" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "FencingToken" bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "ContextJson" text NULL;

CREATE INDEX IF NOT EXISTS "IX_$LockTable$_OwnerToken"
    ON "$SchemaName$"."$LockTable$" ("OwnerToken")
    WHERE "OwnerToken" IS NOT NULL;
