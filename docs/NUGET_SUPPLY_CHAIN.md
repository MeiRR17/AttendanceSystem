# v1.0.0-rc.1 — NuGet dependency minimization and fail-closed security audit

**Engineering stage only. The application remains NO-GO for production.**
This branch is stacked on v1.0.0-rc draft PR #24, not merged into master.

## Why this change

The previous .NET 10 GitHub Actions build succeeded but printed three NuGet audit warnings:
- \`System.Data.SqlClient 4.8.1\` — NU1902 moderate and NU1903 high
- \`System.Drawing.Common 4.7.0\` — NU1904 critical

The direct project reference was the **System.Data.SQLite 1.0.119 metapackage**. The public NuGet metadata indicates that the netstandard2.1 dependency group includes both System.Data.SQLite.Core and System.Data.SQLite.EF6. Entity Framework 6 is *not used* by this project, whose database layer uses System.Data.SQLite's raw ADO.NET connection/command APIs. EF6's closure was a suspected source of the vulnerable SqlClient and Drawing dependencies, not proven until the actual restored graph is checked.

The direct reference was replaced with **System.Data.SQLite.Core 1.0.119**, deliberately retaining the same provider engine/API version while excluding unnecessary EF6. The dormant **Twilio 7.7.0 NuGet package** was also removed, because all outbound WhatsApp calls are disabled and the outbox exercises a synthetic provider only. This change does NOT introduce live provider capability, nor change the database schema, consent or attendance policy.

## Automated acceptance requirements

The project explicitly sets:
- \`NuGetAudit=true\`
- \`NuGetAuditMode=all\` for both direct and transitive packages
- \`NuGetAuditLevel=moderate\`
- \`NU1900\` (unavailable vulnerability feed), \`NU1902\`, \`NU1903\`, \`NU1904\`, \`NU1905\` as errors

If any currently known moderate/high/critical advisory is reported by the configured NuGet audit source, or audit metadata cannot be retrieved, the restore MUST fail instead of quietly producing a green build. Low-severity advisories are not included in this policy and remain a separate review concern.

An explicit PowerShell gate \`deploy/release/audit-resolved-nuget.ps1\` reads the post-restore resolved transitive package graph via \`dotnet list ... package --include-transitive --format json --no-restore\`. It rejects the unexpected return of:
- \`System.Data.SQLite\` wrapper
- \`System.Data.SQLite.EF6\`, \`EntityFramework\`
- \`System.Data.SqlClient\`, \`System.Drawing.Common\`
- \`Twilio\`

It demands the interim \`System.Data.SQLite.Core 1.0.119\` and emits a bounded redacted list of resolved NuGet IDs and versions. The CI uploads this as a **seven-day engineering-only artifact**, separate from the existing source-only NO-GO manifest. This graph is an inventory of package references, not a complete SPDX/CycloneDX SBOM, malware scan or signed provenance attestation. Legacy checked-in SuperSocket/log4net binaries are invisible to NuGet Audit and retain their separate SDK legal/security gate.

## Reproduce on isolated synthetic CI-compatible Windows host

    dotnet restore AttendanceSystem.sln
    dotnet build AttendanceSystem.sln --configuration Release --no-restore
    ./deploy/release/audit-resolved-nuget.ps1 -Project AttendanceSystem/AttendanceSystem.csproj -OutputDirectory $env:TEMP/attendance-nuget-audit

Run all pre-existing offline regression suites. Do not enable Realand legacy gateway, outbound notifications or real organizational OIDC tokens. No real student database is required.

## What is not yet fixed

1. **SQLite provider 1.0.119 is old.** The former wrapper is deprecated upstream because of critical bugs and 2.x is recommended. A separate, measured Core 2.x migration must check API/binary behavior, native interop, backup/restore and all SQLite tests. rc.1 only minimizes unnecessary dependency surface.
2. **Physical Realand vendor binaries remain unchecked.** No proof of redistribution rights, origin, SHA-256 independent attestation or .NET 10 runtime compatibility from actual hardware.
3. **Privacy/legal, real device replay, guardian consent, tenant/row-level authorization, approved sender and production encryption/backup remain open.**
4. **The vulnerability feed reflects only known advisories at restore time.** Subsequent disclosures can reopen the release gate; no green build justifies immutable security claims.
5. **NuGet packages are not the entire bill of materials.** A complete approved SBOM must include managed/native vendor DLLs, licensing and reproducible signed build evidence before formal v1.0.0.

## Operator decision

**NO-GO.** This is a software supply-chain risk reduction step on a draft branch. Do not create a release tag or merge to master until P0 and P1 gates are evidenced, even when all synthetic tests pass.

References:
- NuGet upstream System.Data.SQLite 1.0.119 metadata: https://www.nuget.org/packages/System.Data.SQLite/1.0.119
- Core provider package metadata: https://www.nuget.org/packages/System.Data.SQLite.Core/1.0.119
- Microsoft NuGet audit guidance: https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages
- GitHub issue #14 and docs/RELEASE_READINESS.md for outstanding production gates.
