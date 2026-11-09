# v0.7.0 — OIDC JWT, role policies and append-only corrections

**Development only. No real user, real student data, Windows SSO or production identity provider was connected.**

## Opt-in local configuration

    ATTENDANCE_ADMIN_API_ENABLED=true
    ATTENDANCE_API_OIDC_AUTHORITY=https://idp.example.invalid/realm
    ATTENDANCE_API_OIDC_AUDIENCE=attendance-admin-api
    ATTENDANCE_DB_PATH=<authorized existing SQLite schema-v4 file>
    ATTENDANCE_API_PORT=21813

    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release -- --serve-admin-api

The API is off at default startup, requires explicitly enabled configuration and never migrates/creates databases on server startup. It binds IPv4 loopback only. It uses ASP.NET Core JWT bearer validation against a configured HTTPS OpenID Connect authority, exact audience, issuer, signature and expiry. There is no password store, login or user-provisioning endpoint. The provider must issue verifiable JWTs containing role claims named role (Administrator, Staff or Viewer) and a sub for correction authorship. No arbitrary role headers, anonymous corrections or cookie sessions are trusted.

This is **OIDC JWT resource-server validation**, not an implementation of Windows integrated SSO or a verified enterprise IdP. The IdP, production token claims, automatic discovery and real hardware remain untested. No LAN exposure or reverse proxy is authorized; no permissive CORS is configured. Security response headers include no-store, nosniff, restrictive CSP and no-referrer.

## API v1 endpoint and permission table

| Endpoint | Roles | Result |
| --- | --- | --- |
| GET /api/v1/health/live | Anonymous | Minimal health boolean |
| GET /api/v1/overview | Viewer, Staff, Administrator | Aggregate counts only |
| GET /api/v1/me | Viewer, Staff, Administrator | Validated role names (no user subject) |
| GET /api/v1/events?afterId=0&limit=20 | Staff, Administrator | Opaque EventId, UTC timestamp, effective status, revision |
| GET /api/v1/events/{eventId} | Staff, Administrator | The same no-name/no-phone fields |
| POST /api/v1/events/{eventId}/corrections | Staff, Administrator | Optimistic append-only correction |
| GET /api/v1/events/{eventId}/corrections | Administrator only | Protected before/after, reason code, actor hash and time |
| GET /api/v1/exports | Administrator only | HTTP 501; export deliberately not implemented |

Cursor limit is 1–100; invalid pagination returns 400. Anonymous reads of the overview return 401 and Viewer access to events, corrections and exports returns 403. The API does not expose the raw Students table, name, phone, biometric payload or recipient records.

Example synthetic JSON POST for a user with a verified Staff or Administrator role:

    {"expectedRevision":0,"newStatus":"Excused","reasonCode":"approved-excuse"}

Allowed manual statuses: OnTime, Late, Excused, Invalid, Unclassified; manual Absent is withheld pending an authorized roster workflow. Reason codes: clock-skew, approved-excuse, duplicate-scan, manual-verification, administrative-correction. Free-text names and reasons are refused. Accepted correction returns 201, stale/duplicate edit 409, missing event 404, invalid status/reason 422.

## SQLite v4

An additive transaction creates AttendanceCorrections(EventID, Revision, BeforeStatus, AfterStatus, ReasonCode, ActorHash, RecordedAtUtc), uniquely indexed on (EventID,Revision). A mutation checks expectedRevision, writes append-only correction and a corresponding AuditEvents marker in one transaction, hashes actor ID before storage, and preserves the original AttendanceEvents and rule-derived AttendanceDecisions unchanged. Effective status reads the most recent correction, without silently overwriting revisions.

Existing databases require explicitly authorized, backed-up migration; there is no implicit schema upgrade. Real student records must never enter CI or this repository.

## Offline checks

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-api

The integration test creates an ephemeral SQLite database and signed **synthetic** JWTs, exercises a real loopback HTTP listener and verifies anonymous/Viewer/Staff/Administrator access, protected audit and disabled exports, bounded pagination, no student identifiers in API projections, revision 409 conflicts, 422 validation, append-only correction/history and unchanged source records. No external identity provider, Twilio, hardware, biometrics or real accounts are used.

## Still blocked before production

- Enterprise IdP configuration, signed verified role claims, per-institution provisioning, MFA/session lifecycle and verified authentication threat model.
- Fine-grained Staff access to individual students, tenant scoping and real consent/privacy authorization. No generic PII or export endpoint exists yet.
- Approved TLS edge, reverse proxy and network restrictions; localhost HTTP is never a public service.
- Guardian data rights, retention/export/deletion, anti-CSRF if a cookie-based browser frontend is later added, security monitoring and operator review.
- Authorized Realand physical-device/protocol review and public repository historical data assessment (issue #4).
- Notification permissions, sender approval and real WhatsApp delivery remain disabled.

Tracking: Issue #10. PR is stacked on v0.6.0 draft #19. CI green alone is not production sign-off.
