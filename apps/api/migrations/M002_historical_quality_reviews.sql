-- PostgreSQL counterpart for review decisions on immutable imported rows.
-- Apply only through a reviewed migration job against an isolated database.
CREATE TABLE IF NOT EXISTS migration.quality_review (
    id uuid PRIMARY KEY,
    batch_id uuid NOT NULL REFERENCES migration.import_batch(id) ON DELETE RESTRICT,
    source_row_id uuid NOT NULL REFERENCES migration.source_row(id) ON DELETE RESTRICT,
    issue_code varchar(64) NOT NULL,
    outcome varchar(32) NOT NULL,
    reviewer text NOT NULL,
    notes text NOT NULL DEFAULT '',
    proposed_purchase_order varchar(80) NULL,
    proposed_ip_number varchar(80) NULL,
    recorded_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_quality_review_source_issue_recorded
ON migration.quality_review (source_row_id, issue_code, recorded_at DESC);
