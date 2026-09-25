-- Local authorization mappings. Identity is always issuer + subject, never email.
CREATE TABLE IF NOT EXISTS ErpUsers (
    Id TEXT NOT NULL CONSTRAINT PK_ErpUsers PRIMARY KEY,
    Issuer TEXT NOT NULL,
    Subject TEXT NOT NULL,
    DisplayName TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS ErpUserRoles (
    UserId TEXT NOT NULL,
    Role TEXT NOT NULL,
    CONSTRAINT PK_ErpUserRoles PRIMARY KEY (UserId, Role)
);

CREATE TABLE IF NOT EXISTS ErpUserImporterScopes (
    UserId TEXT NOT NULL,
    Importer TEXT NOT NULL,
    CONSTRAINT PK_ErpUserImporterScopes PRIMARY KEY (UserId, Importer)
);

CREATE UNIQUE INDEX IF NOT EXISTS IX_ErpUsers_Issuer_Subject ON ErpUsers (Issuer, Subject);

-- Development-only Keycloak principal. Production provisioning is an explicit
-- administrative deployment action and must not reuse this account.
INSERT OR IGNORE INTO ErpUsers (Id, Issuer, Subject, DisplayName, IsActive)
VALUES ('11111111-1111-1111-1111-111111111111', 'http://localhost:8180/realms/import-erp', '11111111-1111-1111-1111-111111111111', 'Administrador local', 1);
INSERT OR IGNORE INTO ErpUserRoles (UserId, Role)
VALUES ('11111111-1111-1111-1111-111111111111', 'ADMINISTRATOR');
INSERT OR IGNORE INTO ErpUserImporterScopes (UserId, Importer)
VALUES ('11111111-1111-1111-1111-111111111111', '*');
