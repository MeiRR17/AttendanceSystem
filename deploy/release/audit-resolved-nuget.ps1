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
# Inspect the canonical post-restore NuGet assets rather than CLI output.
# libraries contains every actually resolved direct and transitive package.
$assetsPath = Join-Path (Join-Path (Split-Path -Parent $projectPath) 'obj') 'project.assets.json'
if (-not (Test-Path -LiteralPath $assetsPath -PathType Leaf)) {
  throw 'NuGet project.assets.json not found; run dotnet restore first.'
}
try { $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json }
catch { throw 'Restored NuGet assets JSON could not be parsed.' }
if (-not $assets.PSObject.Properties['libraries']) {
  throw 'NuGet assets missing resolved libraries.'
}
$packages = New-Object System.Collections.Generic.List[object]
foreach ($library in $assets.libraries.PSObject.Properties) {
  if ($null -eq $library.Value -or
      -not $library.Value.PSObject.Properties['type'] -or
      $library.Value.type -ne 'package') { continue }
  $separator = $library.Name.LastIndexOf('/')
  if ($separator -le 0 -or $separator -ge $library.Name.Length - 1) {
    throw 'Invalid package ID/version in NuGet assets.'
  }
  $packages.Add([pscustomobject]@{
    id = $library.Name.Substring(0,$separator)
    resolvedVersion = $library.Name.Substring($separator + 1)
  })
}
if ($packages.Count -lt 6) {
  throw 'Unexpectedly empty or incomplete resolved dependency graph.'
}

$disallowed = @(
  'System.Data.SQLite.Core',
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

# v2 is a new package distribution with P/Invoke native interop.
# The full Windows native+schema/backup regression must also pass.
$provider = @($packages | Where-Object {
  $_.id -eq 'System.Data.SQLite' -and $_.resolvedVersion -eq '2.0.4'
})
if ($provider.Count -ne 1) {
  throw 'Expected System.Data.SQLite 2.0.4 did not resolve exactly once.'
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
    'SQLite provider System.Data.SQLite 2.0.4 changes native P/Invoke distribution; verify Windows runtime and backup smoke.',
    'No transitive license provenance and no production deployment authorization.'
  )
}
$outputPath = Join-Path $folder 'nuget-resolved-inventory.json'
$inventory | ConvertTo-Json -Depth 8 |
  Set-Content -LiteralPath $outputPath -Encoding utf8NoBOM
Write-Output 'NUGET_GRAPH_AUDIT=PASS'
Write-Output "TOTAL_NODES=$($packages.Count)"
Write-Output 'LEGACY_EF6_SQLCLIENT_DRAWING_TWILIO=ABSENT'
Write-Output 'SQLITE_PROVIDER=System.Data.SQLite/2.0.4'
