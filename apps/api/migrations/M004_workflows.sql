-- Production workflow projection and append-only transition journal.
CREATE SCHEMA IF NOT EXISTS audit;

CREATE TABLE IF NOT EXISTS audit.workflow_state (
    aggregate_type varchar(30) NOT NULL,
    aggregate_id uuid NOT NULL,
    current_state varchar(40) NOT NULL,
    previous_active_state varchar(40) NULL,
    version bigint NOT NULL DEFAULT 0,
    PRIMARY KEY (aggregate_type, aggregate_id)
);

CREATE TABLE IF NOT EXISTS audit.workflow_transition_event (
    id uuid PRIMARY KEY,
    aggregate_type varchar(30) NOT NULL,
    aggregate_id uuid NOT NULL,
    from_state varchar(40) NOT NULL,
    to_state varchar(40) NOT NULL,
    actor text NOT NULL,
    reason text NOT NULL,
    occurred_at timestamptz NOT NULL,
    evidence jsonb NOT NULL DEFAULT '{}'::jsonb,
    version bigint NOT NULL,
    UNIQUE (aggregate_type, aggregate_id, version)
);
