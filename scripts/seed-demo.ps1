[CmdletBinding()]
param([switch]$Remove, [switch]$SkipBackup)
# Adds (or with -Remove deletes) fictional demo accounts and tickets in the local database.
# Demo accounts use the @demo.customersupport.vn domain; credentials are written to App_Data/demo-accounts.txt.
# Stop the web app (Ctrl+C) first. A verified backup is made before any change unless -SkipBackup.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (Get-Process CustomerSupportAI -ErrorAction SilentlyContinue) { throw 'The web app is still running. Stop it with Ctrl+C, then run this script again.' }
if (!$SkipBackup -and (Test-Path (Join-Path $projectRoot 'customer-support.db'))) {
    & (Join-Path $PSScriptRoot 'backup-db.ps1')
}
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Logging__LogLevel__Default = 'Warning'
${env:Logging__LogLevel__Microsoft.EntityFrameworkCore} = 'Warning'
& dotnet run --project (Join-Path $projectRoot 'CustomerSupportAI.csproj') --no-launch-profile -- ($(if ($Remove) { '--remove-demo' } else { '--seed-demo' }))
if ($LASTEXITCODE -ne 0) { throw 'Demo data command failed. See the error above.' }
