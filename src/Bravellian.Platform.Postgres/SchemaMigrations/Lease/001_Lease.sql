CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$LeaseTable$" (
    "Name" text NOT NULL,
    "Owner" text NULL,
    "LeaseUntilUtc" timestamptz NULL,
    "LastGrantedUtc" timestamptz NULL,
    CONSTRAINT "PK_$LeaseTable$" PRIMARY KEY ("Name")
);

ALTER TABLE "$SchemaName$"."$LeaseTable$"
    ADD COLUMN IF NOT EXISTS "Name" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Owner" text NULL,
    ADD COLUMN IF NOT EXISTS "LeaseUntilUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LastGrantedUtc" timestamptz NULL;
