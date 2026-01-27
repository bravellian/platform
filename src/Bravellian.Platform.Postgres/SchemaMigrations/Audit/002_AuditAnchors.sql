CREATE TABLE IF NOT EXISTS "$SchemaName$"."$AuditAnchorsTable$" (
    "AuditEventId" text NOT NULL,
    "AnchorType" text NOT NULL,
    "AnchorId" text NOT NULL,
    "Role" text NOT NULL,
    CONSTRAINT "PK_$AuditAnchorsTable$" PRIMARY KEY ("AuditEventId", "AnchorType", "AnchorId", "Role"),
    CONSTRAINT "FK_$AuditAnchorsTable$_$AuditEventsTable$"
        FOREIGN KEY ("AuditEventId")
        REFERENCES "$SchemaName$"."$AuditEventsTable$" ("AuditEventId")
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS "IX_$AuditAnchorsTable$_Type_Id"
    ON "$SchemaName$"."$AuditAnchorsTable$" ("AnchorType", "AnchorId")
    INCLUDE ("AuditEventId", "Role");
