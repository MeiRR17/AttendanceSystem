# v0.10.0 — controlled staging foundation, encrypted snapshots, restoration and pilot evidence

**Development only.** This change does not deploy, authorize actual students, connect Realand scanners, enable WhatsApp, set TLS, grant a real OIDC identity, or declare production readiness. It is stacked on draft v0.9.0. No real database or service account was accessed.

## Synthetic staging workflow

1. Review the previous release SHA, .NET 10 runtime, dependency/legacy SDK license and current unmerged issue chain. Use a dedicated Windows x64/Windows Server test host with **no access** to a real school network or scanner. Do not publish the current localhost-only API to LAN.
2. Isolate test data and credentials. Create disposable synthetic SQLite v4 data using tests only. Run \`pwsh -NoProfile -File deploy/staging/preflight.ps1 -SyntheticOnly\` with no real credentials in environment. The preflight fails if the legacy gateway, bridge or API has been explicitly enabled or a real Twilio/bridge token is present.
3. Run \`dotnet restore AttendanceSystem.sln\` and \`dotnet build AttendanceSystem.sln -c Release\` on the intended OS/runtime. Run all safe smoke tests including \`--self-test-snapshots\` and \`--self-test-pilot\`; no real device or network-dependent tests occur in CI.
4. Preview the Hebrew UX explicitly using \`--preview-ui\` bound to local \`http://127.0.0.1:21814/app\`; verify the banner states that events and statistics are synthetic. The preview neither starts a real database nor opens a public port.
5. Inventory all open P0 gates in docs/PRODUCTION_CHECKLIST.md and log evidence in the relevant GitHub issue. Never overwrite a real student DB for testing or reuse a synthetic snapshot key.
6. No rollout/upgrade of real data is authorized until review of historic public \`attendance.db\` exposure, institution privacy and consent, device protocol, SDK provenance, backup key-management/ACL and identity/tenant enforcement.

### Encryption and restore demonstration

\`SyntheticSnapshotService\` implements an **internal, test-only** staging backup mechanism. The service uses the SQLite online backup API to obtain a consistent snapshot from an existing **exact schema v4** database. It checks database integrity/foreign keys; enforces a 64 MiB synthetic dataset cap; derives a 256-bit key using PBKDF2-SHA256 (310,000 iterations) from a strong externally supplied passphrase; and encrypts using AES-256-GCM with a random salt, random nonce, authenticated version/length header and 128-bit tag. A partial encrypted output is flushed and renamed atomically, and existing backups are never overwritten. A receipt contains a ciphertext SHA-256 and schema/format metadata, not any private record.

Restore requires the correct passphrase, validates the complete GCM authentication tag **before** writing an output, verifies the decrypted SQLite \`integrity_check\`, \`foreign_key_check\` and exact schema, and promotes only to a **new destination path**. Existing destinations are refused, preserving rollback evidence. Wrong key, tampered file, invalid schema and corrupt source fail closed. Unit/smoke tests use random synthetic keys and disposable databases; no real institutional backup is created and no backup artifact is uploaded to GitHub.

**Security limitation:** SQLite's online backup first creates a temporary **plaintext** database file. The service deletes it after use, but filesystem deletion is **not secure erasure**; Windows private NTFS ACLs, full-disk encryption, encrypted temp filesystem, OS-level privileged isolation, hardware-backed production keys, rotation, secret injection and offsite access controls are **not implemented or proven**. For this reason, the backup service is marked \`internal\`, has **no production CLI switch**, and MUST NOT be used with real private data. It is engineering groundwork for a future audited encrypted-memory/secure-OS backup flow. An in-process receipt alone does not satisfy disaster recovery policy.

## Restore / upgrade / rollback runbook

- Stop all gateway and API processes; confirm no writes in progress before restoring. Never restore over a live database or silently downgrade a schema.
- Obtain an approved versioned encrypted backup and key through separate authorized secret custody. Verify external digest/inventory and correct institutional scope. Restore to a fresh isolated path, run integrity and FK checks and compare event/decision counts and authorized reconciliation evidence (without PII in logs).
- Keep the original DB untouched. With operator and data-controller approval, switch the service's database path **only** during an outage window; verify v4 exact schema and read-only health. Keep messaging, Realand inputs and public listeners blocked until release sign-off.
- On failure, stop services and point back to the original unchanged DB *if and only if schema and application compatibility are verified*. Never replace a newer DB with an older snapshot without an approved data-loss/replay plan. Record lost-event assessment and rollback ticket.
- RPO <=24 hours and RTO <=4 hours are **targets** from Issue #13; this change includes no measured real DR drill, scheduler, offsite copy, secrets store or monitored backup retention. Those remain separate approval gates.

## Pilot-evidence validator

\`PilotEvidenceGate\` is a read-only, synthetic-testable, fail-closed evaluator. It requires **14 unique consecutive days** with daily reconciliation, accepted-event totals, >=99.5% correctly captured accepted events, zero lost acknowledged events and p95 persistence below 5,000 ms per day; plus operator-recorded RPO <=24h, RTO <=4h, backup restore/rollback/offsite approval, SDK/legal/device/privacy/consent and owner approvals and live messaging disabled.

The evaluator returns bounded *blocker codes*, a measured sample capture rate and \`ReadyForHumanEvidenceReview\`; **\`ProductionGoAuthorized\` is always false**. Boolean checkboxes and invented synthetic data are not evidence of an institutional review. A separate authorized person must validate authentic source records and sign Go/No-Go. Use docs/PILOT_DAY_TEMPLATE.md to record only non-PII per-day redacted evidence after approval.

### Emergency decisions and stop criteria

| Event | Required action |
| --- | --- |
| Failed database integrity check | Stop intake, no ACK, preserve original and verified forensic backup; no automatic restore |
| Incorrect backup passphrase or GCM authentication error | Reject restore; do not create a DB or disclose sensitive key/path in output |
| Missing/invalid version or restored FK inconsistency | Block service activation; remediate in isolated staging |
| Lost acknowledged packet / unexpected duplicate | Stop pilot, preserve device/transaction proof, investigate ACK/replay reconciliation |
| Pilot below capture or persistence objective | Keep NO-GO; fix first and restart measured acceptance period if policy requires |
| Unapproved consent, SDK, identity or real WhatsApp | Keep live services disabled; escalate to owner and privacy controller |
| RPO/RTO objectives unmet | Reassess backup cadence and restore process, obtain revised written sign-off |

## What is not yet done

No real host hardening, TLS or service-account deployment, actual backup scheduling, production-grade secrets or KMS, offsite replication, key rotation, physical device replacement/reconnect, end-to-end power-loss experiment or two-week approved pilot. The preceding stacked draft PRs and public-history privacy issue remain unresolved. CI passing is **not** production acceptance.

Tracking: Issue #13, production checklist, next v1.0.0 signed Go/No-Go release issue #14.
