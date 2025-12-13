param(
    [string]$BaseUrl,
    [string]$DiscoveryPath = $env:UE_MCP_DISCOVERY,
    [int]$LogCount = 10,
    [int]$StreamLines = 3,
    [int]$TimeoutSeconds = 10,
    [switch]$EnableWriteSmoke
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")

Push-Location $repoRoot
try {
    $smoke = Join-Path $scriptRoot "Invoke-McpSmokeMono.ps1"
    & $smoke -BaseUrl $BaseUrl -DiscoveryPath $DiscoveryPath -LogCount $LogCount -StreamLines $StreamLines -TimeoutSeconds $TimeoutSeconds -EnableWriteSmoke:$EnableWriteSmoke
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
