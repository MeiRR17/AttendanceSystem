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
- [Production Go/No-Go checklist](docs/PRODUCTION_CHECKLIST.md)
- [Version and issue tracker](https://github.com/MeiRR17/AttendanceSystem/issues/3)

## Existing critical blockers
- Legacy Attendance INSERT/schema mismatch is bypassed: experimental scanner events are rejected, not written. New append-only AttendanceEvents tables are not connected to live scanner input until v0.4.0.
- Old SuperSocket 1.x SDK binaries are still present in the checkout and have unverified provenance, redistribution rights, modern .NET runtime compatibility and supported OS.
- Attendance rules are now independently configurable (weekdays, slots, overrides, Israel DST), with audited re-evaluation of derived decisions. No automatic absence decisions, live messaging, calendar-to-closure interpretation or institutional policy is shipped. An HMAC-authenticated loopback bridge accepts synthetic/authorized messages and responds only after SQLite commit. Actual Realand firmware is NOT verified to support this contract. LAN device identity, real ACK/reconnect, hardware, privacy, outbox consent, admin UI and production deployment remain open.
- An optional localhost OIDC JWT API has role-protected pseudonymous attendance views and append-only corrections. It is disabled by default, not Windows SSO, and not approved for LAN or real student data. Persistent consent-gated outbox is dry-run only. No real Twilio delivery adapter, recipient vault, admin authorization or actual message sending exists. Git history previously contained an attendance.db artifact. Removing it from current HEAD did not erase old Git history, forks or clones. This public repository must not contain personal records, secrets or biometric data.

Bridge startup is disabled unless explicitly requested with --serve-bridge and externally provisioned credentials. No startup demo WhatsApp send exists. All external messaging remains DISABLED in v0.6.0; only a synthetic dry-run queue and replay-safe state machine are implemented. SQLite smoke checks use only disposable synthetic databases; no production migration runs automatically.

**Do not treat build changes, static review or mock checks as production readiness.**
