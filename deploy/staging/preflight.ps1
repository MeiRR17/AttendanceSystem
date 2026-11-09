[CmdletBinding()]
param([switch]$SyntheticOnly)
$ErrorActionPreference = 'Stop'

if (-not $SyntheticOnly) {
    throw 'Staging preflight requires -SyntheticOnly. This script never approves production.'
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $repoRoot 'AttendanceSystem/AttendanceSystem.csproj'
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw 'AttendanceSystem project not found.'
}
$dangerous = @(
    'ATTENDANCE_ENABLE_LEGACY_GATEWAY',
    'ATTENDANCE_BRIDGE_ENABLED',
    'ATTENDANCE_ADMIN_API_ENABLED'
)
foreach ($name in $dangerous) {
    $value = [Environment]::GetEnvironmentVariable($name)
    if ($value -eq 'true') {
        throw "Unsafe staging environment: $name must be disabled."
    }
}
if ([Environment]::GetEnvironmentVariable('ATTENDANCE_BRIDGE_SECRET_BASE64')) {
    throw 'Bridge secret must not be loaded into synthetic staging preflight.'
}
if ([Environment]::GetEnvironmentVariable('TWILIO_AUTH_TOKEN')) {
    throw 'Real Twilio credentials are not allowed in synthetic staging.'
}
$dotnet = Get-Command 'dotnet' -ErrorAction Stop
$version = & $dotnet.Source --version
if ($LASTEXITCODE -ne 0 -or -not $version.StartsWith('10.')) {
    throw 'The supported .NET 10 SDK is required.'
}
Write-Output 'SYNTHETIC_STAGING_PREFLIGHT=PASS'
Write-Output 'Safe mode only; no device, no messaging, no real DB, no production approval.'
