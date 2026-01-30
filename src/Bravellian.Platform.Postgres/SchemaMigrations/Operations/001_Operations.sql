CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$OperationsTable$" (
    "OperationId" text NOT NULL,
    "Name" text NOT NULL,
    "Status" smallint NOT NULL,
    "StartedAtUtc" timestamptz NOT NULL,
    "UpdatedAtUtc" timestamptz NOT NULL,
    "CompletedAtUtc" timestamptz NULL,
    "PercentComplete" numeric(5,2) NULL,
    "Message" text NULL,
    "CorrelationId" text NULL,
    "CausationId" text NULL,
    "TraceId" text NULL,
    "SpanId" text NULL,
    "CorrelationCreatedAtUtc" timestamptz NULL,
    "CorrelationTagsJson" text NULL,
    "ParentOperationId" text NULL,
    "TagsJson" text NULL,
    "RowVersion" bigint NOT NULL DEFAULT 0,
    CONSTRAINT "PK_$OperationsTable$" PRIMARY KEY ("OperationId")
);

ALTER TABLE "$SchemaName$"."$OperationsTable$"
    ADD COLUMN IF NOT EXISTS "OperationId" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Name" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Status" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "StartedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "UpdatedAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "CompletedAtUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "PercentComplete" numeric(5,2) NULL,
    ADD COLUMN IF NOT EXISTS "Message" text NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationId" text NULL,
    ADD COLUMN IF NOT EXISTS "CausationId" text NULL,
    ADD COLUMN IF NOT EXISTS "TraceId" text NULL,
    ADD COLUMN IF NOT EXISTS "SpanId" text NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationCreatedAtUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationTagsJson" text NULL,
    ADD COLUMN IF NOT EXISTS "ParentOperationId" text NULL,
    ADD COLUMN IF NOT EXISTS "TagsJson" text NULL,
    ADD COLUMN IF NOT EXISTS "RowVersion" bigint NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS "IX_$OperationsTable$_Status_UpdatedAtUtc"
    ON "$SchemaName$"."$OperationsTable$" ("Status", "UpdatedAtUtc")
    INCLUDE ("OperationId", "CompletedAtUtc");

CREATE INDEX IF NOT EXISTS "IX_$OperationsTable$_ParentOperationId"
    ON "$SchemaName$"."$OperationsTable$" ("ParentOperationId");
