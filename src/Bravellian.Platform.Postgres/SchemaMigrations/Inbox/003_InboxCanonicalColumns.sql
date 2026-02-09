DO $$
BEGIN
    IF to_regclass(format('%I.%I', '$SchemaName$', '$InboxTable$')) IS NULL THEN
        RETURN;
    END IF;
END
$$;

ALTER TABLE "$SchemaName$"."$InboxTable$"
    ADD COLUMN IF NOT EXISTS "CreatedOn" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "ProcessedOn" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "AttemptCount" integer NULL,
    ADD COLUMN IF NOT EXISTS "DueOn" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "CorrelationId" text NULL,
    ADD COLUMN IF NOT EXISTS "ProcessedBy" text NULL;

UPDATE "$SchemaName$"."$InboxTable$"
SET "CreatedOn" = COALESCE("CreatedOn", "FirstSeenUtc")
WHERE "CreatedOn" IS NULL;

UPDATE "$SchemaName$"."$InboxTable$"
SET "ProcessedOn" = COALESCE("ProcessedOn", "ProcessedUtc")
WHERE "ProcessedOn" IS NULL AND "ProcessedUtc" IS NOT NULL;

UPDATE "$SchemaName$"."$InboxTable$"
SET "AttemptCount" = COALESCE("AttemptCount", "Attempts")
WHERE "AttemptCount" IS NULL;

UPDATE "$SchemaName$"."$InboxTable$"
SET "DueOn" = COALESCE("DueOn", "DueTimeUtc")
WHERE "DueOn" IS NULL AND "DueTimeUtc" IS NOT NULL;

ALTER TABLE "$SchemaName$"."$InboxTable$"
    ALTER COLUMN "CreatedOn" SET NOT NULL,
    ALTER COLUMN "CreatedOn" SET DEFAULT (CURRENT_TIMESTAMP),
    ALTER COLUMN "AttemptCount" SET NOT NULL,
    ALTER COLUMN "AttemptCount" SET DEFAULT 0;
