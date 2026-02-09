DO $$
BEGIN
    IF to_regclass(format('%I.%I', '$SchemaName$', '$OutboxTable$')) IS NULL THEN
        RETURN;
    END IF;
END
$$;

ALTER TABLE "$SchemaName$"."$OutboxTable$"
    ADD COLUMN IF NOT EXISTS "CreatedOn" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "ProcessedOn" timestamptz NULL,
    ADD COLUMN IF NOT EXISTS "AttemptCount" integer NULL,
    ADD COLUMN IF NOT EXISTS "DueOn" timestamptz NULL;

UPDATE "$SchemaName$"."$OutboxTable$"
SET "CreatedOn" = COALESCE("CreatedOn", "CreatedAt")
WHERE "CreatedOn" IS NULL;

UPDATE "$SchemaName$"."$OutboxTable$"
SET "ProcessedOn" = COALESCE("ProcessedOn", "ProcessedAt")
WHERE "ProcessedOn" IS NULL AND "ProcessedAt" IS NOT NULL;

UPDATE "$SchemaName$"."$OutboxTable$"
SET "AttemptCount" = COALESCE("AttemptCount", "RetryCount")
WHERE "AttemptCount" IS NULL;

UPDATE "$SchemaName$"."$OutboxTable$"
SET "DueOn" = COALESCE("DueOn", "DueTimeUtc")
WHERE "DueOn" IS NULL AND "DueTimeUtc" IS NOT NULL;

ALTER TABLE "$SchemaName$"."$OutboxTable$"
    ALTER COLUMN "CreatedOn" SET NOT NULL,
    ALTER COLUMN "CreatedOn" SET DEFAULT (CURRENT_TIMESTAMP),
    ALTER COLUMN "AttemptCount" SET NOT NULL,
    ALTER COLUMN "AttemptCount" SET DEFAULT 0;
