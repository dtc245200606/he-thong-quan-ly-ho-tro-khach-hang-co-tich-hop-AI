$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:BootstrapAdmin__LocalSetup = 'true'
dotnet run --project (Join-Path $PSScriptRoot 'CustomerSupportAI.csproj') --no-launch-profile --urls http://localhost:5180
