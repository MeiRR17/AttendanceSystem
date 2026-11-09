# v1.0.0-rc.2 — SQLite ADO.NET provider 2.0.4 migration

**Engineering-only, stacked draft PR. The release remains NO-GO. No real student database is opened or changed.**

## Verified package release information

The \`System.Data.SQLite.Core\` package only publishes through 1.0.119, whereas the active 2.x ADO.NET provider is distributed as \`System.Data.SQLite\`, whose official NuGet 2.0.4 release was published on 4 August 2026. The **2.0.4** .NETStandard package advertises **no direct dependencies** (no EF6), unlike the old 1.0.119 metapackage. See:

- https://www.nuget.org/packages/System.Data.SQLite/2.0.4
- https://www.nuget.org/packages/System.Data.SQLite.Core/1.0.119
- https://system.data.sqlite.org/home/doc/trunk/www/news.md

The vendor's 2.0.1 migration notes describe replacement of the previous \`SQLite.Interop.dll\` C++/CLI bridge with native **P/Invoke** interop. Thus a compile is insufficient to establish runtime compatibility or safe native deployment.

## Actual source changes

- Pin the single direct ADO.NET package \`System.Data.SQLite\` to \`2.0.4\`, replacing \`System.Data.SQLite.Core 1.0.119\`. No schema, roster, notification, API or hardware semantics are intentionally changed.
- Continue strict NuGet security auditing with all direct/transitive references and fail-closed minimum **moderate** advisory policy. \`NU1900, NU1902, NU1903, NU1904, NU1905\` remain build/restore errors.
- Update the post-restore \`project.assets.json\` allow/deny script to require exactly one \`System.Data.SQLite/2.0.4\`, and prohibit the old \`System.Data.SQLite.Core\`, EF6, \`EntityFramework\`, vulnerable \`System.Data.SqlClient\`/\`System.Drawing.Common\`, and unused Twilio.
- Keep clean Windows .NET 10 Restore, Release Build and all existing offline synthetic test suites as required acceptance gates. Especially important: SQLite v4 schema migration, idempotent events/replays, unique constraints, FK enforcement, transactions, WAL-safe backup/restore, AES-GCM restoration, lock contention, rules, outbox, authenticated API and safe startup.
- The exact pinned package, source commit and resolved NuGet dependency inventory are recorded in the short-lived unsigned Actions artifacts. Neither constitutes a signed complete software bill of materials nor independent supply-chain verification.

## Migration / rollback policy

- **Never perform an implicit real DB migration.** This branch runs on disposable synthetic SQLite files only. Any future controlled pilot must create a separately authorized encrypted backup with secure temporary storage, preserve original historical records and recover by validated rollback.
- SQLite database file format is distinct from the ADO.NET provider binary version; compatibility is *expected*, not proven against any user's live database. Backward opening must be checked on approved, de-identified, verifiably consented fixture snapshots separately.
- A new provider uses native P/Invoke; confirm the target Windows x64 native library loading and deployment on an actual isolated target host. CI's Windows x64 host is a partial check, not Realand runtime validation.
- Pin 2.0.4 without speculative updates. Revert by restoring the parent branch/commit from a safe Git ref rather than running another provider over a real DB.
- Re-run release blocker assessment for historical Git exposure, license/provenance and checked-in legacy SuperSocket/log4net DLLs, guardian consent, institution OIDC authorization, secure backups, physical hardware, real 14-day pilot and owner sign-off.

## Acceptance evidence

CI run on this **exact commit** must show:
1. \`dotnet restore\` succeeds with no moderate/higher NuGet advisory or audit metadata errors.
2. Release build is successful on Windows 2022 / .NET 10 x64 and resolves native SQLite 2.x.
3. Resolved graph contains System.Data.SQLite 2.0.4, not the Core 1.x package, EF6/SqlClient/Drawing/Twilio.
4. All existing synthetic SQLite, transactions, authenticated bridge, fault injection and snapshot restore smoke tests pass.
5. No real device connection, real WhatsApp sending, private student data or automatic production Release/tag is introduced.

**Production decision: NO-GO** regardless of synthetic results.
