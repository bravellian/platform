CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$PolicyTable$" (
    "FanoutTopic" varchar(100) NOT NULL,
    "WorkKey" varchar(100) NOT NULL,
    "DefaultEverySeconds" integer NOT NULL,
    "JitterSeconds" integer NOT NULL DEFAULT 60,
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "PK_$PolicyTable$" PRIMARY KEY ("FanoutTopic", "WorkKey")
);

ALTER TABLE "$SchemaName$"."$PolicyTable$"
    ADD COLUMN IF NOT EXISTS "FanoutTopic" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "WorkKey" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "DefaultEverySeconds" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "JitterSeconds" integer NOT NULL DEFAULT 60,
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;

CREATE INDEX IF NOT EXISTS "IX_$PolicyTable$_FanoutTopic"
    ON "$SchemaName$"."$PolicyTable$" ("FanoutTopic");

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$CursorTable$" (
    "FanoutTopic" varchar(100) NOT NULL,
    "WorkKey" varchar(100) NOT NULL,
    "ShardKey" varchar(100) NOT NULL,
    "LastCompletedAt" timestamptz NULL,
    "LastAttemptAt" timestamptz NULL,
    "LastAttemptStatus" varchar(20) NULL,
    "NextAttemptAt" timestamptz NULL,
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "UpdatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT "PK_$CursorTable$" PRIMARY KEY ("FanoutTopic", "WorkKey", "ShardKey")
);

ALTER TABLE "$SchemaName$"."$CursorTable$"
    ADD COLUMN IF NOT EXISTS "FanoutTopic" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "WorkKey" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "ShardKey" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "LastCompletedAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LastAttemptAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LastAttemptStatus" varchar(20) NULL,
    ADD COLUMN IF NOT EXISTS "NextAttemptAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP;

CREATE INDEX IF NOT EXISTS "IX_$CursorTable$_TopicWork"
    ON "$SchemaName$"."$CursorTable$" ("FanoutTopic", "WorkKey");

CREATE INDEX IF NOT EXISTS "IX_$CursorTable$_LastCompleted"
    ON "$SchemaName$"."$CursorTable$" ("LastCompletedAt")
    WHERE "LastCompletedAt" IS NOT NULL;
