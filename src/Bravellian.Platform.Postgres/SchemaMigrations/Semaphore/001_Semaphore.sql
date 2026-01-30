CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."Semaphore" (
    "Name" varchar(200) NOT NULL,
    "Limit" integer NOT NULL,
    "NextFencingCounter" bigint NOT NULL DEFAULT 1,
    "UpdatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "PK_Semaphore" PRIMARY KEY ("Name")
);

ALTER TABLE "$SchemaName$"."Semaphore"
    ADD COLUMN IF NOT EXISTS "Name" varchar(200) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Limit" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "NextFencingCounter" bigint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS "UpdatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;

CREATE TABLE IF NOT EXISTS "$SchemaName$"."SemaphoreLease" (
    "Name" varchar(200) NOT NULL,
    "Token" uuid NOT NULL,
    "Fencing" bigint NOT NULL,
    "OwnerId" varchar(200) NOT NULL,
    "LeaseUntilUtc" timestamptz NOT NULL,
    "CreatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "RenewedUtc" timestamptz NULL,
    "ClientRequestId" varchar(100) NULL,
    CONSTRAINT "PK_SemaphoreLease" PRIMARY KEY ("Name", "Token")
);

ALTER TABLE "$SchemaName$"."SemaphoreLease"
    ADD COLUMN IF NOT EXISTS "Name" varchar(200) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Token" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "Fencing" bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "OwnerId" varchar(200) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "LeaseUntilUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "CreatedUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "RenewedUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "ClientRequestId" varchar(100) NULL;

CREATE INDEX IF NOT EXISTS "IX_SemaphoreLease_Name_LeaseUntilUtc"
    ON "$SchemaName$"."SemaphoreLease" ("Name", "LeaseUntilUtc")
    INCLUDE ("Token");

CREATE INDEX IF NOT EXISTS "IX_SemaphoreLease_LeaseUntilUtc"
    ON "$SchemaName$"."SemaphoreLease" ("LeaseUntilUtc");

CREATE INDEX IF NOT EXISTS "IX_SemaphoreLease_ClientRequestId"
    ON "$SchemaName$"."SemaphoreLease" ("ClientRequestId")
    WHERE "ClientRequestId" IS NOT NULL;
