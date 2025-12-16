param(
    [string]$BaseUrl = "http://192.168.178.210:51477",
    [string]$AuthToken
)

$ErrorActionPreference = "Stop"

Write-Host "Inspector UI is deprecated. Use the CLI-only flow instead." -ForegroundColor Yellow
Write-Host "Run: pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl $BaseUrl [-AuthToken <token>]" -ForegroundColor Cyan
exit 1
