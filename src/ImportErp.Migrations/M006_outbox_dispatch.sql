-- PostgreSQL is the initial operational queue (ADR 010). The worker claims
-- rows with SKIP LOCKED; this migration supplies durable lease and inbox state.
ALTER TABLE audit.outbox_message ADD COLUMN IF NOT EXISTS lease_owner text NULL;
ALTER TABLE audit.outbox_message ADD COLUMN IF NOT EXISTS lease_expires_at timestamptz NULL;
ALTER TABLE audit.outbox_message ADD COLUMN IF NOT EXISTS dead_lettered_at timestamptz NULL;

CREATE INDEX IF NOT EXISTS ix_outbox_dispatchable
    ON audit.outbox_message (next_attempt_at, lease_expires_at, occurred_at)
    WHERE published_at IS NULL AND dead_lettered_at IS NULL;

CREATE TABLE IF NOT EXISTS audit.event_inbox_message (
    consumer_name varchar(160) NOT NULL,
    event_id uuid NOT NULL,
    status varchar(20) NOT NULL,
    lease_owner text NULL,
    lease_expires_at timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    processed_at timestamptz NULL,
    last_error text NULL,
    PRIMARY KEY (consumer_name, event_id),
    CONSTRAINT ck_event_inbox_message_status CHECK (status IN ('PROCESSING', 'COMPLETED', 'PENDING'))
);

CREATE INDEX IF NOT EXISTS ix_event_inbox_message_lease
    ON audit.event_inbox_message (status, lease_expires_at);
