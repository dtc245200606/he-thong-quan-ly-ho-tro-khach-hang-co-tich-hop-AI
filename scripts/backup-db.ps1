[CmdletBinding(SupportsShouldProcess = $true)]
param([string]$DatabasePath, [string]$BackupPath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$DatabasePath) { $DatabasePath = Join-Path $projectRoot 'customer-support.db' }
if (!$BackupPath) { $BackupPath = Join-Path $projectRoot ('backups\customer-support-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '.db') }
$DatabasePath = [IO.Path]::GetFullPath($DatabasePath)
$BackupPath = [IO.Path]::GetFullPath($BackupPath)
if (!(Test-Path -LiteralPath $DatabasePath -PathType Leaf)) { throw 'Database not found. Specify -DatabasePath for a custom connection string.' }
if (Test-Path -LiteralPath $BackupPath) { throw 'Backup already exists; choose a new filename.' }
if ($PSCmdlet.ShouldProcess($BackupPath, 'Create and verify consistent SQLite backup')) {
    & dotnet run --project (Join-Path $projectRoot 'verification\DatabaseChecks\DatabaseChecks.csproj') -- backup $DatabasePath $BackupPath
    if ($LASTEXITCODE -ne 0) { throw 'SQLite backup failed. See the error above.' }
}
