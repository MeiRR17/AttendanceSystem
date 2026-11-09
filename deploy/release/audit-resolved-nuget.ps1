[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][string]$Project,
  [Parameter(Mandatory = $true)][string]$OutputDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectPath = (Resolve-Path -LiteralPath $Project).Path
$folder = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $folder)) {
  New-Item -ItemType Directory -Path $folder | Out-Null
}
# dotnet package list reads the *resolved* post-restore graph, including
# transitive packages. No feed credentials or project secrets are persisted.
$jsonLines = @(& dotnet list $projectPath package --include-transitive --format json --no-restore)
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect resolved NuGet dependency graph.' }
$raw = $jsonLines -join [Environment]::NewLine
try { $data = $raw | ConvertFrom-Json }
catch { throw 'NuGet JSON graph could not be parsed.' }
if (-not $data.PSObject.Properties['projects']) {
  throw 'No project data returned by NuGet.'
}
$packages = New-Object System.Collections.Generic.List[object]
foreach ($project in @($data.projects)) {
  if (-not $project.PSObject.Properties['frameworks']) { continue }
  foreach ($framework in @($project.frameworks)) {
    foreach ($field in @('topLevelPackages','transitivePackages')) {
      if (-not $framework.PSObject.Properties[$field]) { continue }
      foreach ($package in @($framework.$field)) {
        if ($null -ne $package -and $package.PSObject.Properties['id']) {
          $packages.Add($package)
        }
      }
    }
  }
}
if ($packages.Count -lt 6) {
  throw 'Unexpectedly empty or incomplete resolved dependency graph.'
}

$disallowed = @(
  'System.Data.SQLite',
  'System.Data.SQLite.EF6',
  'EntityFramework',
  'System.Data.SqlClient',
  'System.Drawing.Common',
  'Twilio'
)
$packageIds = @($packages | ForEach-Object { [string]$_.id })
$foundForbidden = @($packageIds | Where-Object { $_ -in $disallowed } | Sort-Object -Unique)
if ($foundForbidden.Count -gt 0) {
  throw ('Unsupported/vulnerable dependency family remains: ' + ($foundForbidden -join ', '))
}

# A provider major-version migration is NOT attempted in this review.
# Exact version retained to preserve existing SQLite ADO.NET API semantics.
$core = @($packages | Where-Object {
  $_.id -eq 'System.Data.SQLite.Core' -and $_.resolvedVersion -eq '1.0.119'
})
if ($core.Count -ne 1) {
  throw 'Expected vetted interim System.Data.SQLite.Core 1.0.119 not resolved.'
}

# Store resolved dependency inventory for inspection. This is not a complete
# SBOM, vulnerability feed export, binary signature or license clearance.
$inventory = [ordered]@{
  format = 'attendance-nuget-graph-review-v1'
  status = 'ENGINEERING_ONLY_NO_GO'
  project = 'AttendanceSystem'
  target = 'net10.0'
  totalResolvedNodes = $packages.Count
  excludedLegacyFamilies = $disallowed
  resolvedDependencies = @($packages |
    Sort-Object -Property id |
    ForEach-Object { [ordered]@{
      id = [string]$_.id
      resolvedVersion = [string]$_.resolvedVersion
    } })
  limitations = @(
    'NuGetAudit consults its online advisory feed at restore time.',
    'Historical vulnerable packages can still be present in old releases.',
    'Vendor SDK DLLs are NOT audited by NuGet.',
    'The current SQLite.Core 1.0.119 is an interim pinned provider, not a final v2 migration.',
    'No transitive license provenance and no production deployment authorization.'
  )
}
$outputPath = Join-Path $folder 'nuget-resolved-inventory.json'
$inventory | ConvertTo-Json -Depth 8 |
  Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
Write-Output 'NUGET_GRAPH_AUDIT=PASS'
Write-Output "TOTAL_NODES=$($packages.Count)"
Write-Output 'LEGACY_EF6_SQLCLIENT_DRAWING_TWILIO=ABSENT'
