# AttendanceSystem

**Status: pre-production prototype. Do not deploy with real student data or connect a production scanner until the security and production readiness gates are complete.**

A C# attendance prototype integrating a Realand fingerprint attendance device over WebSocket, SQLite storage, Hebrew-calendar lookups (Hebcal), and WhatsApp notifications (Twilio). The repository currently targets .NET 8; a migration to supported .NET 10 LTS is planned. The old README mentioned .NET Framework 4.7.2, which does not match the project file.

## Project tracking
- [Full versioned production roadmap (Hebrew)](docs/PRODUCTION_ROADMAP.he.md)
- [Source-based audit and risks](docs/BASELINE_AUDIT.md)
- [Production go/no-go checklist](docs/PRODUCTION_CHECKLIST.md)

## Known blockers
The existing SQL INSERT does not match the created Attendance table. The project also references legacy local Windows SDK paths that prevent guaranteeing a clean build. Device authentication, input validation, idempotent ingest, consent management, automated testing, backups, and an administrator UI are not implemented to production standards.

**Privacy advisory:** A historical build artifact included an attendance database and was removed from the current branch in PR #1; it may remain retrievable from historical Git objects or clones. Do not upload real student data, files, credentials, scanner payloads, or biometric templates to this repository. Review privacy obligations and Git history under appropriate authorization.

## Development safety
No startup sample WhatsApp message is sent after the v0.1.1 safety change. Real messaging must remain disabled unless configured through an explicit authorized environment and the approved messaging workflow delivered in later roadmap versions. The current WebSocket example is not hardened and is not suitable for untrusted networks.

For reviewing the code without attaching production devices, inspect the solution (AttendanceSystem.sln) and the files in AttendanceSystem/. Build/runtime behavior is **unverified** until the v0.2.0 build gate is implemented. Do not install real secrets into source files.

## Historical context
Previous instructions in this README mentioned directly editing Twilio credentials in source and using a scheduled Task Scheduler process. Those instructions are deliberately removed pending a secure configuration and supported service-hosting model.
