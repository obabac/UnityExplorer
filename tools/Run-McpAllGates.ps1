param(
    [string]$BaseUrlIl2cpp = "http://192.168.178.210:51477",
    [string]$BaseUrlMono = "http://192.168.178.210:51478",
    [switch]$EnableWriteSmoke,
    [string]$AuthToken
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")

function Invoke-Step {
    param(
        [string]$Label,
        [scriptblock]$Action
    )
    Write-Host "== $Label ==" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Label failed (exit $LASTEXITCODE)."
    }
}

function Invoke-ContractTests {
    param(
        [string]$DiscoveryPath,
        [string]$Label,
        [string]$ContractScript
    )
    $previous = $env:UE_MCP_DISCOVERY
    try {
        $env:UE_MCP_DISCOVERY = $DiscoveryPath
        & pwsh -NoProfile $ContractScript
        if ($LASTEXITCODE -ne 0) {
            throw "Contract tests failed for $Label (exit $LASTEXITCODE)."
        }
    }
    finally {
        $env:UE_MCP_DISCOVERY = $previous
    }
}

Push-Location $repoRoot
try {
    $inspector = Join-Path $scriptRoot "Run-McpInspectorCli.ps1"
    $smokeIl2cpp = Join-Path $scriptRoot "Invoke-McpSmoke.ps1"
    $smokeMono = Join-Path $scriptRoot "Run-McpMonoSmoke.ps1"
    $contract = Join-Path $scriptRoot "Run-McpContractTests.ps1"

    Invoke-Step -Label "IL2CPP inspector CLI" -Action { & $inspector -BaseUrl $BaseUrlIl2cpp -AuthToken $AuthToken }
    Invoke-Step -Label "IL2CPP smoke" -Action { & $smokeIl2cpp -BaseUrl $BaseUrlIl2cpp -EnableWriteSmoke:$EnableWriteSmoke }

    Invoke-Step -Label "Mono inspector CLI" -Action { & $inspector -BaseUrl $BaseUrlMono -AuthToken $AuthToken }
    Invoke-Step -Label "Mono smoke" -Action { & $smokeMono -BaseUrl $BaseUrlMono -EnableWriteSmoke:$EnableWriteSmoke }

    Invoke-Step -Label "Contract tests (IL2CPP discovery)" -Action { Invoke-ContractTests -DiscoveryPath "ue-mcp-il2cpp-discovery.json" -Label "IL2CPP" -ContractScript $contract }
    Invoke-Step -Label "Contract tests (Mono discovery)" -Action { Invoke-ContractTests -DiscoveryPath "ue-mcp-mono-discovery.json" -Label "Mono" -ContractScript $contract }

    Write-Host "PASS: All gates succeeded." -ForegroundColor Green
}
catch {
    Write-Error $_
    Write-Host "FAIL: All gates failed." -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
