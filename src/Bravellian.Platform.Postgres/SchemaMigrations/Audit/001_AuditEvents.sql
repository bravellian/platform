CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$AuditEventsTable$" (
    "AuditEventId" text NOT NULL,
    "OccurredAtUtc" timestamptz NOT NULL,
    "Name" text NOT NULL,
    "DisplayMessage" text NOT NULL,
    "Outcome" smallint NOT NULL,
    "DataJson" text NULL,
    "ActorType" text NULL,
    "ActorId" text NULL,
    "ActorDisplay" text NULL,
    "CorrelationId" text NULL,
    "CausationId" text NULL,
    "TraceId" text NULL,
    "SpanId" text NULL,
    "CorrelationCreatedAtUtc" timestamptz NULL,
    "CorrelationTagsJson" text NULL,
    CONSTRAINT "PK_$AuditEventsTable$" PRIMARY KEY ("AuditEventId")
);

ALTER TABLE "$SchemaName$"."$AuditEventsTable$"
    ADD COLUMN IF NOT EXISTS "AuditEventId" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "OccurredAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "Name" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "DisplayMessage" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Outcome" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "DataJson" text NULL,
    ADD COLUMN IF NOT EXISTS "ActorType" text NULL,
    ADD COLUMN IF NOT EXISTS "ActorId" text NULL,
    ADD COLUMN IF NOT EXISTS "ActorDisplay" text NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationId" text NULL,
    ADD COLUMN IF NOT EXISTS "CausationId" text NULL,
    ADD COLUMN IF NOT EXISTS "TraceId" text NULL,
    ADD COLUMN IF NOT EXISTS "SpanId" text NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationCreatedAtUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationTagsJson" text NULL;

CREATE INDEX IF NOT EXISTS "IX_$AuditEventsTable$_OccurredAtUtc"
    ON "$SchemaName$"."$AuditEventsTable$" ("OccurredAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_$AuditEventsTable$_Name_OccurredAtUtc"
    ON "$SchemaName$"."$AuditEventsTable$" ("Name", "OccurredAtUtc" DESC);
