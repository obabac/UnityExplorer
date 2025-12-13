param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [string]$AuthToken
)

$ErrorActionPreference = "Stop"

function Assert-NpxExists {
    $npx = Get-Command npx -ErrorAction SilentlyContinue
    if (-not $npx) {
        throw "npx is required. Install Node.js/npm or run via a machine that has @modelcontextprotocol/inspector available."
    }
}

function Invoke-InspectorCli {
    param(
        [string]$Method,
        [string[]]$ExtraArgs = @()
    )
    $args = @("--yes", "@modelcontextprotocol/inspector", "--cli", "--transport", "http", $BaseUrl.TrimEnd('/'), "--method", $Method)
    if ($AuthToken) {
        $args += @("--header", "Authorization: Bearer $AuthToken")
    }
    $args += $ExtraArgs

    Write-Host "Running inspector --method $Method" -ForegroundColor Cyan
    & npx @args
    if ($LASTEXITCODE -ne 0) {
        throw "inspector CLI failed for method $Method (exit code $LASTEXITCODE)."
    }
}

try {
    Assert-NpxExists
    $target = $BaseUrl.TrimEnd('/')
    Write-Host "Inspector CLI smoke target: $target" -ForegroundColor Green

    Invoke-InspectorCli -Method "tools/list"
    Invoke-InspectorCli -Method "resources/list"
    Invoke-InspectorCli -Method "resources/read" -ExtraArgs @("--uri", "unity://status")
    Invoke-InspectorCli -Method "tools/call" -ExtraArgs @("--tool-name", "GetStatus")

    Write-Host "PASS: inspector CLI smoke succeeded against $target" -ForegroundColor Green
}
catch {
    Write-Error $_
    Write-Host "FAIL: inspector CLI smoke failed against $BaseUrl" -ForegroundColor Red
    exit 1
}
