param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptRoot "..")

Push-Location $repoRoot
try {
    Write-Host "Running UnityExplorer MCP contract tests (configuration: $Configuration)..."
    dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
} finally {
    Pop-Location
}
