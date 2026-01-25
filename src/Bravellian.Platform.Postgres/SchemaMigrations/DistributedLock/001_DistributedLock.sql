CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$LockTable$" (
    "ResourceName" text NOT NULL,
    "OwnerToken" uuid NULL,
    "LeaseUntil" timestamptz NULL,
    "FencingToken" bigint NOT NULL DEFAULT 0,
    "ContextJson" text NULL,
    CONSTRAINT "PK_$LockTable$" PRIMARY KEY ("ResourceName")
);

CREATE INDEX IF NOT EXISTS "IX_$LockTable$_OwnerToken"
    ON "$SchemaName$"."$LockTable$" ("OwnerToken")
    WHERE "OwnerToken" IS NOT NULL;
