# Migrations operacionais

`M006_outbox_dispatch` is additive: it adds mutable outbox lease/dead-letter
metadata and the `(consumer_name, event_id)` inbox key without deleting or
reinitializing the local database. PostgreSQL workers claim with `FOR UPDATE SKIP
LOCKED`. The mechanism is at-least-once; an inbox cannot turn an external side
effect into exactly-once delivery.

`M001_historical_core.sql` cria o mínimo necessário para staging e promoção da
carga histórica. Execute-o somente por um job de release contra PostgreSQL;
a API não aplica migrations automaticamente.

O script usa `migration.source_row` como evidência imutável, mantém uma única
PO por `(importer, normalized_number)`, cria IPs por número normalizado e
impede a repetição de custo por `(source_row_id, source_column)`.

`M001_historical_core.sqlite.sql` é a versão versionada e incorporada para
SQLite local. Ela é aplicada pelo `SqliteMigrationRunner`, que registra hash e
identificador em `__import_erp_migrations`. O banco de teste existente jamais é
apagado: se ele tiver o esquema completo sem histórico, a migration é marcada
como baseline; um esquema parcial falha de forma segura.
