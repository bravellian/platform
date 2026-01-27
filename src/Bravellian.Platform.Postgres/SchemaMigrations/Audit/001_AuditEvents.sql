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

CREATE INDEX IF NOT EXISTS "IX_$AuditEventsTable$_OccurredAtUtc"
    ON "$SchemaName$"."$AuditEventsTable$" ("OccurredAtUtc" DESC);

CREATE INDEX IF NOT EXISTS "IX_$AuditEventsTable$_Name_OccurredAtUtc"
    ON "$SchemaName$"."$AuditEventsTable$" ("Name", "OccurredAtUtc" DESC);
