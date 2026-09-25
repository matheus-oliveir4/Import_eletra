-- Apply through the release migration job; never through API startup.
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE SCHEMA IF NOT EXISTS migration;
CREATE SCHEMA IF NOT EXISTS procurement;
CREATE SCHEMA IF NOT EXISTS imports;
CREATE SCHEMA IF NOT EXISTS costs;

CREATE TABLE IF NOT EXISTS migration.import_batch (
    id uuid PRIMARY KEY,
    file_name varchar(260) NOT NULL,
    file_sha256 char(64) NOT NULL,
    mapping_version varchar(64) NOT NULL,
    state varchar(32) NOT NULL,
    received_at timestamptz NOT NULL DEFAULT now(),
    promoted_at timestamptz NULL,
    source_row_count integer NULL,
    UNIQUE (file_sha256, mapping_version)
);

CREATE TABLE IF NOT EXISTS migration.source_row (
    id uuid PRIMARY KEY,
    batch_id uuid NOT NULL REFERENCES migration.import_batch(id) ON DELETE RESTRICT,
    sheet_name varchar(80) NOT NULL,
    row_number integer NOT NULL CHECK (row_number > 0),
    raw_values jsonb NOT NULL,
    error_columns jsonb NOT NULL DEFAULT '[]'::jsonb,
    row_hash char(64) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (batch_id, sheet_name, row_number)
);

CREATE TABLE IF NOT EXISTS procurement.purchase_order (
    id uuid PRIMARY KEY,
    importer varchar(120) NOT NULL,
    external_number varchar(80) NOT NULL,
    normalized_number varchar(80) NOT NULL,
    identity_status varchar(32) NOT NULL DEFAULT 'HISTORICAL_UNVERIFIED',
    source_kind varchar(32) NOT NULL DEFAULT 'HISTORICAL_EXCEL',
    version bigint NOT NULL DEFAULT 0,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (importer, normalized_number)
);

CREATE TABLE IF NOT EXISTS imports.import_process (
    id uuid PRIMARY KEY,
    importer varchar(120) NULL,
    ip_number varchar(80) NOT NULL,
    normalized_ip_number varchar(80) NOT NULL,
    logistics_status varchar(40) NULL,
    quality_status varchar(32) NOT NULL DEFAULT 'PENDING_REVIEW',
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (normalized_ip_number)
);

CREATE TABLE IF NOT EXISTS procurement.po_line_observation (
    id uuid PRIMARY KEY,
    purchase_order_id uuid NOT NULL REFERENCES procurement.purchase_order(id) ON DELETE RESTRICT,
    source_row_id uuid NOT NULL REFERENCES migration.source_row(id) ON DELETE RESTRICT,
    source_row_number integer NOT NULL,
    product_code_snapshot text NULL,
    description_snapshot text NULL,
    quantity numeric(24,8) NULL,
    unit_price numeric(24,8) NULL,
    historical_amount numeric(24,8) NULL,
    currency_code char(3) NULL,
    necessity_date date NULL,
    historical_status text NULL,
    source_ip_text text NULL,
    raw_values jsonb NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_row_id)
);

CREATE TABLE IF NOT EXISTS procurement.process_purchase_order (
    purchase_order_id uuid NOT NULL REFERENCES procurement.purchase_order(id) ON DELETE RESTRICT,
    process_id uuid NOT NULL REFERENCES imports.import_process(id) ON DELETE RESTRICT,
    source_kind varchar(32) NOT NULL DEFAULT 'HISTORICAL_EXCEL',
    created_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (purchase_order_id, process_id)
);

CREATE TABLE IF NOT EXISTS costs.process_cost (
    id uuid PRIMARY KEY,
    process_id uuid NOT NULL REFERENCES imports.import_process(id) ON DELETE RESTRICT,
    source_row_id uuid NOT NULL REFERENCES migration.source_row(id) ON DELETE RESTRICT,
    source_column varchar(80) NOT NULL,
    cost_type varchar(32) NOT NULL,
    amount numeric(24,8) NOT NULL,
    currency_code char(3) NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'HISTORICAL',
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (source_row_id, source_column)
);

CREATE TABLE IF NOT EXISTS migration.data_issue (
    id uuid PRIMARY KEY,
    batch_id uuid NOT NULL REFERENCES migration.import_batch(id) ON DELETE RESTRICT,
    source_row_id uuid NULL REFERENCES migration.source_row(id) ON DELETE RESTRICT,
    severity varchar(20) NOT NULL,
    issue_code varchar(64) NOT NULL,
    field_name varchar(80) NULL,
    evidence jsonb NOT NULL,
    status varchar(20) NOT NULL DEFAULT 'OPEN',
    created_at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_source_row_batch_sheet_row ON migration.source_row (batch_id, sheet_name, row_number);
CREATE INDEX IF NOT EXISTS ix_po_importer_number ON procurement.purchase_order (importer, normalized_number);
CREATE INDEX IF NOT EXISTS ix_observation_po ON procurement.po_line_observation (purchase_order_id, source_row_number);
CREATE INDEX IF NOT EXISTS ix_process_importer_status ON imports.import_process (importer, logistics_status);
CREATE INDEX IF NOT EXISTS ix_cost_process_currency ON costs.process_cost (process_id, currency_code, cost_type);
