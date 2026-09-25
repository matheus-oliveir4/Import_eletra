#!/usr/bin/env sh
# Executed only when the local PostgreSQL volume is first initialized.
# Values are development-only and may contain only simple identifier/password
# characters; do not use this mechanism for shared environments.
set -eu

psql --username "$POSTGRES_USER" --dbname postgres --set ON_ERROR_STOP=1 <<SQL
CREATE ROLE $ERP_DB_USER LOGIN PASSWORD '$ERP_DB_PASSWORD';
CREATE DATABASE import_erp OWNER $ERP_DB_USER;
CREATE ROLE $DW_DB_USER LOGIN PASSWORD '$DW_DB_PASSWORD';
CREATE DATABASE import_dw OWNER $DW_DB_USER;
CREATE ROLE $KEYCLOAK_DB_USER LOGIN PASSWORD '$KEYCLOAK_DB_PASSWORD';
CREATE DATABASE keycloak OWNER $KEYCLOAK_DB_USER;
SQL
