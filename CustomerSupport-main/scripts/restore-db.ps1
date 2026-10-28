[CmdletBinding(SupportsShouldProcess = $true)]
param([Parameter(Mandatory = $true)][string]$BackupPath, [string]$DatabasePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$DatabasePath) { $DatabasePath = Join-Path $projectRoot 'customer-support.db' }
$DatabasePath = [IO.Path]::GetFullPath($DatabasePath)
$BackupPath = [IO.Path]::GetFullPath($BackupPath)
if (!(Test-Path -LiteralPath $BackupPath -PathType Leaf)) { throw 'Backup not found.' }
if ($DatabasePath -eq $BackupPath) { throw 'Backup and target must differ.' }
if ($PSCmdlet.ShouldProcess($DatabasePath, 'Restore SQLite backup; retain previous database when present')) {
    Write-Warning "Stop CustomerSupport gracefully before restoring. Target: $DatabasePath. Backup: $BackupPath. Existing data will be replaced; a safety copy will be retained."
    $confirmation = Read-Host 'Type RESTORE to confirm (anything else cancels)'
    if ($confirmation -cne 'RESTORE') { throw 'Restore cancelled. No database was changed.' }
    & dotnet run --project (Join-Path $projectRoot 'verification\DatabaseChecks\DatabaseChecks.csproj') -- restore $BackupPath $DatabasePath
    if ($LASTEXITCODE -ne 0) { throw 'SQLite restore refused/failed. No recovery files were deleted; consult the error and safety copy.' }
}
