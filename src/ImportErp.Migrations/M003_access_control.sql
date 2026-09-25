-- PostgreSQL production migration contract for DEV04. Apply through the
-- production migration runner; this script intentionally has no seeded user.
CREATE SCHEMA IF NOT EXISTS identity;

CREATE TABLE IF NOT EXISTS identity.erp_user (
    id uuid PRIMARY KEY,
    issuer text NOT NULL,
    subject text NOT NULL,
    display_name text NULL,
    is_active boolean NOT NULL DEFAULT true,
    CONSTRAINT uq_erp_user_issuer_subject UNIQUE (issuer, subject)
);

CREATE TABLE IF NOT EXISTS identity.erp_user_role (
    user_id uuid NOT NULL REFERENCES identity.erp_user(id),
    role varchar(40) NOT NULL,
    PRIMARY KEY (user_id, role)
);

CREATE TABLE IF NOT EXISTS identity.erp_user_importer_scope (
    user_id uuid NOT NULL REFERENCES identity.erp_user(id),
    importer_code varchar(120) NOT NULL,
    PRIMARY KEY (user_id, importer_code)
);
