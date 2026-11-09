[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][switch]$SyntheticOnly,
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$ExpectedHeadSha,
    [Parameter(Mandatory = $true)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $SyntheticOnly) { throw 'Only synthetic candidate evidence is supported.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location -LiteralPath $root
try {
    $head = (& git rev-parse HEAD).Trim().ToLowerInvariant()
    if ($LASTEXITCODE -ne 0 -or $head -ne $ExpectedHeadSha.ToLowerInvariant()) {
        throw 'Source HEAD differs from expected immutable SHA.'
    }
    $dirty = & git status --porcelain --untracked-files=no
    if ($LASTEXITCODE -ne 0 -or $dirty) { throw 'Tracked checkout is dirty.' }
    foreach ($key in @(
        'ATTENDANCE_ENABLE_LEGACY_GATEWAY',
        'ATTENDANCE_BRIDGE_ENABLED',
        'ATTENDANCE_ADMIN_API_ENABLED'
    )) {
        if ([Environment]::GetEnvironmentVariable($key) -eq 'true') {
            throw "Dangerous listener enabled: $key"
        }
    }
    if ([Environment]::GetEnvironmentVariable('TWILIO_AUTH_TOKEN') -or
        [Environment]::GetEnvironmentVariable('ATTENDANCE_BRIDGE_SECRET_BASE64')) {
        throw 'Unsafe credentials in candidate evidence environment.'
    }

    $fullOutput = [IO.Path]::GetFullPath($OutputDirectory)
    if ($fullOutput.StartsWith($root + [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -or $fullOutput -eq $root) {
        throw 'Evidence directory must be outside the checkout.'
    }
    if (Test-Path -LiteralPath $fullOutput) {
        if ((@(Get-ChildItem -LiteralPath $fullOutput -Force)).Count -ne 0) {
            throw 'Evidence directory must be empty.'
        }
    } else { New-Item -ItemType Directory -Path $fullOutput | Out-Null }

    # Source-only manifest from git-tracked paths. Never redistribute vendor
    # SDK binaries or package any DB, secrets or untracked user files.
    $paths = @(& git ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to list Git files.' }
    $extensions = @('.cs','.csproj','.sln','.md','.yaml','.yml',
                    '.json','.js','.html','.css','.ps1')
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($relative in @($paths | Sort-Object)) {
        $extension = [IO.Path]::GetExtension($relative).ToLowerInvariant()
        if ($extension -notin $extensions -and $relative -ne '.gitignore') { continue }
        if ($relative -match '(?i)(^|/)(secrets?|\.env)(/|\.|$)' -or
            $relative -match '(?i)\.(pfx|pem|db|sqlite|bak|dll)$') {
            throw 'Sensitive-looking tracked source path requires manual review.'
        }
        $path = Join-Path $root ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw 'Tracked source file is missing.'
        }
        $size = (Get-Item -LiteralPath $path).Length
        if ($size -gt 2MB) { throw 'Source file exceeds 2MiB evidence limit.' }
        $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        $entries.Add([ordered]@{ path = $relative; sizeBytes = $size; sha256 = $digest })
    }
    if ($entries.Count -lt 10) { throw 'Incomplete source file manifest.' }
    [xml]$project = Get-Content -LiteralPath (
        Join-Path $root 'AttendanceSystem/AttendanceSystem.csproj') -Raw
    $packages = @($project.Project.ItemGroup.PackageReference |
        Where-Object { $_ -ne $null } |
        ForEach-Object { [ordered]@{ package = [string]$_.Include
                                     version = [string]$_.Version } } |
        Sort-Object { $_.package })
    if ($packages.Count -eq 0) { throw 'Missing direct NuGet dependency inventory.' }

    $openGates = @(
        'public_git_history_privacy_review',
        'realand_hardware_sdk_legal_provenance',
        'institutional_consent_retention_and_object_scope',
        'approved_two_week_physical_pilot',
        'production_key_custody_and_secure_temporary_storage',
        'offsite_restore_and_measured_rpo_rto',
        'tls_service_account_monitoring_and_oncall',
        'vendor_approved_twilio_sender_and_receipts',
        'signed_product_privacy_security_and_operations_approvals'
    )
    $manifest = [ordered]@{
        format = 'attendance-source-evidence-v1'
        candidate = 'v1.0.0-rc'
        sourceSha = $head
        sourceOnly = $true
        includesVendorBinaries = $false
        includesStudentData = $false
        cryptographicallySigned = $false
        productionAuthorized = $false
        decision = 'NO-GO'
        files = @($entries.ToArray())
        directNugetReferences = @($packages)
        unresolvedExternalGates = $openGates
    }
    $manifestPath = Join-Path $fullOutput 'source-evidence.json'
    $manifest | ConvertTo-Json -Depth 10 |
        Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
    $digest = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$digest  source-evidence.json" |
        Set-Content -LiteralPath (Join-Path $fullOutput 'SHA256SUMS.txt') -Encoding ascii
    $notes = @(
        '# AttendanceSystem v1.0.0-rc — NO-GO evidence bundle',
        '',
        'Not an installable release, complete SBOM, attestation or code signature.',
        "Source commit: $head",
        "Evidence SHA256: $digest",
        '',
        'Source hashes and direct NuGet references are informational only.',
        'Vendor SDK provenance, historic PII, physical device and two-week pilot',
        'have not been validated. No release tag, publish or merge is authorized.'
    ) -join [Environment]::NewLine
    $notes | Set-Content -LiteralPath (
        Join-Path $fullOutput 'NO-GO.md') -Encoding utf8NoBOM
    Write-Output 'RC_EVIDENCE_GENERATED=YES'
    Write-Output 'RELEASE_DECISION=NO-GO'
    Write-Output "SOURCE_SHA=$head"
    Write-Output "SOURCE_FILES=$($entries.Count)"
}
finally { Pop-Location }
