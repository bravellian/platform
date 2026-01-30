CREATE TABLE IF NOT EXISTS "$SchemaName$"."$OperationEventsTable$" (
    "EventId" bigserial NOT NULL,
    "OperationId" text NOT NULL,
    "OccurredAtUtc" timestamptz NOT NULL,
    "Kind" text NOT NULL,
    "Message" text NOT NULL,
    "DataJson" text NULL,
    CONSTRAINT "PK_$OperationEventsTable$" PRIMARY KEY ("EventId"),
    CONSTRAINT "FK_$OperationEventsTable$_$OperationsTable$"
        FOREIGN KEY ("OperationId")
        REFERENCES "$SchemaName$"."$OperationsTable$" ("OperationId")
        ON DELETE CASCADE
);

ALTER TABLE "$SchemaName$"."$OperationEventsTable$"
    ADD COLUMN IF NOT EXISTS "EventId" bigserial,
    ADD COLUMN IF NOT EXISTS "OperationId" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "OccurredAtUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "Kind" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Message" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "DataJson" text NULL;

CREATE INDEX IF NOT EXISTS "IX_$OperationEventsTable$_OperationId_OccurredAtUtc"
    ON "$SchemaName$"."$OperationEventsTable$" ("OperationId", "OccurredAtUtc");
