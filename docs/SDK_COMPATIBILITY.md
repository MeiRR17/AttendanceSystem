# v0.2.0 — Build, SDK, and environment compatibility

**Release status: IN PROGRESS; not production ready.**

## Technical change
- Project target changed from net8.0 to net10.0, supported LTS. Supported SDK selection is specified in root global.json.
- Former absolute Windows Downloads HintPath references now resolve the existing repository's Dependancies/*.dll files (not newly downloaded, recopied or relicensed).
- Newtonsoft.Json is declared as an explicit NuGet dependency, avoiding a DLL absent from the checkout. Existing NuGet dependency versions remain pinned.
- Console runtime has safe default (no network listener, no DB modification, no WhatsApp). The legacy listener requires an explicit acknowledgement.
- SuperSocket listener is constrained to 127.0.0.1, not remotely accessible without subsequent redesign.
- On socket startup failure, fail closed and exit. Ctrl+C stops the legacy server; input parser bounds message length and refuses DTD/XXE.
- Manual workflow_dispatch GitHub Actions build on Windows with offline configuration smoke cases. Intentionally no automatic CI run, conserving quota.

## Commands
Use a **clean clone** with the .NET 10 SDK on a Windows x64 host; do not connect a scanner or real student DB:

    dotnet --info
    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --self-test
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --check-config
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release

The last command should exit immediately in safe mode, without opening a network port or creating an attendance database. A manual GitHub Actions workflow is available under Actions > Manual .NET 10 build and offline smoke checks (workflow_dispatch); it is not automatically started.

## Dangerous legacy gateway — offline testing only
The following values deliberately permit running the 2018 SDK on loopback, not a trusted production service:

    ATTENDANCE_ENABLE_LEGACY_GATEWAY=true
    ATTENDANCE_LEGACY_PORT=2012
    ATTENDANCE_LEGACY_RISK_ACK=I_UNDERSTAND_LEGACY_GATEWAY_IS_UNAUTHENTICATED

On PowerShell, set values using $env:VARIABLE = "value"; do not add the acknowledgement to repository files, environments or CI. No external device should be connected. This opt-in is for controlled local development only.

## Known blockers and release gates
1. **No build performed in this implementation environment:** .NET CLI is absent and network installation is unavailable. Automated workflow is deliberately manual. The net10 target and legacy framework DLL compatibility remain unverified; if build fails, collect the full non-PII error and fix it before merging.
2. **Legacy DLL provenance/licensing unknown:** existing SuperSocket 1.x/log4net DLLs are already in Git; this change only rewires references. Owner must validate binaries, SHA-256 checksums, redistribution rights and exact supported architectures; prefer replacing them with a supported SDK or isolated Windows gateway. Do not claim .NET 10 runtime support from a successful compile alone.
3. **No real hardware validation:** precise Realand model/firmware, handshake, ACK/reconnect behavior, API and LAN constraints remain unverified.
4. **Data correctness unresolved:** Attendance INSERT does not match current schema. v0.3.0 must repair it before ingestion can be enabled with real users.
5. **Biometric/privacy gate open:** public Git history previously contained a student DB. Handling requires authorized security/privacy assessment and decision on repository visibility.
6. **Legacy protocol still unauthenticated:** even with loopback bound, test mode is not secure for production. v0.4.0 must provide network device identity / constrained gateway and verified ingestion semantics.
7. **This is not a signed release:** no hardware tests, staged installation, production data, notifications, or executable artifacts are shipped.

## Acceptance evidence required to close issue #5
- [ ] Clean checkout Windows x64 Restore/Build on .NET 10 SDK, logs retained with no secrets.
- [ ] --self-test, --check-config and safe default startup succeed.
- [ ] Confirm exact vendor SDK/Realand model and approved binary provenance, file hashes, licensing.
- [ ] Prove SuperSocket components run without .NET Framework-only runtime failures; if incompatible, isolate or replace behind gateway.
- [ ] Complete dependency vulnerability/license review and baseline security sign-off.
- [ ] Document official supported host environment, upgrade/rollback and actual machine test evidence.
- [ ] Review changes and only then merge to master. Do not promote based on static source review alone.

Evidence intentionally pending as of 2026-11-09.
