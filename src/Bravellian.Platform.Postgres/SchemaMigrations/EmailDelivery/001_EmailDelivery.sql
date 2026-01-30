CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$EmailDeliveryTable$" (
    "EmailDeliveryEventId" uuid NOT NULL,
    "EventType" smallint NOT NULL,
    "Status" smallint NOT NULL,
    "OccurredAtUtc" timestamptz NOT NULL,
    "MessageKey" text NULL,
    "ProviderMessageId" text NULL,
    "ProviderEventId" text NULL,
    "AttemptNumber" integer NULL,
    "ErrorCode" text NULL,
    "ErrorMessage" text NULL,
    "MessagePayload" text NULL,
    "CorrelationId" text NULL,
    "CausationId" text NULL,
    "TraceId" text NULL,
    "SpanId" text NULL,
    "CorrelationCreatedAtUtc" timestamptz NULL,
    "CorrelationTagsJson" text NULL,
    CONSTRAINT "PK_$EmailDeliveryTable$" PRIMARY KEY ("EmailDeliveryEventId")
);

ALTER TABLE "$SchemaName$"."$EmailDeliveryTable$"
    ADD COLUMN IF NOT EXISTS "EmailDeliveryEventId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "EventType" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "Status" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "OccurredAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "MessageKey" text NULL,
    ADD COLUMN IF NOT EXISTS "ProviderMessageId" text NULL,
    ADD COLUMN IF NOT EXISTS "ProviderEventId" text NULL,
    ADD COLUMN IF NOT EXISTS "AttemptNumber" integer NULL,
    ADD COLUMN IF NOT EXISTS "ErrorCode" text NULL,
    ADD COLUMN IF NOT EXISTS "ErrorMessage" text NULL,
    ADD COLUMN IF NOT EXISTS "MessagePayload" text NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationId" text NULL,
    ADD COLUMN IF NOT EXISTS "CausationId" text NULL,
    ADD COLUMN IF NOT EXISTS "TraceId" text NULL,
    ADD COLUMN IF NOT EXISTS "SpanId" text NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationCreatedAtUtc" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationTagsJson" text NULL;

CREATE INDEX IF NOT EXISTS "IX_$EmailDeliveryTable$_OccurredAtUtc"
    ON "$SchemaName$"."$EmailDeliveryTable$" ("OccurredAtUtc");

CREATE INDEX IF NOT EXISTS "IX_$EmailDeliveryTable$_MessageKey"
    ON "$SchemaName$"."$EmailDeliveryTable$" ("MessageKey")
    WHERE "MessageKey" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_$EmailDeliveryTable$_ProviderMessageId"
    ON "$SchemaName$"."$EmailDeliveryTable$" ("ProviderMessageId")
    WHERE "ProviderMessageId" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_$EmailDeliveryTable$_ProviderEventId"
    ON "$SchemaName$"."$EmailDeliveryTable$" ("ProviderEventId")
    WHERE "ProviderEventId" IS NOT NULL;
