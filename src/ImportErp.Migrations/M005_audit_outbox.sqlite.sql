-- Audit entries are immutable. Outbox rows are inserted with business commits;
-- delivery state is mutable by a future dispatcher.
CREATE TABLE IF NOT EXISTS AuditLog (
    Id TEXT NOT NULL CONSTRAINT PK_AuditLog PRIMARY KEY,
    AggregateType TEXT NOT NULL,
    AggregateId TEXT NOT NULL,
    EntityType TEXT NOT NULL,
    EntityId TEXT NOT NULL,
    Operation TEXT NOT NULL,
    FieldName TEXT NULL,
    OldValueJson TEXT NULL,
    NewValueJson TEXT NULL,
    ActorId TEXT NOT NULL,
    OccurredAt TEXT NOT NULL,
    Reason TEXT NULL,
    CorrelationId TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS IX_AuditLog_Aggregate_Time ON AuditLog (AggregateType, AggregateId, OccurredAt);
CREATE INDEX IF NOT EXISTS IX_AuditLog_Actor_Time ON AuditLog (ActorId, OccurredAt);

CREATE TRIGGER IF NOT EXISTS TR_AuditLog_NoUpdate
BEFORE UPDATE ON AuditLog BEGIN
    SELECT RAISE(ABORT, 'AuditLog is append-only');
END;

CREATE TRIGGER IF NOT EXISTS TR_AuditLog_NoDelete
BEFORE DELETE ON AuditLog BEGIN
    SELECT RAISE(ABORT, 'AuditLog is append-only');
END;

CREATE TABLE IF NOT EXISTS OutboxMessages (
    EventId TEXT NOT NULL CONSTRAINT PK_OutboxMessages PRIMARY KEY,
    EventType TEXT NOT NULL,
    AggregateType TEXT NOT NULL,
    AggregateId TEXT NOT NULL,
    PayloadJson TEXT NOT NULL,
    OccurredAt TEXT NOT NULL,
    PublishedAt TEXT NULL,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    NextAttemptAt TEXT NULL,
    LastError TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_OutboxMessages_Pending ON OutboxMessages (OccurredAt) WHERE PublishedAt IS NULL;
