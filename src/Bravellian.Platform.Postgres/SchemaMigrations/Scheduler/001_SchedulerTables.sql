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

INSERT INTO "$SchemaName$"."SchedulerState" ("Id", "CurrentFencingToken", "LastRunAt")
VALUES (1, 0, NULL)
ON CONFLICT ("Id") DO NOTHING;
