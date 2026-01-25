CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$LeaseTable$" (
    "Name" text NOT NULL,
    "Owner" text NULL,
    "LeaseUntilUtc" timestamptz NULL,
    "LastGrantedUtc" timestamptz NULL,
    CONSTRAINT "PK_$LeaseTable$" PRIMARY KEY ("Name")
);
