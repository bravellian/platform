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

CREATE INDEX IF NOT EXISTS "IX_$OperationsTable$_Status_UpdatedAtUtc"
    ON "$SchemaName$"."$OperationsTable$" ("Status", "UpdatedAtUtc")
    INCLUDE ("OperationId", "CompletedAtUtc");

CREATE INDEX IF NOT EXISTS "IX_$OperationsTable$_ParentOperationId"
    ON "$SchemaName$"."$OperationsTable$" ("ParentOperationId");
