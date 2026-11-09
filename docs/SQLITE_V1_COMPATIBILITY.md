# AttendanceSystem v1.0.0-rc.3 — SQLite 1.x → 2.x file compatibility

**Engineering test only; production decision remains NO-GO.** This branch introduces no automatic migration and never opens, copies, reads, or uploads any real student database. The old SQLite ADO.NET package is referenced **only from an isolated test-only generator project**, never from AttendanceSystem.csproj or AttendanceSystem.sln.

## What the two-provider test actually does

1. Windows .NET 10 GitHub Actions runs \`tests/LegacyProviderFixture/LegacyProviderFixture.csproj\` in a separate process. Its **only NuGet provider** is \`System.Data.SQLite.Core 1.0.119\`. The program requires \`ATTENDANCE_SYNTHETIC_FIXTURES=true\` and a freshly empty, dedicated directory inside the OS temporary directory. It checks the provider assembly major version is 1.x.
2. This *old provider* creates two **actual SQLite files**, not SQL mocks:
   - \`legacy-unversioned.db\` with the old Students/Attendance/Holidays tables and no SchemaMigrations.
   - \`legacy-schema-v1.db\` with the same old tables plus an explicit SchemaMigrations version 1.
   Each file uses only fixed synthetic identifiers, clearly fictional phone markers, a Hebrew text value, apostrophe, NULL fields, dates, recorded presence/late status and WAL mode. **No real guardian/biometric/phone information exists.** There are four fake Students, three Attendance and two Holidays rows per file.
3. The generator closes SQLite and emits a receipt containing provider name/version, fixture schema, counts and SHA-256 file hashes. The test process using the **upgraded** \`System.Data.SQLite 2.0.4\` with native \`SourceGear.sqlite3 3.53.4\` validates receipt, verifies the files are in its dedicated temp folder and reads every field.
4. It computes a canonical SHA-256 digest of all legacy rows including Hebrew, NULLs, punctuation and status columns; confirms initial schema 0/1 and SQLite integrity.
5. Calling \`InitializeDatabaseAtPath\` **without explicit upgrade authorization must fail**, while all legacy rows remain byte-equivalent by field value.
6. With opt-in on these vetted fake fixtures only, it creates one pre-migration **SQLite online backup** and upgrades to schema version 4. Legacy rows and canonical digests remain identical, every pre-migration backup reopens with the **old schema** and source content, and the migrated file has no foreign-key violations.
7. It inserts one additional synthetic event using the v2 provider, replays it and confirms deduplication; reopens after a second no-op migration and verifies that none of the original tables was rewritten.
8. All generated databases, WAL sidecars, backup files and receipts live under temporary storage and are deleted in a \`finally\` block, never committed as binary fixtures or uploaded as GitHub artifacts.

The v1.x package exists only in the generator's own \`.csproj\` outside the solution and is not shipped as an application dependency. CI runs the old and new providers in **separate processes**, avoiding mixed-version assembly resolution.

## Verification

On an isolated Windows x64 machine with .NET 10 and no actual school records:

    $dir = Join-Path ([IO.Path]::GetTempPath()) ("attendance-legacy-fixture-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $dir | Out-Null
    try {
        $env:ATTENDANCE_SYNTHETIC_FIXTURES = "true"
        $env:ATTENDANCE_LEGACY_FIXTURE_DIR = $dir
        dotnet run --project tests/LegacyProviderFixture/LegacyProviderFixture.csproj -c Release -- "$dir"
        dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-legacy-compat
    } finally {
        Remove-Item Env:ATTENDANCE_SYNTHETIC_FIXTURES -ErrorAction SilentlyContinue
        Remove-Item Env:ATTENDANCE_LEGACY_FIXTURE_DIR -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $dir -Recurse -Force
    }

The workflow runs this directly after the normal SQLite migration smoke check, before the authenticated gateway, rule engine, outbox, RBAC, native failure-injection, backup/restore and RC governance checks. It does not store the generated SQLite files as GitHub artifacts.

## What the test cannot prove

- The *actual* historical attendance.db in a public Git history might have different columns, invalid data, a non-UTF8 encoding, damaged pages, unknown migrations or private records. It was **not accessed**. Before any approved migration, the institution's privacy owner must review that history and authorize a separately sanitized and representative fixture.
- Neither automatic conversion nor a direct production rollback procedure is enabled. The existing migration's unencrypted pre-migration \`.bak\` is acceptable **only for fake disposable data**. A real process needs secure temporary storage, verified key custody/offsite copies and operator sign-off.
- The package v1.0.119 may be obsolete and is used solely to construct controlled test files; this is not an endorsement of the old package as an application runtime. Main build still uses v2 only.
- These checks do not establish physical Realand protocol/SDK provenance, real OIDC/RBAC, guardian consent, private CSV exports, the two-week pilot, measured RPO/RTO or signed owner approvals.

**Final release decision: NO-GO.** No master merge or v1.0.0 tag follows from this test.
