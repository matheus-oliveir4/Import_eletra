-- Delivery remains at-least-once. Leases are mutable operational metadata;
-- the event id and payload written by the business transaction remain stable.
ALTER TABLE OutboxMessages ADD COLUMN LeaseOwner TEXT NULL;
ALTER TABLE OutboxMessages ADD COLUMN LeaseExpiresAt TEXT NULL;
ALTER TABLE OutboxMessages ADD COLUMN DeadLetteredAt TEXT NULL;

CREATE INDEX IF NOT EXISTS IX_OutboxMessages_Dispatchable
    ON OutboxMessages (PublishedAt, DeadLetteredAt, NextAttemptAt, LeaseExpiresAt, OccurredAt);

CREATE TABLE IF NOT EXISTS EventInboxMessages (
    ConsumerName TEXT NOT NULL,
    EventId TEXT NOT NULL,
    Status TEXT NOT NULL,
    LeaseOwner TEXT NULL,
    LeaseExpiresAt TEXT NULL,
    AttemptCount INTEGER NOT NULL DEFAULT 0,
    ProcessedAt TEXT NULL,
    LastError TEXT NULL,
    CONSTRAINT PK_EventInboxMessages PRIMARY KEY (ConsumerName, EventId),
    CONSTRAINT CK_EventInboxMessages_Status CHECK (Status IN ('PROCESSING', 'COMPLETED', 'PENDING'))
);

CREATE INDEX IF NOT EXISTS IX_EventInboxMessages_Lease ON EventInboxMessages (Status, LeaseExpiresAt);
