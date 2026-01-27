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

CREATE INDEX IF NOT EXISTS "IX_$OperationEventsTable$_OperationId_OccurredAtUtc"
    ON "$SchemaName$"."$OperationEventsTable$" ("OperationId", "OccurredAtUtc");
