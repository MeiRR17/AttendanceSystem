# v1.0.0-rc.5 — public legacy database migration disabled

**Production status: NO-GO.** This hardens the actual upgrade entry point and tests it using disposable fake data only. No real attendance database is inspected, migrated, exported or copied.

## Fixed risk

Before rc.5, callers could pass \`allowExistingUpgrade:true\` to public \`DatabaseSetup.InitializeDatabaseAtPath\` and migrate an *existing* old database, creating an **unencrypted** pre-migration SQLite backup. That boolean was not an institutional authorization or a safe substitute for approved encrypted temporary storage, privacy review, data-subject consent, key custody and verified restoration. A structurally valid database is not necessarily legal or approved to process.

The public API now **rejects \`allowExistingUpgrade:true\` unconditionally**. With its default \`false\`, it can create a new SQLite DB or validate a current-schema DB, but it continues to reject old existing schema 0–3. There is no public CLI, API or webhook for upgrading existing private records.

An internal \`DatabaseSetup.InitializeSyntheticFixtureAtPath\` permits existing-db migration solely for the test harness, and requires all of these checks before opening the database:

- \`ATTENDANCE_SYNTHETIC_FIXTURES=true\`, set explicitly within isolated CI test steps.
- A **pre-existing** DB file immediately beneath the OS TEMP folder in a directory whose name matches \`attendance-db-smoke-<32hex>\` or \`attendance-legacy-fixture-<32hex>\`.
- Reject a nested arbitrary directory, an external path, or file/folder links, junctions and NTFS reparse points.
- Continue the rc.4 legacy structural preflight before generating any plaintext backup or executing schema DDL.

This is **test isolation, not real security authentication**: TEMP paths and environment variables are forgeable and do not establish user identity or de-identification. The internal migration is not exposed over an endpoint; no production migration option exists. An actual production migration will need a separately reviewed, permissioned worker and cryptographically verified approval evidence, with encrypted temporary media and a supervised restoration drill.

## Regression checks

- \`DatabaseSmokeChecks\`: public Boolean true returns generic rejection without backup, synthetic internal API rejects absent marker and nested/unapproved directory even if marker is set, and the known TEMP fixture succeeds and creates only one backup.
- \`LegacyProviderCompatibilityChecks\`: two separately generated SQLite 1.x files reject public Boolean bypass; internal scoped fixture migration preserves all **18 synthetic original records**, makes verified pre-upgrade snapshots and deduplicates newly inserted events. Ten malformed-layout variants (five × two fixtures) still fail in the *real* legacy preflight path even though the isolated fixture marker is present.
- Full Windows .NET 10 CI retains NuGet vulnerability gate, SQLite, HMAC bridge, policies/DST, outbox dry-run, JWT RBAC, RTL UI, failure injection, encrypted synthetic backup and source-only NO-GO release evidence.

**Still blocked:** historical Git PII risk, representative sanitized real-world fixture, tenant authorization, Realand SDK/hardware, approved backup key custody, physical 14-day pilot, signed release and live provider. No production tag or master merge is authorized.
