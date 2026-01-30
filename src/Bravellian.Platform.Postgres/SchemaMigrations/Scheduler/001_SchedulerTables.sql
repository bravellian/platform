CREATE SCHEMA IF NOT EXISTS "$SchemaName$";

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$JobsTable$" (
    "Id" uuid NOT NULL,
    "JobName" varchar(100) NOT NULL,
    "CronSchedule" varchar(100) NOT NULL,
    "Topic" text NOT NULL,
    "Payload" text NULL,
    "IsEnabled" boolean NOT NULL DEFAULT TRUE,
    "NextDueTime" timestamptz NULL,
    "LastRunTime" timestamptz NULL,
    "LastRunStatus" varchar(20) NULL,
    CONSTRAINT "PK_$JobsTable$" PRIMARY KEY ("Id")
);

ALTER TABLE "$SchemaName$"."$JobsTable$"
    ADD COLUMN IF NOT EXISTS "Id" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "JobName" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "CronSchedule" varchar(100) NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Topic" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Payload" text NULL,
    ADD COLUMN IF NOT EXISTS "IsEnabled" boolean NOT NULL DEFAULT TRUE,
    ADD COLUMN IF NOT EXISTS "NextDueTime" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LastRunTime" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LastRunStatus" varchar(20) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS "UQ_$JobsTable$_JobName"
    ON "$SchemaName$"."$JobsTable$" ("JobName");

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$JobRunsTable$" (
    "Id" uuid NOT NULL,
    "JobId" uuid NOT NULL,
    "ScheduledTime" timestamptz NOT NULL,
    "StatusCode" smallint NOT NULL DEFAULT 0,
    "LockedUntil" timestamptz NULL,
    "OwnerToken" uuid NULL,
    "Status" varchar(20) NOT NULL DEFAULT 'Pending',
    "ClaimedBy" varchar(100) NULL,
    "ClaimedAt" timestamptz NULL,
    "RetryCount" integer NOT NULL DEFAULT 0,
    "StartTime" timestamptz NULL,
    "EndTime" timestamptz NULL,
    "Output" text NULL,
    "LastError" text NULL,
    CONSTRAINT "PK_$JobRunsTable$" PRIMARY KEY ("Id"),
    CONSTRAINT "FK_$JobRunsTable$_Jobs" FOREIGN KEY ("JobId")
        REFERENCES "$SchemaName$"."$JobsTable$" ("Id")
);

ALTER TABLE "$SchemaName$"."$JobRunsTable$"
    ADD COLUMN IF NOT EXISTS "Id" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "JobId" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "ScheduledTime" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "StatusCode" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "LockedUntil" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "OwnerToken" uuid NULL,
    ADD COLUMN IF NOT EXISTS "Status" varchar(20) NOT NULL DEFAULT 'Pending',
    ADD COLUMN IF NOT EXISTS "ClaimedBy" varchar(100) NULL,
    ADD COLUMN IF NOT EXISTS "ClaimedAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "StartTime" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "EndTime" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "Output" text NULL,
    ADD COLUMN IF NOT EXISTS "LastError" text NULL;

CREATE INDEX IF NOT EXISTS "IX_$JobRunsTable$_WorkQueue"
    ON "$SchemaName$"."$JobRunsTable$" ("StatusCode", "ScheduledTime")
    INCLUDE ("Id", "OwnerToken")
    WHERE "StatusCode" = 0;

CREATE TABLE IF NOT EXISTS "$SchemaName$"."$TimersTable$" (
    "Id" uuid NOT NULL,
    "DueTime" timestamptz NOT NULL,
    "Payload" text NOT NULL,
    "Topic" text NOT NULL,
    "CorrelationId" text NULL,
    "StatusCode" smallint NOT NULL DEFAULT 0,
    "LockedUntil" timestamptz NULL,
    "OwnerToken" uuid NULL,
    "Status" varchar(20) NOT NULL DEFAULT 'Pending',
    "ClaimedBy" varchar(100) NULL,
    "ClaimedAt" timestamptz NULL,
    "RetryCount" integer NOT NULL DEFAULT 0,
    "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ProcessedAt" timestamptz NULL,
    "LastError" text NULL,
    CONSTRAINT "PK_$TimersTable$" PRIMARY KEY ("Id")
);

ALTER TABLE "$SchemaName$"."$TimersTable$"
    ADD COLUMN IF NOT EXISTS "Id" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
    ADD COLUMN IF NOT EXISTS "DueTime" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "Payload" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "Topic" text NOT NULL DEFAULT '',
    ADD COLUMN IF NOT EXISTS "CorrelationId" text NULL,
    ADD COLUMN IF NOT EXISTS "StatusCode" smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "LockedUntil" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "OwnerToken" uuid NULL,
    ADD COLUMN IF NOT EXISTS "Status" varchar(20) NOT NULL DEFAULT 'Pending',
    ADD COLUMN IF NOT EXISTS "ClaimedBy" varchar(100) NULL,
    ADD COLUMN IF NOT EXISTS "ClaimedAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "CreatedAt" timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    ADD COLUMN IF NOT EXISTS "ProcessedAt" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "LastError" text NULL;

CREATE INDEX IF NOT EXISTS "IX_$TimersTable$_WorkQueue"
    ON "$SchemaName$"."$TimersTable$" ("StatusCode", "DueTime")
    INCLUDE ("Id", "OwnerToken")
    WHERE "StatusCode" = 0;

CREATE TABLE IF NOT EXISTS "$SchemaName$"."SchedulerState" (
    "Id" integer NOT NULL,
    "CurrentFencingToken" bigint NOT NULL DEFAULT 0,
    "LastRunAt" timestamptz NULL,
    CONSTRAINT "PK_SchedulerState" PRIMARY KEY ("Id")
);

ALTER TABLE "$SchemaName$"."SchedulerState"
    ADD COLUMN IF NOT EXISTS "Id" integer NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS "CurrentFencingToken" bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS "LastRunAt" timestamptz NULL;

INSERT INTO "$SchemaName$"."SchedulerState" ("Id", "CurrentFencingToken", "LastRunAt")
VALUES (1, 0, NULL)
ON CONFLICT ("Id") DO NOTHING;
