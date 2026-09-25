-- Append-only business audit and transactional outbox.
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE IF NOT EXISTS audit.audit_log (
    id uuid PRIMARY KEY,
    aggregate_type varchar(80) NOT NULL,
    aggregate_id uuid NOT NULL,
    entity_type varchar(80) NOT NULL,
    entity_id uuid NOT NULL,
    operation varchar(30) NOT NULL,
    field_name varchar(80) NULL,
    old_value jsonb NULL,
    new_value jsonb NULL,
    actor_id text NOT NULL,
    occurred_at timestamptz NOT NULL,
    reason text NULL,
    correlation_id uuid NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_audit_log_aggregate_time ON audit.audit_log (aggregate_type, aggregate_id, occurred_at);
CREATE INDEX IF NOT EXISTS ix_audit_log_actor_time ON audit.audit_log (actor_id, occurred_at);

CREATE OR REPLACE FUNCTION audit.reject_audit_log_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'audit.audit_log is append-only';
END;
$$;

DROP TRIGGER IF EXISTS audit_log_no_update_delete ON audit.audit_log;
CREATE TRIGGER audit_log_no_update_delete BEFORE UPDATE OR DELETE ON audit.audit_log
FOR EACH ROW EXECUTE FUNCTION audit.reject_audit_log_mutation();

CREATE TABLE IF NOT EXISTS audit.outbox_message (
    event_id uuid PRIMARY KEY,
    event_type varchar(100) NOT NULL,
    aggregate_type varchar(80) NOT NULL,
    aggregate_id uuid NOT NULL,
    payload jsonb NOT NULL,
    occurred_at timestamptz NOT NULL,
    published_at timestamptz NULL,
    attempt_count integer NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NULL,
    last_error text NULL
);

CREATE INDEX IF NOT EXISTS ix_outbox_pending ON audit.outbox_message (occurred_at) WHERE published_at IS NULL;
