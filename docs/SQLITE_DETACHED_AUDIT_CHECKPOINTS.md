# AttendanceSystem v1.0.0-rc.10 — detached signed synthetic audit checkpoints

**Engineering scope only. Production release: NO-GO.** This change does not create an institution-owned PKI, write-once log, production authorization workflow, or access to any real student database.

## What the code actually does

The existing internal \`SyntheticMigrationRehearsal.Run\` already requires root-signed synthetic reviewer policy, two test-reviewer signatures, a persistent synthetic nonce ledger, exact old-provider file checks and a schema preflight. rc.10 adds an **audit of that same rehearsal path**, rather than merely drawing an architectural diagram.

- \`SyntheticMigrationAuditChain\` creates a test-only SQLite audit ledger for exactly the two generated old-schema fixtures (0 and 1), under separate fixed filenames in their existing disposable runner TEMP directory. The audit records have strictly allowlisted event kinds, increasing sequence numbers, UTC Unix timestamps and \`PrevHash\`/\`EntryHash\` SHA-256 links. No student names, phones, attendance details, subject identifiers, source-file paths or actual biometric data are written to the audit.
- The actual rehearsal now appends \`synthetic-review-accepted\` **after** verifying the signed root/reviewer approvals but **before** creating a clone, and \`synthetic-rollback-verified\` **only after** the migration, pre-migration backup, independent rollback and original-source integrity have all been checked. Incomplete rehearsals cannot produce a two-event signed completion checkpoint.
- \`SyntheticAuditCheckpointTestSigner\` generates a **third, independent, disposable in-memory ECDSA P-256 test key**. It signs a domain-separated detached checkpoint containing fixture schema, exact two-event count, latest chain-head SHA-256, issuance timestamp, test signer public-key fingerprint and explicit \`syntheticOnly=true\`, \`productionAuthorized=false\`, \`NO-GO\`. The checkpoint contains the **public** ephemeral test key for inspection, but the verifier refuses to trust that embedded key without a separately supplied pinned copy.
- \`SyntheticAuditCheckpointVerifier\` verifies the signature with the caller's independent pinned test key and **reopens the audit SQLite with \`FailIfMissing=true\`**, walks/recalculates the entire hash chain, then compares both row count and chain-head hash to the signed detached checkpoint.
- \`SyntheticAuditCheckpointSmokeChecks\` verifies authentic data, then intentionally tests a changed checkpoint, forged signature, wrong pinned test key, an absent audit file, a **previously valid shorter audit prefix** (simulated rollback) and a changed event in a separate copy. The latter two are prepared by SQLite online backup so that a WAL journal is never ignored. All negative controls must be refused while the original ledger remains unchanged.
- \`SyntheticAuditCheckpointEvidence\` emits **only** \`audit-checkpoint-schema0.json\` and \`audit-checkpoint-schema1.json\` after the existing \`migration-rehearsal.json\` and \`SHA256SUMS.txt\`. The CI artifact has four small non-PII documents and no SQLite files; the source and test ledgers, rollback clones and WAL/SHM sidecars are removed with the temporary fixture directory.

The two checkpoint documents are **signed with disposable test keys, not with real institutional keys**. The GitHub Actions run checks the NO-GO format and public-key fingerprint and independently exercises full signature/hash-chain verification in .NET against a pinned test key. The public key embedded in the artifact alone **does not establish trust**; a future real deployment would need an authenticated, separately provisioned root key and a remote append-only/WORM checkpoint sink.

## Security guarantees and limitations

| Scenario | Synthetic test result | Real-world limitation |
| --- | --- | --- |
| Change an audit event without recomputing its hash chain | Local hash-chain validation rejects | An administrator could recompute an unanchored local chain |
| Rewrite the local audit and retain the old signed detached checkpoint | Signed head comparison rejects | Only if verifier still has a trusted checkpoint and its genuine key anchor |
| Delete the local audit database | Explicit no-create-on-verify rejects | A real system needs remote durable checkpoint retention and alerting |
| Revert to a valid earlier audit prefix | Signed event count and head hash reject | A checkpoint must be anchored elsewhere before a privileged rollback |
| Tamper with checkpoint, signature or pinned root | ECDSA verification rejects | The CI key is ephemeral, self-issued and not an institutional attestation |
| Lose all local data plus the detached checkpoint | Not detectable from a local-only source | Requires external retention/attestation and offsite monitoring |
| Restart, restore or migrate between hosts | Not a production distributed protocol | Requires atomic multi-host ordering, trusted clock and durable central ledger |

**No false WORM claim:** SQLite is writable by privileged local actors. SHA-256 hash chaining alone is not immutability. GitHub Actions artifacts provide a separate **temporary CI evidence location**, not an institution-controlled, tamper-resistant audit backend; artifacts expire after seven days. The root and reviewer keys remain disposable synthetic test keys. No real student source data, scanner, parent message, institutional identity provider or production signing key has been used.

The public existing-database upgrade entry point remains **unconditionally disabled**. Existing critical blockers—historic public Git student data/privacy remediation, secure production encryption and key custody, vendor-verified Realand hardware/SDK, lawful guardian consent/tenant-scoped access, real 14-day pilot, offsite recovery and human release approvals—remain unresolved. PR #34 must stay a draft, master remains unchanged, no v1.0.0 tag or deployment is authorized.

## Verification

On PR #34's exact head, Windows .NET 10 GitHub Actions runs old SQLite v1.x fake-file generation, modern SQLite v2.x compatibility, root/reviewer signatures, fixed trust epoch, persistent nonce replay, signed detached audit checkpoint and the tamper/delete/rollback controls. Previous 1,000 synthetic authenticated gateway inserts/replays, JWT/RBAC, offline outbox, UI, encrypted synthetic snapshot/restore, failure-injection and NO-GO governance tests remain mandatory.

**Expected result:** \`SYNTHETIC_DETACHED_AUDIT=PASS\`, \`SYNTHETIC_RESTORE_EVIDENCE=NO-GO\`, and workflow success. Any missing evidence file, inadequate count, wrong signature, forged root or incomplete audit must cause CI failure.
