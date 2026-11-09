# AttendanceSystem — Baseline audit (2026-11-09)

Repository: MeiRR17/AttendanceSystem, default branch master, baseline commit 6edea2e70750c918e1a545b54f67a11263b607ba. Review based on source/static inspection, **not** a successful build, live scanner run, SQL migration, or end-to-end delivery.

## Confirmed findings

| ID | Priority | Source | Observation | Required remediation |
| --- | --- | --- | --- | --- |
| AS-001 | P0 | Database/DatabaseSetup.cs vs SDK/RealandSDK.cs and WebSocket/WebSocketServerSetup.cs | INSERT refers to Name/PhoneNumber/Status absent from Attendance schema | Versioned schema + migration + insertion tests |
| AS-002 | P0 | Program.cs | Main sent a sample Twilio notification on startup with personally identifying example inputs | Remove unconditional sample side effect and clear committed PII |
| AS-003 | P0 | Public repository history, merged PR #1 | Previous commits contained an attendance.db under build outputs; removal from HEAD is not deletion from history/forks | Authorized privacy incident assessment, access/retention/remediation plan |
| AS-004 | P0 | AttendanceSystem.csproj | Vendored legacy SuperSocket DLLs referenced from local Downloads path, unclear portability and vendor provenance | Reproducible dependency strategy, license/compatibility validation |
| AS-005 | P0 | SDK/RealandSDK.cs, WebSocket/WebSocketServerSetup.cs | Message parsing without schema/trust boundary; raw XML logged by one implementation; duplicate ingest paths | Validate and authenticate devices, disable DTD/XXE, redact logs, unified adapter |
| AS-006 | P0 | AttendanceSystem.csproj | Target is net8.0 with upstream support ending 2026-11-10 | Migrate to supported .NET 10 LTS after dependency compatibility checks |
| AS-007 | P1 | Services/HolidayChecker.cs | Friday/Saturday/**Sunday** hard-coded as no-attendance; not configurable | Institutional schedule and timezone-aware rules |
| AS-008 | P1 | Services/HolidayFetcher.cs | Hebcal call uses hardcoded flags; no timeout/cache/Israel policy, external dependency on every event | Calendar service, policy classification, fallback |
| AS-009 | P1 | Services/NotificationService.cs | Placeholder credentials and sandbox From; business-initiated WhatsApp free text may violate template rules | Secrets, consent, approved templates, outbox, dry run |
| AS-010 | P1 | Entire repository | No tests, CI workflow, auth, administrative UX, deployment/backup runbook in inspected tree | Implement release gates from roadmap |
| AS-011 | P1 | README.md | Legacy .NET Framework prerequisite and direct-in-source credential advice conflict with project config and security | Replace onboarding guidance |
| AS-012 | P1 | SDK/RealandSDK.cs | async void storage handler with uncontrolled exception handling; no dedup/transaction boundary | Awaitable pipeline, idempotency and error policy |

## Evidence boundaries
- Static source references above are based on files present on 2026-11-09.
- No claim that external hardware, CI, Twilio sandbox, .NET build, or network connectivity has been validated.
- Do not publish any student name, phone number, identity number, fingerprint, raw scanner XML, or database data in issues, traces, or screenshots.
- Privacy exposure in historical commits may remain even if repository visibility changes. Do not rewrite Git history or delete audit evidence without an approved response plan.
- A legacy .NET binary is not automatically safe or compatible merely because a build succeeded.

## Immediate next priorities
1. Land v0.1.1 safety baseline.
2. Assess access to Git history/old database under authorization; determine if visibility should be private and whether notification/incident procedures apply.
3. Confirm scanner model/firmware and licensing, then land v0.2.0 reproducible build.
4. Fix AS-001 before accepting real attendance events.
