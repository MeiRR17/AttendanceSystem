# v0.3.0 — additive SQLite migrations and idempotent event store

**Status: development only. No real student data, production migration, scanner import or WhatsApp.**

## Schema and operational boundary
New schema version 2 introduces SchemaMigrations, Devices, append-only AttendanceEvents, AttendanceDecisions, Schedules and AuditEvents. Existing legacy Students, Attendance and Holidays are preserved. No legacy records are silently converted into new events, and no migration drops or overwrites existing records.

AttendanceEvents fields: server-trusted DeviceID, opaque EventKey (must be stable for retransmissions), pseudonymous StudentID, OccurredAtUtc, ReceivedAtUtc, and generated EventID. Unique (DeviceID, EventKey) provides idempotency. An event-key collision with conflicting student/time fails closed rather than being counted as a duplicate. Enrolment/enabling of devices are trusted administrative operations; the event store refuses unknown/disabled devices. It never enrolls a device on the basis of XML input.

AttendanceDecisions stores derived status separately so raw events remain immutable. Schedules and audit table are foundations only; no decision engine or administrative API is yet connected.

## Migration discipline
- Existing databases are never automatically migrated. InitializeDatabaseAtPath(existingPath) refuses upgrades unless allowExistingUpgrade: true is passed by an authorized upgrade tool or synthetic smoke test.
- Before upgrading an existing DB, code creates a timestamped backup with SQLite's online backup API, then verifies PRAGMA integrity_check. Migration DDL and version markers run in one transaction.
- Even after successful tests, operators must obtain privacy authorization and a separately verified off-host backup/restore rehearsal before upgrading real student records. The local backup is an additional safeguard, not a disaster-recovery strategy.
- Starting the normal application with a loopback legacy gateway does not initiate migrations. The old duplicate WebSocket listener is disabled, and the active loopback adapter rejects TimeLog input; v0.4.0 must connect an authenticated/isolated gateway to this store.
- A version newer than the app is refused, preventing accidental downgrades. Version 2 initialization is idempotent.

## Running the integration smoke suite
On a clean Windows x64 .NET 10 environment:

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-db

The smoke suite creates only disposable synthetic SQLite files under the OS temporary directory. It tests a fresh schema, registered fake device, first-event insert, duplicate, event-key conflict, unknown device, migration idempotency, refusal to change a legacy DB implicitly, authorized synthetic legacy migration, preservation of a synthetic legacy row, and a verified pre-migration backup. It cleans test files afterward. No scanner, external HTTP, Twilio, biometrics, real student data or network connection is involved.

The PR-scoped GitHub Actions build runs the same command on Windows. CI results must be reviewed before merging.

## Still incomplete before v0.3.0 can be called production-quality
- Hardware-generated stable event keys, trusted source-of-time, offline replay, ACK-after-persist and device authentication belong to v0.4.0.
- Real historical DB migration requires authorized review and a sanitized representative fixture; production data must never be accessed by CI.
- Concurrency/load/busy-lock tests, cryptographic access controls, migration rollback rehearsal and multi-process integrity testing need evidence.
- v0.5.0 will implement schedules, holidays and derived attendance decisions; v0.6.0 will implement consent-aware notifications.
- Verify SQLite backup API/runtime behavior across supported platforms; this is not a deployment artifact.

Related: issue #6, and parent v0.2.0 PR #15.
