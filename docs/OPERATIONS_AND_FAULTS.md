# v0.9.0 — Operational metrics and synthetic fault campaign

Status: **pre-production**, no physical devices, no student records, no phone numbers, no outbound messages. All prior stacked PRs remain drafts.

## Implemented
- In-process .NET Meter counters for inserted events, exact duplicate replay, rejected requests and SQLite write errors. Bounded 512-sample persistence-duration histogram calculates a provisional p95 in milliseconds. Counters restart with the process; values are never durable service-level measurements. No event IDs, device IDs, students, IPs, phone numbers, tokens or XML contents enter metric labels or logs.
- The authenticated localhost bridge records insert/replay metrics ONLY after SQLite returns a committed result. Rejections and database-write exceptions have separate counters, and SQLite failures continue to return HTTP 503, never an acknowledgement.
- OperationalHealth.Check over an **existing** approved SQLite schema v4 file checks version and PRAGMA quick_check(1), and safely aggregates dry-run notification state: queued/retry, uncertain, dead-letter. No DB creation or migration occurs.
- GET /api/v1/health/live remains anonymous liveness. GET /api/v1/health/ready returns ONLY {"ready":true/false} with HTTP 200/503. GET /api/v1/ops/status requires Administrator JWT; anonymous/Viewer/Staff cannot read it. It returns coarse metric snapshots, in-process sample count, outbox totals, alert codes, and deviceConnectivity="not_verified". API remains localhost-only.
- Hebrew RTL admin's system screen displays real operational aggregates only to an authenticated Administrator; synthetic preview explicitly states that no actual monitoring exists.

## Synthetic CI fault scenarios

Run on a clean .NET 10 Windows machine:

    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-faults

All tests create disposable synthetic data. An intentional SQLite lock may last up to the configured 5-second busy timeout. The checks include:
1. Missing DB: false readiness, no file creation.
2. Corrupt DB: false readiness, no SQL or filesystem detail in response.
3. Transaction interrupted before commit: rollback, zero stored records.
4. Signed retry after rollback: exactly one committed event, subsequent replay deduplicated.
5. Actual simultaneous SQLite connections with write lock: write fails without ACK; after lock release, same key succeeds once.
6. Thirty synthetic dry-run queued notifications: backlog warning, DB readiness remains true.
7. One ambiguous and one dead-letter outcome: explicit warnings requesting manual reconciliation.
8. In-process counters, provisional p95 and “device not verified” classification.
9. API authorization for operational details and boolean-only anonymous readiness; old suites continue to cover auth, XML, outbox, database and UI.

### Thresholds (initial demonstration, NOT approved operational policy)

| Alert code | Trigger | Response |
|---|---|---|
| database_not_ready | missing, mismatched, corrupt or inaccessible DB | stop ingestion, preserve evidence, investigate local backups |
| dry_run_outbox_backlog | >=25 Queued/Retry | review simulated queue, consent, configuration |
| ambiguous_delivery_requires_manual_review | >=1 Uncertain | DO NOT auto-retry a possibly delivered message |
| dead_letter_requires_manual_review | >=1 DeadLetter | review redacted ledger, never assume delivery |
| database_write_errors_observed | >=1 process-local DB write error | investigate locks, storage/permissions and replay audit |

Alerts are reported **in the Admin API response only**. No pager, email, Twilio message, external metrics collector, Prometheus scrape endpoint or notification/webhook is configured.

## Pilot objectives are not achieved or measured here

The roadmap sets ≥99.5% correctly captured accepted events, p95 committed event latency below five seconds and zero lost acknowledged events. CI synthetic timings or provider-free counters cannot establish these real-world outcomes. Real hardware, independent ground truth, vendor reconnect/ACK verification, staging tests and approved observability retention are required. The interrupted transaction is a simulated crash by disposal, **not a power-loss validation**.

## Unresolved release gates

Physical Realand hardware authentication and ACK; enterprise SSO; per-student authorization; historical public attendance.db privacy response; signed/secured backups and restore drills; retention/redaction; manual accessibility review; live provider receipts, cross-process metrics, durable paging and true deployment monitoring. Keep the draft PR and issue #12 open pending those gates.
