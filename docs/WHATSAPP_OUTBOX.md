# v0.6.0 — consent-gated WhatsApp outbox (OFFLINE/DRY RUN)

**Pre-production. Not approved for real student records, recipients, credentials, Twilio or WhatsApp transmission.**

## What is implemented
Schema version 3 adds additive, transactional SQLite tables:

- NotificationConsents: pseudonymous RecipientKey and Purpose, IsGranted flag, opaque external EvidenceRef and consent change timestamp. Grant and opt-out are explicit and audited.
- NotificationTemplates: TemplateKey, Purpose, opaque ApprovalEvidenceRef and enabled switch. These are internal approval references, not proof of Meta/WhatsApp/Twilio template approval.
- NotificationOutbox: EventID, RecipientKey, Purpose, TemplateKey, RuleVersion, state/attempt count/next attempt and result metadata. Unique (EventID, RecipientKey, Purpose) blocks duplicate logical requests even if template or rules version changes.
- NotificationAttempts: append-only metadata-only attempt ledger. No real message bodies, student names, phone numbers or Twilio secrets are stored.

The migration preserves old Students, Attendance and Holidays; existing databases still require an explicit authorized, backed-up migration. No production database is automatically upgraded by starting the app.

## Consent and decision gates
NotificationOutboxService.RecordConsent must be called by a trusted institution-authorized integration, with an external evidence reference. This is not a public user endpoint. Actual verification of institutional authority and parent/student consent procedures remains to be implemented with v0.7 RBAC.

NotificationOutboxService.RegisterTemplate similarly records an internal approved-template reference and an enable/disable switch; it does not establish Meta or Twilio approval. An enabled internal template, current explicit consent and a materialized AttendanceDecisions row with exact RuleVersion and status Late are REQUIRED before any late-notification request is enqueued.

No other attendance status automatically qualifies. Duplicate enqueue is a no-op; a per-recipient one-notification-per-24h cap applies to the attendance-late purpose. A revocation or template disable suppresses queued/retry requests immediately; a decision revision suppresses a queued item at dispatch check.

## State machine (simulation only)
- Queued -> Simulated after a dry-run provider result. Simulated NEVER means WhatsApp delivered.
- Queued/Retry -> Retry on a simulated error unambiguously known to occur before transmission. Exponential backoff: 1 min then 2 min; maximum 3 attempts.
- Retry -> DeadLetter after the third safe-to-retry pre-send failure.
- Queued -> DeadLetter on permanent rejection.
- Queued -> Uncertain if a provider attempt may have occurred; NEVER auto-retry an ambiguous outcome, because double-send would be possible.
- Queued/Retry -> Suppressed when consent, template or approved Late decision is withdrawn.

No external message delivery code is provided or enabled, even through a CLI option. Existing direct NotificationService.SendLateNotification is retired and throws NotSupportedException. Neither Twilio credentials nor an actual WhatsApp sender/content-template ID are read by this code. This is deliberately safe-by-default while authorized recipients, vendor approvals, secret storage, delivery-status webhooks, rate/retention policy and real retry semantics are pending. A state named Sent or Delivered is intentionally absent because no network send can occur in this version.

The service exposes an aggregate status-count query (no PII) for future admin review. Actual operator dashboard, phone-number lookup in a protected vault, delivery receipts, authorized recipient preferences and secure provider integration remain future work.

## Offline deterministic verification
Run on Windows/.NET 10:

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-outbox

Tests create only disposable synthetic SQLite databases. They also assert that the retired direct-send API fails closed. They cover unauthorized enqueue (no consent/template), grant, duplicate enqueue, one-recipient 24-hour cap, Simulated terminal state, 3-attempt exponential backoff and dead-letter, consent opt-out, disabled template, revoked Late decision, held ambiguous result (no retry), metadata audit and attendance-event preservation. No outgoing HTTP, WhatsApp, Twilio API, real user phone or real student identity is involved.

## Production blockers
- Verified consent legal basis/identity and RBAC on grants/revocations; guardian authorization as applicable.
- Actual WhatsApp/Meta business-initiated approved template with vendor-verified Twilio ContentSid, sender identity and rate restrictions; secrets must be in supported vault, not Git, plaintext logs or database.
- Authorized per-recipient contact information lookup and secure opt-out pathway; phone validation (E.164), content previews without PII exposure.
- Actual durable worker, provider idempotency/uncertain outcome reconciliation, callback verification, delivery status ledger, secret rotation, global provider quotas and operating alerts.
- Hardware SDK, privacy/public Git-history incident and school deployment approvals for prior stacked versions.

Roadmap: GitHub Issue #9. This PR remains a draft stacked on v0.5.0 PR #18. Do not merge as a production WhatsApp feature or contact real people.
