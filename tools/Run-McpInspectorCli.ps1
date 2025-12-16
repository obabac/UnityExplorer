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

function Normalize-InspectorBaseUrl {
    param(
        [string]$Url
    )

    $trimmed = $Url.TrimEnd('/')
    if ($trimmed -match '/mcp($|/)') { return $trimmed }
    if ($trimmed -match '/message($|/)') { return $trimmed }
    return "$trimmed/mcp"
}

function Invoke-InspectorCli {
    param(
        [string]$Method,
        [string[]]$ExtraArgs = @()
    )
    $args = @("--yes", "@modelcontextprotocol/inspector", "--cli", "--transport", "http", $script:InspectorBaseUrl, "--method", $Method)
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
    $script:InspectorBaseUrl = Normalize-InspectorBaseUrl $BaseUrl
    $inputBase = $BaseUrl.TrimEnd('/')
    if ($script:InspectorBaseUrl -ne $inputBase) {
        Write-Host "Normalized base URL to $script:InspectorBaseUrl (input: $inputBase)" -ForegroundColor DarkGray
    }
    Write-Host "Inspector CLI smoke target: $script:InspectorBaseUrl" -ForegroundColor Green

    Invoke-InspectorCli -Method "tools/list"
    Invoke-InspectorCli -Method "resources/list"
    Invoke-InspectorCli -Method "resources/read" -ExtraArgs @("--uri", "unity://status")
    Invoke-InspectorCli -Method "tools/call" -ExtraArgs @("--tool-name", "GetStatus")

    Write-Host "PASS: inspector CLI smoke succeeded against $script:InspectorBaseUrl" -ForegroundColor Green
}
catch {
    Write-Error $_
    Write-Host "FAIL: inspector CLI smoke failed against $script:InspectorBaseUrl" -ForegroundColor Red
    exit 1
}
