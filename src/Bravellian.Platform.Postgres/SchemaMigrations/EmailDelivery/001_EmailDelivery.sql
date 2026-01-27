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
