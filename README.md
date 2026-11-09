# AttendanceSystem

**PRE-PRODUCTION / NOT SAFE FOR REAL STUDENT DATA.**

C# attendance prototype for Realand fingerprint-device events over WebSocket, SQLite, Hebcal calendar rules and Twilio notifications. The code now targets .NET 10 LTS, but build/runtime compatibility of the 2018 device DLLs has not been verified. In particular, no actual attendance import, messaging or network service is enabled by default.

## Build and safe development
Use a clean Windows x64 machine with the .NET 10 SDK (see global.json). Start without a scanner or production database.

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-db
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-bridge
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-rules
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-outbox
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-api
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-ui
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-faults
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-snapshots
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-pilot
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test-release
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --assess-release
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --check-config
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release

The last command exits in safe mode: no listener, DB creation, scanner access, or Twilio message.

To preview the interactive synthetic Hebrew RTL UI (no database, JWT or hardware):
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --preview-ui
Open http://127.0.0.1:21814/app on that machine. This preview only binds localhost, not a production network service. To explore the unauthenticated loopback-only legacy gateway in an isolated local environment, follow docs/SDK_COMPATIBILITY.md. Never enable it with real devices/students.

A scoped GitHub Actions pull-request workflow provides a clean Windows .NET 10 build and safe smoke checks when available; it can consume CI minutes. A separate manual dispatch trigger is defined for later use when the workflow resides on the default branch. No CI success is claimed.

## Project documentation
- [Full Hebrew production roadmap](docs/PRODUCTION_ROADMAP.he.md)
- [Baseline code/security audit](docs/BASELINE_AUDIT.md)
- [v0.2.0 build, SDK, configuration, limits and verification](docs/SDK_COMPATIBILITY.md)
- [v0.3.0 SQLite migrations and synthetic tests](docs/DATA_MIGRATIONS.md)
- [v0.4.0 authenticated localhost bridge and security model](docs/BRIDGE_PROTOCOL.md)
- [v0.5.0 configurable attendance policies, Israel calendar and audit](docs/ATTENDANCE_RULES.md)
- [v0.6.0 consent-gated dry-run WhatsApp outbox](docs/WHATSAPP_OUTBOX.md)
- [v0.7.0 OIDC bearer API, role policies and append-only corrections](docs/ADMIN_API_SECURITY.md)
- [v0.8.0 Hebrew RTL admin and local synthetic preview](docs/ADMIN_UI.md)
- [v0.9.0 operational health, metrics and offline fault campaign](docs/OPERATIONS_AND_FAULTS.md)
- [v0.10.0 controlled synthetic staging, encrypted backup/restore and pilot gates](docs/STAGING_AND_PILOT.md)
- [14-day pilot evidence template — no PII](docs/PILOT_DAY_TEMPLATE.md)
- [v1.0.0 release candidate review and immutable source-only evidence](docs/RELEASE_READINESS.md)
- [v1.0.0-rc.1 NuGet supply-chain reduction and fail-closed audit](docs/NUGET_SUPPLY_CHAIN.md)
- [v1.0.0-rc.2 SQLite 2.0.4 provider migration and regression evidence](docs/SQLITE_PROVIDER_V2.md)
- [v1.0.0-rc.3 legacy-provider 1.x file compatibility and data preservation](docs/SQLITE_V1_COMPATIBILITY.md)
- [v1.0.0-rc.4 fail-closed schema preflight and negative migration controls](docs/SQLITE_MIGRATION_PREFLIGHT.md)
- [v1.0.0-rc.5 public upgrade blocked, synthetic-only isolated migration](docs/SQLITE_PUBLIC_UPGRADE_GATE.md)
- [v1.0.0-rc.6 clone-only legacy migration recovery rehearsal and NO-GO evidence](docs/SQLITE_MIGRATION_REHEARSAL.md)
- [v1.0.0-rc.7 dual-signed synthetic migration reviews (still NO-GO)](docs/SQLITE_SYNTHETIC_SIGNED_REVIEWS.md)
- [v1.0.0-rc.8 synthetic reviewer key revocation and persistent replay tests](docs/SQLITE_REVIEWER_TRUST_AND_REPLAY.md)
- [v1.0.0-rc.9 offline-root-signed synthetic trust policy and epoch rollback guard](docs/SQLITE_OFFLINE_ROOT_TRUST.md)
- [v1.0.0-rc.10 detached ECDSA-signed synthetic audit checkpoints and tamper/rollback detection](docs/SQLITE_DETACHED_AUDIT_CHECKPOINTS.md)
- [NO-GO change-control and support handoff template](docs/RELEASE_CHANGE_TEMPLATE.md)
- [Production Go/No-Go checklist](docs/PRODUCTION_CHECKLIST.md)
- [Version and issue tracker](https://github.com/MeiRR17/AttendanceSystem/issues/3)

Operational telemetry v0.9.0 adds loopback readiness (public coarse boolean), Administrator-only aggregate metrics and SQLite lock/rollback fault checks. Real device connectivity, alert delivery and pilot SLOs remain unverified.

v0.10.0 includes a **synthetic-only**, internal AES-256-GCM snapshot/restore test foundation and a read-only pilot evidence validator that **cannot authorize production GO**. No secure Windows backup ACL/secret custody/offsite automation, physical device pilot or real data protection is approved. Run only isolated synthetic tests and reviewed staging preflight.

v1.0.0-rc.1 removes unused Twilio and SQLite EF6 meta-package dependencies and adds a transitive NuGet security audit gate; v1.0.0-rc.2 switches to System.Data.SQLite 2.0.4 and pins native SourceGear.sqlite3 3.53.4. v1.0.0-rc.3 adds an isolated SQLite.Core 1.0.119 generator and a fail-closed 1.x-to-v2 offline data-preservation test. v1.0.0-rc.4 rejects unknown legacy columns, triggers, orphaned references and unexpected schema objects BEFORE backup/DDL, even when an explicit upgrade flag is supplied. Neither uses real databases or authorizes deployment. v1.0.0 release candidate governance produces an **unsigned, source-only checksum/evidence bundle** for exact PR SHA, a direct-NuGet inventory and a formal NO-GO blocker matrix. It is **not a signed executable, complete SBOM, published release, or production authorization**. User-approved production deployment remains blocked by historical privacy, SDK/device, key custody, backup, pilot, identity and operator approvals.

## Existing critical blockers
- Legacy Attendance INSERT/schema mismatch is bypassed: experimental scanner events are rejected, not written. New append-only AttendanceEvents tables are not connected to live scanner input until v0.4.0.
- Old SuperSocket 1.x SDK binaries are still present in the checkout and have unverified provenance, redistribution rights, modern .NET runtime compatibility and supported OS.
- Attendance rules are now independently configurable (weekdays, slots, overrides, Israel DST), with audited re-evaluation of derived decisions. No automatic absence decisions, live messaging, calendar-to-closure interpretation or institutional policy is shipped. An HMAC-authenticated loopback bridge accepts synthetic/authorized messages and responds only after SQLite commit. Actual Realand firmware is NOT verified to support this contract. LAN device identity, real ACK/reconnect, hardware, privacy, outbox consent, admin UI and production deployment remain open.
- An optional localhost OIDC JWT API has role-protected pseudonymous attendance views and append-only corrections. It is disabled by default, not Windows SSO, and not approved for LAN or real student data. Persistent consent-gated outbox is dry-run only. No real Twilio delivery adapter, recipient vault, admin authorization or actual message sending exists. Git history previously contained an attendance.db artifact. Removing it from current HEAD did not erase old Git history, forks or clones. This public repository must not contain personal records, secrets or biometric data.

Bridge startup is disabled unless explicitly requested with --serve-bridge and externally provisioned credentials. No startup demo WhatsApp send exists. All external messaging remains DISABLED in v0.6.0; only a synthetic dry-run queue and replay-safe state machine are implemented. SQLite smoke checks use only disposable synthetic databases; no production migration runs automatically.

**Do not treat build changes, static review or mock checks as production readiness.**
