# v1.0.0-rc.4 — Fail-closed SQLite legacy schema preflight

**Engineering status: production NO-GO.** This version strengthens the **actual pre-migration code path** but exercises it only on fake, disposable old-provider-generated databases. It does not accept a real institution database, grant privacy permission, or write a sanitized production data copy.

## Migration safety boundary

When \`DatabaseSetup.InitializeDatabaseAtPath(existingPath, allowExistingUpgrade: true)\` encounters an **existing** SQLite schema version 0 or 1, it now runs \`LegacyMigrationPreflight.Inspect\` **before creating a pre-migration backup or performing any DDL**. The opt-in Boolean alone cannot bypass a structural rejection.

The preflight uses fixed, compiled-in SQL identifiers, never interpolates any student identifier, and only returns controlled blocker codes. Its checks include:

- Exact known original Students, Attendance, Holidays column names/types/order/primary-key layout, plus SchemaMigrations only for v1. No arbitrary extra fields, generated/hidden columns or silently missing columns.
- No extra application tables, views, triggers or custom indexes. These would violate assumptions of the additive migration, or allow an unexpected trigger to change legacy contents.
- SQLite full \`PRAGMA integrity_check\` and \`PRAGMA foreign_key_check\`; rejects altered \`application_id\` or \`user_version\` metadata.
- Expected version 0/1 only, and rejects unknown migration history.
- Aggregate-only referential consistency: attendance rows with NULL or unknown StudentID are rejected. The report never returns the IDs or row contents.

If any check fails, migration throws a **generic** InvalidOperationException before SQLite online backup and before schema changes. The exception does not include table contents, names, attendance statuses, filesystem path or private information.

**Preflight is NOT release authorization.** \`StructurallyCompatible=true\` means only that the file's shape matches the supported limited schema. \`MigrationAuthorized=false\` and \`ProductionDataApproved=false\` are always explicitly returned. The caller also still needs a separately approved, secured backup and organizational privacy approval. Existing schema v2/v3 upgrades use their separate migration path and are not classified as historical schema 0/1.

## Negative controls in Windows CI

The CI step previously introduced in rc.3 runs a standalone **v1.0.119 provider process** to create two real synthetic SQLite files (unversioned/v1). A separate **v2.0.4 provider process** now:

1. Checks both legitimate old file layouts as compatible, without authorizing release.
2. Makes SQLite-online-backup *clones* (never a WAL-unsafe \`File.Copy\`) of each fixture and injects five dangerous variations: an unrecognized Students column, a new table, a SQL trigger, an orphan attendance StudentID, and a modified SQLite application ID.
3. Confirms every variant is rejected by the preflight **and** by the actual explicit-upgrade API. Verifies refusal produces **no pre-migration backup file**, schema version does not change, and error messages contain no fixture student values.
4. Verifies the original files still undergo explicitly authorized *synthetic-only* schema-0/1 → v4 upgrade, preserve all 18 preexisting source records including Hebrew/NULL fields, retain their verified pre-migration snapshots, and support one new idempotent event per file.

All generated data, clone files and backup artifacts stay inside the isolated runner TEMP directory; they are deleted after the test and never uploaded to GitHub. The CI does not expose student identifiers, phone numbers or raw data.

## When a privacy owner later authorizes a representative historical fixture

A real institution would first need to conduct the historical Git-PII incident assessment, obtain an approved **irreversibly anonymized** representative copy produced by an authorized data-controller process, and verify encrypted temporary storage/OS ACL/secure key custody. Keep the file **outside** the public Git repository, CI and any connected developer workspace. Do not attach, email, upload or paste student DBs into these tools.

Then, in an isolated, controlled and authorized environment (not these GitHub Actions), compare the representative sanitized schema and original domain semantics against this limited preflight. Any mismatches require a separate reviewed migration mapping. A positive shape report is not permission to copy records or turn on Realand/Twilio. A real-source migration rehearsal still needs data-loss reconciliation, offsite backups, restoration evidence and owner signoff.

## Not provided

No production CLI to inspect private databases, no real student records, no de-identification engine, no automatic schema repair/downgrade, no hosted migration service, no production encrypted scratch disk, no long-term backups and no completed physical pilot.

**Formal state: NO-GO**. Review PR #28, GitHub Issue #14 and the production checklist for the still-open blockers.
