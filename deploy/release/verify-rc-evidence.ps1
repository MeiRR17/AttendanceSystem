[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][switch]$SyntheticOnly,
    [Parameter(Mandatory = $true)][ValidatePattern('^[a-fA-F0-9]{40}$')][string]$ExpectedHeadSha,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $SyntheticOnly) { throw 'Real production evidence verification not supported.' }
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$evidence = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
$manifestPath = Join-Path $evidence 'source-evidence.json'
$checksumsPath = Join-Path $evidence 'SHA256SUMS.txt'
$notesPath = Join-Path $evidence 'NO-GO.md'
foreach ($file in @($manifestPath,$checksumsPath,$notesPath)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw 'Incomplete source-only evidence bundle.'
    }
}
$expected = [IO.File]::ReadAllText($checksumsPath).Trim()
if ($expected -notmatch '^([a-f0-9]{64})  source-evidence\.json$') {
    throw 'Malformed evidence checksums.'
}
$actual = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $Matches[1]) { throw 'Evidence manifest digest mismatch.' }
$data = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($data.format -ne 'attendance-source-evidence-v1' -or
    $data.candidate -ne 'v1.0.0-rc' -or
    $data.decision -ne 'NO-GO' -or
    $data.productionAuthorized -ne $false -or
    $data.cryptographicallySigned -ne $false -or
    $data.sourceOnly -ne $true -or
    $data.includesVendorBinaries -ne $false -or
    $data.includesStudentData -ne $false) {
    throw 'Release gate flags do not prove a safe NO-GO source-only bundle.'
}
if ($data.sourceSha.ToLowerInvariant() -ne $ExpectedHeadSha.ToLowerInvariant()) {
    throw 'Evidence tied to a different commit.'
}
if (@($data.files).Count -lt 10 -or
    @($data.directNugetReferences).Count -eq 0 -or
    @($data.unresolvedExternalGates).Count -lt 8) {
    throw 'Missing source inventory, dependencies or external release blockers.'
}
$seen = New-Object System.Collections.Generic.HashSet[string]
foreach ($item in $data.files) {
    $path = [string]$item.path
    if ($path.StartsWith('/') -or $path.Contains('..') -or
        $path.Contains('\') -or
        $path -match '(?i)\.(db|bak|sqlite|dll|pem|pfx|key)$' -or
        -not $seen.Add($path)) {
        throw 'Unsafe or duplicate tracked path in evidence.'
    }
    $full = [IO.Path]::GetFullPath((Join-Path $root $path))
    if (-not $full.StartsWith($root + [IO.Path]::DirectorySeparatorChar,
                             [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Path attempted to escape the source checkout.'
    }
    if (-not (Test-Path -LiteralPath $full -PathType Leaf)) {
        throw 'Source evidence points to a missing file.'
    }
    $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $item.sha256 -or
        (Get-Item -LiteralPath $full).Length -ne [long]$item.sizeBytes) {
        throw 'Source file no longer matches the candidate manifest.'
    }
}
Write-Output 'RC_EVIDENCE_VERIFIED=YES'
Write-Output 'RELEASE_DECISION=NO-GO'
Write-Output "FILES_CHECKED=$($seen.Count)"
