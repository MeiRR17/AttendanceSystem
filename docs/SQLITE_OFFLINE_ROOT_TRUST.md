# v1.0.0-rc.9 — Offline-root-signed synthetic reviewer policy

**Engineering-only. Production remains NO-GO. No real student records, credentials, owner signatures or Realand devices were used.**

## Implementation

The rc.8 reviewer registry was supplied by the local fixture signer. In rc.9, the **actual internal synthetic migration rehearsal** rejects a caller-supplied reviewer registry and instead requires a separately signed `SyntheticRootSignedPolicy` and an **out-of-band pinned test root public key**. The verifier must validate the independent ECDSA P-256 signature before the reviewer endorsements can be accepted or any migration clone is created.

The root-signed manifest binds a fixed test-only format and purpose, monotonically increasing policy epoch, issue/expiry timestamps (at most 60 minutes), and *both* reviewers' exact roles, SPKI public-key bytes, SHA-256 key fingerprints, validity windows and revocation bits. It is encoded in a fixed domain-separated, newline-delimited, invariant-culture UTF-8 representation with reviewer entries sorted by role. A changed key ID, revoked flag, validity window, role, purpose, epoch or signature is rejected.

A second, **separate local SQLite database**, `synthetic-root-epoch.db`, records the last accepted policy epoch and canonical-payload SHA-256 digest. Its single-row primary key and atomic UPSERT provide:
- First signed epoch accepted only after signature verification.
- Same epoch accepted again only for the *identical* signed payload.
- Signed higher epoch accepted as a policy rotation; older epoch rejected after DB reopen.
- Different signed payload at an existing epoch rejected as a policy fork.

The internal rehearsal still requires its distinct `synthetic-review-nonces.db` for consumed per-rehearsal approvals. Root policy verification occurs **after** the synthetic fixture source/preflight checks but **before** consuming the two reviewer signatures or creating a SQLite backup clone. Both reviewer signatures continue to bind the fixture source hash, schema, nonce, purpose, time and independently pinned reviewer IDs.

## Negative controls and evidence

The Windows .NET 10 test harness creates one **ephemeral offline root test private key** (not stored, printed or uploaded) and two separate ephemeral reviewer keys. For each of the two SQLite 1.x-created fictional DBs, CI exercises:
- Missing root signature; altered signature, epoch, purpose, issue/expiry; wrong root public key; changed reviewer revocation status or fingerprint; duplicated/missing reviewer.
- Valid signature with exact root pin, then a signed policy epoch rotation.
- A signed rollback to the older epoch and a conflicting newly signed manifest at the same epoch, **rejected after the epoch ledger is reopened**.
- Original two reviewer approvals, durable one-time nonce replay rejection, schema-0/1 to schema-v4 clone-only migration, intact original fake records, online pre-migration backup and independent rollback.

The GitHub Actions engineering evidence requires `offlineRootTrustVerified=true` plus the existing `syntheticDualReviewVerified` and `durableReviewLedgerUsed`. It uploads only aggregate test assertions under an **unsigned NO-GO** receipt, not a student database, signing key, signature, reviewer SPKI, file path or source-data hash.

## Deliberate limitations

This is **not institutional trust**: the root is test-generated in the same offline CI process, not owned or issued by privacy/security officers; no real authentication, independent quorum of actual people, HSM/KMS, secure root pin distribution, certificate chain or managed revocation service is present.

The SQLite epoch ledger detects rollback and fork only **while its file survives intact**. A privileged adversary can delete or restore its file; it has no remote checkpoint, protected audit service or anti-rollback hardware. This test does not claim true tamper-evident audit history, legal approval, secure production backup or cross-host distributed replay prevention.

The public `DatabaseSetup.InitializeDatabaseAtPath(..., allowExistingUpgrade:true)` continues to reject every existing-db upgrade. No production migration API, private-data import, live Twilio messaging, Realand scanner integration or production tag is enabled. Original historical Git PII/privacy, institutional identity/guardian consent, actual hardware, protected backups and measured two-week pilot remain unresolved.

**Release decision: NO-GO.** Draft PR #33 is stacked on #32. Do not merge to `master`, tag `v1.0.0` or deploy on a real school environment solely because synthetic CI passes.
