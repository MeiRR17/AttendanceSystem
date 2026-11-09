# AttendanceSystem v1.0.0 — release-candidate evidence and formal NO-GO

**Status: v1.0.0-rc engineering branch only. Production GO is NOT authorized.**
No v1.0.0 Git tag or GitHub Release may be published, no unlicensed vendor binaries may be redistributed, and no user/student production data may be used while P0 gates remain open. A passing Windows .NET 10 GitHub CI workflow is **engineering evidence**, not a formal institutional approval.

## What is actually implemented

- Code-only governance gate in \`AttendanceSystem/Deployment/ReleaseGovernanceGate.cs\` with 18 explicit release requirements: privacy of historical Git records, Realand SDK/hardware/legal provenance, guardian consent and retention, per-student RBAC and live IdP, approved outbound sender/opt-out, verified recovery/RPO/RTO, real 14-day device pilot, verified ACK/replay, host TLS/threat model, approved runtime and dependencies, service account and key custody, complete SBOM, independently verified release signature and immutable tag, production monitoring/on-call/rollback, and separate product/privacy-security/operations owner approvals.
- The gate checks the structure and documented review references of supplied evidence, but **can never set ProductionGoAuthorized=true**. The CLI \`--assess-release\` always reports NO-GO and unresolved required gates. \`--self-test-release\` checks empty approvals, missing privacy approval, dubious evidence reference and mutable source name; even synthetic "all approved" remains NO-GO.
- \`deploy/release/create-rc-evidence.ps1 -SyntheticOnly\` generates **source-only**, unsigned, non-installable evidence bound to a full 40-digit commit SHA from an exact Git checkout. The JSON inventory includes sizes and SHA-256 hashes of tracked source/config/document files, the direct NuGet PackageReference list and the unresolved external gate list. Binary vendor SDK DLLs, SQLite databases, secrets, encrypted backups and private records are excluded from the bundle. Build binaries are NOT packaged.
- \`deploy/release/verify-rc-evidence.ps1 -SyntheticOnly\` compares the manifest checksum, full source SHA, source file hashes and metadata, validates the NO-GO flags and rejects unsafe/missing/duplicate files.
- PR CI checks out the **exact PR head SHA**, runs the same offline regression suites, generates and verifies the release-candidate evidence, then uploads a **7-day-expiry GitHub Actions artifact named NO-GO**. This artifact is not a cryptographic attestation, signature, SPDX/CycloneDX complete SBOM or reproducible release binary. No GitHub Release/tag is created by CI.

## How to reproduce in an isolated test checkout

Use PowerShell 7 and .NET 10 on a supported Windows test host, with the real scanner bridge/API, Twilio and other production credentials **absent**.

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln -c Release --no-restore
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --self-test-release
    dotnet run --project AttendanceSystem/AttendanceSystem.csproj -c Release --no-build -- --assess-release

Obtain a full 40-character SHA using \`git rev-parse HEAD\`, then generate a source-only evidence bundle **outside the checkout**. Example PowerShell:

    $sha = (git rev-parse HEAD).Trim()
    $output = Join-Path $env:TEMP 'attendance-rc-evidence'
    ./deploy/release/create-rc-evidence.ps1 -SyntheticOnly -ExpectedHeadSha $sha -OutputDirectory $output
    ./deploy/release/verify-rc-evidence.ps1 -SyntheticOnly -ExpectedHeadSha $sha -EvidenceDirectory $output

Output directory must be new or empty. Never upload the artifact to a public Release or external storage. Do not mistake the inventory of direct NuGet dependencies for a complete transitive license/security SBOM. For a production build, separately establish reproducibility, vendor binary license rights, transitive dependency provenance and signed attestations.

## Gate matrix — current evidence (2026-11-09)

| Gate / accountable review | Evidence today | Release status |
| --- | --- | --- |
| .NET 10 source build and offline smoke | Previous stacked PRs #15–#23 had green CI on their individual SHAs | Engineering-only |
| v1.0 source hash / direct dependency inventory | CI generation and verification in this candidate branch | Engineering-only; unsigned |
| Biometric/student PII and historical public Git DB (#4) | **No authorized data-controller assessment** | BLOCKED P0 |
| Realand device firmware, SDK redistribution and replay | Synthetic local bridge only | BLOCKED P0 |
| Real OIDC issuer, guardian/row-level RBAC and consent | JWT-local tests, no institution authentication | BLOCKED P0 |
| Approved live Twilio template and vendor callbacks | Dry-run only; no message sent | BLOCKED |
| Secure Windows ephemeral disk, KMS and offsite backups | Synthetic encrypted snapshot has plaintext temp file | BLOCKED P0 |
| RPO <=24h / RTO <=4h, offsite restore, rollback rehearsal | No measured production restore drill | BLOCKED P0 |
| Minimum 14-day device pilot with daily reconciliation | Synthetic evidence evaluator, no actual approved pilot | BLOCKED P0 |
| Operational deployment, TLS/monitoring/on-call | Localhost experiments only | BLOCKED P0 |
| Three accountable owner signatures and approval record | Not provided/verified | BLOCKED P0 |
| Reproducible signed executable, complete SBOM and v1.0.0 tag | Explicitly not generated until prior gates are met | BLOCKED |

**Decision: NO-GO.** The engineering code may be reviewed and tested on synthetic fixtures; it must not be connected to students, real recipients or an institutional scanner or promoted to production.

## Exact order to remove blockers

1. Independent authorized review of potentially sensitive historical public Git content and repository visibility; privacy incident/retention decisions recorded.
2. Realand model, protocol, firmware and vendor DLL redistribution/license approval; isolated device authorization and ACK/replay verification.
3. Institution-backed consent/guardian rights, data controller, PII flows, per-student RBAC and real OIDC/SSO, approved opt-out; independent threat model.
4. Audit safe backup key custody, encrypted temporary storage and OS ACLs, schedule offsite backups and verify a clean-system recovery; record observed RPO/RTO and rollback.
5. Real staging host, service account, TLS boundary, monitoring/on-call and rollback playbook reviewed and tested.
6. After approvals, execute the real **14-day** supervised pilot with source-of-truth attendance reconciliation, daily ownership and adverse event escalation.
7. Confirm supported SDK/dependency/license provenance, complete transitive SBOM, signed/reproducible binary and immutable artifact digest, then obtain signed three-owner Go/No-Go decision.
8. Only then consider merging the reviewed stacked PRs, a signed v1.0.0 tag/Release and actual production rollout. Failed P0 implies NO-GO.

Use docs/RELEASE_CHANGE_TEMPLATE.md for authorization records and docs/PRODUCTION_CHECKLIST.md for unchanged release gates. Don't mark them checked from mock assertions or source existence.
