# AttendanceSystem

**PRE-PRODUCTION / NOT SAFE FOR REAL STUDENT DATA.**

C# attendance prototype for Realand fingerprint-device events over WebSocket, SQLite, Hebcal calendar rules and Twilio notifications. The code now targets .NET 10 LTS, but build/runtime compatibility of the 2018 device DLLs has not been verified. In particular, no actual attendance import, messaging or network service is enabled by default.

## Build and safe development
Use a clean Windows x64 machine with the .NET 10 SDK (see global.json). Start without a scanner or production database.

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --check-config
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release

The last command exits in safe mode: no listener, DB creation, scanner access, or Twilio message. To explore the unauthenticated loopback-only legacy gateway in an isolated local environment, follow docs/SDK_COMPATIBILITY.md. Never enable it with real devices/students.

A scoped GitHub Actions pull-request workflow provides a clean Windows .NET 10 build and safe smoke checks when available; it can consume CI minutes. A separate manual dispatch trigger is defined for later use when the workflow resides on the default branch. No CI success is claimed.

## Project documentation
- [Full Hebrew production roadmap](docs/PRODUCTION_ROADMAP.he.md)
- [Baseline code/security audit](docs/BASELINE_AUDIT.md)
- [v0.2.0 build, SDK, configuration, limits and verification](docs/SDK_COMPATIBILITY.md)
- [Production Go/No-Go checklist](docs/PRODUCTION_CHECKLIST.md)
- [Version and issue tracker](https://github.com/MeiRR17/AttendanceSystem/issues/3)

## Existing critical blockers
- SQLite schema does not match attendance INSERT; v0.3.0 migration is required.
- Old SuperSocket 1.x SDK binaries are still present in the checkout and have unverified provenance, redistribution rights, modern .NET runtime compatibility and supported OS.
- Device identity, event ACK/replay semantics, access controls, privacy/biometrics review, outbox consent, reliable tests, admin UI, backup and production deployment remain open.
- Git history previously contained an attendance.db artifact. Removing it from current HEAD did not erase old Git history, forks or clones. This public repository must not contain personal records, secrets or biometric data.

No startup demo WhatsApp send exists. All external messaging remains outside the safe runtime path until v0.6.0.

**Do not treat build changes, static review or mock checks as production readiness.**
