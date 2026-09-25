# PostgreSQL migrations

These migrations are the PostgreSQL subset retained from the former .NET
implementation and reorganized under the active Node API. `M002` is the
PostgreSQL counterpart for quality-review records.

They are not run by API startup. There is not yet a Node migration runner or a
validated history table for these scripts. Review and apply them only to an
isolated database after verifying the VPS PostgreSQL version and taking a
restorable backup. Do not apply them to the operational database yet.

The old SQLite migrations and .NET runner were removed with the retired local
architecture. Historical validation results remain in
`CHECKLIST_IMPLEMENTACAO.md`; they do not imply that the Node API has feature
parity.
