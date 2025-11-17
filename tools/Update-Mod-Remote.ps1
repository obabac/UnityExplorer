<#
    update-mods.ps1

    Copies the built UnityExplorer MelonLoader IL2CPP CoreCLR files
    from the local repo into the Soulstone Survivors install folder
    on the Windows test VM over SSH/SCP.

    Defaults:
      From (local):
        ./UnityExplorer/Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/*
      To (remote, on GPUVM):
        C:/Program Files (x86)/Steam/steamapps/common/Soulstone Survivors/

    Requirements:
    - `scp` (OpenSSH client) available on PATH.
    - SSH access to GPUVM@192.168.178.210 configured (keys or password).

    Usage (from repo root, on Linux/macOS):
      pwsh ./tools/update-mods.ps1

    Optional overrides:
      pwsh ./tools/update-mods.ps1 `
        -LocalSource "./UnityExplorer/Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/*" `
        -RemoteUserHost "GPUVM@192.168.178.210" `
        -RemoteTarget "C:/Program Files (x86)/Steam/steamapps/common/Soulstone Survivors/"
#>

param(
    [string]$LocalSource = "./Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/*",
    [string]$RemoteUserHost = "GPUVM@192.168.178.210",
    [string]$RemoteTarget = "C:/Program Files (x86)/Steam/steamapps/common/Soulstone Survivors/"
)

$ErrorActionPreference = "Stop"

Write-Host "Preparing to copy mods via SSH/SCP..." -ForegroundColor Cyan
Write-Host "  Local  : $LocalSource"
Write-Host "  Remote : ${RemoteUserHost}:$RemoteTarget"

if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
    Write-Error "The 'scp' command was not found. Please install the OpenSSH client (provides 'scp') on this machine."
    exit 1
}

if (-not (Test-Path -Path (Split-Path -Path $LocalSource -Parent))) {
    Write-Error "Local source directory does not exist: $(Split-Path -Path $LocalSource -Parent)"
    exit 1
}

# Ensure the remote target ends with a slash so contents are copied into the folder.
if (-not $RemoteTarget.EndsWith("/")) {
    $RemoteTarget += "/"
}

$remotePath = "${RemoteUserHost}:$RemoteTarget"

Write-Host "Running: scp -r $LocalSource $remotePath" -ForegroundColor Yellow

& scp -r $LocalSource $remotePath

if ($LASTEXITCODE -ne 0) {
    Write-Error "scp failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

Write-Host "Copy completed successfully." -ForegroundColor Green
