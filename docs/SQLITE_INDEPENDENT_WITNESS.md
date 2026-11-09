# AttendanceSystem v1.0.0-rc.11 — isolated synthetic evidence witness

**Status: test-only engineering control. Production remains NO-GO.**

This version is a concrete improvement to the **independent verification process** for the rc.10 signed synthetic audit checkpoints. It does NOT configure an external notarization provider, institutional PKI, WORM storage, secure remote retention, student-data processing or production migration authorization.

## Separate-process verification

New standalone console project: \`tests/SyntheticEvidenceVerifier/\`. It targets .NET 10 with **no reference to AttendanceSystem, SQLite packages, Realand DLLs, the original ECDSA signers, or any migration code**. Windows GitHub Actions launches this separate verifier after the old-provider-generated synthetic fixture migrations and recovery rehearsals have completed.

The producer's disposable third ECDSA P-256 test signer writes its **public SPKI key** for schema 0 and schema 1 into a dedicated, freshly created \`attendance-rc11-witness-pins-<GUID>\` directory directly under the Windows runner's OS TEMP folder. This directory is **separate from** the existing \`attendance-rc6-migration-evidence\` folder; both remain on the same runner. Neither SPKI file is uploaded in the GitHub Actions artifact. Each file and directory is checked against symlinks/reparse points and a strict fixed-name pattern before use. The pin directory is deleted by a \`finally\` block after verification.

The independent process takes two explicit arguments: (1) evidence-directory path, (2) **out-of-bundle** pin-directory path; it refuses invocation unless \`ATTENDANCE_SYNTHETIC_FIXTURES=true\`. The verifier independently checks:

- There are **exactly four** evidence files and **exactly two** pinned SPKI files; no unexpected extra files, paths, links or empty values.
- SHA-256 checksum of \`migration-rehearsal.json\`; source/target provider version labels; two distinct schema 0/1 synthetic results; precise 4/3/2 fictional record counts; all migration, rollback, root and reviewer validation flags; and \`NO-GO\` with \`ProductionMigrationAuthorized=false\` throughout.
- Each detached checkpoint's schema, two-entry audit head format, timestamp window, purpose, format, \`SyntheticOnly=true\`, \`ProductionAuthorized=false\`, \`NO-GO\`, and the fingerprint and SPKI matching the **independently supplied** CI public key pin.
- The independent ECDSA P-256 SHA-256/IEEE-P1363 64-byte signature, by reconstructing the checkpoint's exact domain-separated canonical message in **separate source code**. The verifier does not trust the public key embedded inside the self-signed checkpoint.

The \`--self-test\` mode re-verifies the positive evidence, then runs negative controls for manipulated head hash/count, \`NO-GO\` flipped to \`GO\`, production authorization, altered purpose or timestamp, random signature, changed schema, substituted pinned key and a maliciously replaced embedded public key. A changed production authorization flag in the numeric evidence is also denied. No real data enters any test.

The workflow additionally checks the four-file NO-GO contract in PowerShell and uploads **only** the same temporary engineering evidence artifact with seven-day retention. The two public key pins and any other source data are deleted with their temporary folders. The witness never writes a credential, private key, source database, student ID or biometric data.

## What this does not prove

1. Two programs on the **same GitHub runner** are process-isolated but do not establish independent ownership, a second administrative domain, a managed trusted clock, a remote checkpoint acceptance record, or strong protection against a malicious CI administrator.
2. Both the checkpoint signing key and the separate pinned public key are created in one run's original synthetic test harness. This is a **separately supplied test pin**, not a real externally provisioned institution trust anchor. An attacker controlling the producer/runner could replace both.
3. A GitHub Actions artifact (seven-day retention) is a **temporary evidence handoff**, not WORM, an external Notary service, cryptographic timestamping or a durable remote monitoring service.
4. The verifier establishes that a checkpoint signature and synthetic numeric manifest are internally consistent with the test public pin. It **does not verify an absent production audit database**, prove a private ledger still exists after the run, or recover a deleted source. These checks remain in the existing same-process SQLite rollback/tamper smoke suite.
5. Institutional KMS/HSM, certificate lifecycle, real reviewer identity and legal approval, remote append-only/WORM retention and alerts, guardian consent, licensed Realand device, privacy incident resolution, secure offsite backup, actual 14-day pilot and human release signoff are still required.

**The public existing-schema SQLite migration API remains disabled.** No merge to \`master\`, production tag, real data import or production deployment is permitted on this evidence. PR #35 remains a stacked draft.

## Acceptance criteria

The exact final SHA of PR #35 must pass Windows .NET 10 restore/build, resolved NuGet audit, old SQLite 1.x provider fixture generation, signed root/reviewer/audit/rollback tests, the standalone \`--verify\` witness, negative \`--self-test\` witness, all prior bridge/policy/authorization/outbox/fault/backup/gating suites, and \`RELEASE_DECISION=NO-GO\`. Successful witness output includes \`SYNTHETIC_INDEPENDENT_WITNESS=PASS\` and \`SYNTHETIC_INDEPENDENT_WITNESS_NEGATIVE_CONTROLS=PASS\`.
