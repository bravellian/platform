CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$InboxTable$" (
    "MessageId" varchar(64) NOT NULL,
    "Source" varchar(64) NOT NULL,
    "Hash" bytea NULL,
    "FirstSeenUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "LastSeenUtc" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ProcessedUtc" timestamptz NULL,
    "DueTimeUtc" timestamptz NULL,
    "Attempts" integer NOT NULL DEFAULT 0,
    "Status" varchar(16) NOT NULL DEFAULT 'Seen',
    "LastError" text NULL,
    "LockedUntil" timestamptz NULL,
    "OwnerToken" uuid NULL,
    "Topic" varchar(128) NULL,
    "Payload" text NULL,
    CONSTRAINT "PK_$InboxTable$" PRIMARY KEY ("MessageId"),
    CONSTRAINT "CK_$InboxTable$_Status" CHECK ("Status" IN ('Seen', 'Processing', 'Done', 'Dead'))
);

CREATE INDEX IF NOT EXISTS "IX_$InboxTable$_ProcessedUtc"
    ON "$SchemaName$"."$InboxTable$" ("ProcessedUtc")
    WHERE "ProcessedUtc" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_$InboxTable$_Status"
    ON "$SchemaName$"."$InboxTable$" ("Status");

CREATE INDEX IF NOT EXISTS "IX_$InboxTable$_Status_ProcessedUtc"
    ON "$SchemaName$"."$InboxTable$" ("Status", "ProcessedUtc")
    WHERE "Status" = 'Done' AND "ProcessedUtc" IS NOT NULL;

CREATE INDEX IF NOT EXISTS "IX_$InboxTable$_WorkQueue"
    ON "$SchemaName$"."$InboxTable$" ("Status", "LastSeenUtc")
    INCLUDE ("MessageId", "OwnerToken")
    WHERE "Status" IN ('Seen', 'Processing');
