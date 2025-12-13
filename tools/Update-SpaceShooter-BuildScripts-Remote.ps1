param(
    [string]$LocalPath = "./tools/space-shooter/BuildCommands.cs",
    [string]$RemoteUserHost = "GPUVM@192.168.178.210",
    [string]$RemoteIl2CppProjectPath = "C:/codex-workspace/space-shooter/unity-space-shooter-2019",
    [string]$RemoteMonoProjectPath = "C:/codex-workspace/space-shooter/unity-space-shooter-2019-mono"
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
    Write-Error "The 'scp' command was not found on PATH. Install the OpenSSH client to continue."
    exit 1
}

if (-not (Test-Path -Path $LocalPath)) {
    Write-Error "Local build script not found: $LocalPath"
    exit 1
}

function Sync-Script {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath
    )

    $remoteEditorDir = "$ProjectPath/Assets/Editor"
    $remoteDestination = "$remoteEditorDir/BuildCommands.cs"

    Write-Host "  → Syncing to ${ProjectPath}" -ForegroundColor Cyan
    ssh -T $RemoteUserHost pwsh -NoLogo -NoProfile -Command "New-Item -ItemType Directory -Force -Path '$remoteEditorDir'"
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to ensure remote editor folder exists at $remoteEditorDir (exit $LASTEXITCODE)."
    }

    & scp $LocalPath "${RemoteUserHost}:$remoteDestination"
    if ($LASTEXITCODE -ne 0) {
        throw "scp failed for $remoteDestination with exit code $LASTEXITCODE."
    }

    Write-Host "    Copied BuildCommands.cs to $remoteDestination" -ForegroundColor Green
}

Write-Host "Syncing BuildCommands.cs to Test-VM projects..." -ForegroundColor Cyan
Write-Host "  Local : $LocalPath"
Write-Host "  Remote: $RemoteUserHost" -ForegroundColor Cyan

Sync-Script -ProjectPath $RemoteIl2CppProjectPath
Sync-Script -ProjectPath $RemoteMonoProjectPath

Write-Host "Sync completed." -ForegroundColor Green
