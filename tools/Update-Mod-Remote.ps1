<#
    Update-Mod-Remote.ps1

    Copies the built UnityExplorer MelonLoader files to the Space Shooter
    install folders on the Windows Test-VM via SSH/SCP. Uses a staging copy
    in the remote home directory to avoid scp drive-letter issues, then
    performs the final copy with remote PowerShell.

    Defaults:
      IL2CPP: Local ./Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/*
              Remote C:/codex-workspace/space-shooter-build/SpaceShooter_IL2CPP/
      Mono  : Local ./Release/UnityExplorer.MelonLoader.Mono/Mods/*
              Remote C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/Mods/

    Requirements:
    - `scp` and `ssh` available on PATH.
    - SSH access to GPUVM@192.168.178.210 configured (keys or password).

    Usage (from repo root, on Linux/macOS):
      pwsh -NoProfile ./tools/Update-Mod-Remote.ps1

    Target selector (default Il2Cpp):
      -Target Il2Cpp|Mono|Both

    Optional overrides:
      -LocalSource           (IL2CPP source glob)
      -LocalSourceMono       (Mono source glob)
      -RemoteUserHost        (default GPUVM@192.168.178.210)
      -RemoteTarget          (IL2CPP target folder)
      -RemoteTargetMono      (Mono target folder)
      -StopGame              (Stop SpaceShooter.exe remotely before copy)
#>

param(
    [ValidateSet("Il2Cpp", "Mono", "Both")]
    [string]$Target = "Il2Cpp",

    [string]$LocalSource = "./Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/*",
    [string]$LocalSourceMono = "./Release/UnityExplorer.MelonLoader.Mono/Mods/*",

    [string]$RemoteUserHost = "GPUVM@192.168.178.210",
    [string]$RemoteTarget = "C:/codex-workspace/space-shooter-build/SpaceShooter_IL2CPP/",
    [string]$RemoteTargetMono = "C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/Mods/",

    [string]$StagingSubPath = "ue-mcp-stage",
    [switch]$StopGame
)

$ErrorActionPreference = "Stop"

function Ensure-TrailingSlash {
    param([string]$Path)
    if ($Path.EndsWith("/") -or $Path.EndsWith("\\")) { return $Path }
    return "$Path/"
}

function Invoke-RemotePowerShell {
    param(
        [string]$UserHost,
        [string]$Script
    )
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($Script)
    $encoded = [Convert]::ToBase64String($bytes)
    & ssh $UserHost "pwsh -NoProfile -EncodedCommand $encoded"
    if ($LASTEXITCODE -ne 0) {
        throw "Remote PowerShell failed with exit code $LASTEXITCODE"
    }
}

if (-not (Get-Command scp -ErrorAction SilentlyContinue)) {
    Write-Error "The 'scp' command was not found. Please install the OpenSSH client."
}

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    Write-Error "The 'ssh' command was not found. Please install the OpenSSH client."
}

$targets = @()
if ($Target -eq "Il2Cpp" -or $Target -eq "Both") {
    $targets += [pscustomobject]@{ Name = "Il2Cpp"; LocalSource = $LocalSource; RemoteTarget = $RemoteTarget }
}
if ($Target -eq "Mono" -or $Target -eq "Both") {
    $targets += [pscustomobject]@{ Name = "Mono"; LocalSource = $LocalSourceMono; RemoteTarget = $RemoteTargetMono }
}

if (-not $targets) {
    Write-Error "No targets selected."
}

Write-Host "Preparing to copy mods via SSH/SCP (staging -> remote target)..." -ForegroundColor Cyan
Write-Host "  Remote host : $RemoteUserHost"
Write-Host "  Target mode : $Target"

foreach ($t in $targets) {
    $localBase = Split-Path -Path $t.LocalSource -Parent
    if (-not (Test-Path -Path $localBase)) {
        Write-Error "Local source directory does not exist: $localBase"
    }
    if (-not (Get-ChildItem -Path $t.LocalSource -ErrorAction SilentlyContinue)) {
        Write-Error "Local source has no files: $($t.LocalSource)"
    }
}

$stagingRootPosix = "~/$StagingSubPath"
$stopFlagLiteral = if ($StopGame) { '$true' } else { '$false' }

$processScript = @'
$proc = Get-Process -Name SpaceShooter -ErrorAction SilentlyContinue
if ($proc) {{
    Write-Output "WARNING: SpaceShooter.exe is running (PID: $($proc.Id -join ', ')). Files may be locked during copy."
    if ({0}) {{
        Stop-Process -Id ($proc | Select-Object -ExpandProperty Id) -Force
        Write-Output "Stopped SpaceShooter.exe because -StopGame was set."
    }}
}} else {{
    Write-Output "SpaceShooter.exe is not running."
}}
'@ -f $stopFlagLiteral
Invoke-RemotePowerShell -UserHost $RemoteUserHost -Script $processScript

foreach ($t in $targets) {
    $remoteTarget = Ensure-TrailingSlash $t.RemoteTarget
    $stagingPosix = "$stagingRootPosix/$($t.Name)"

    Write-Host "\n=== Deploying $($t.Name) ===" -ForegroundColor Yellow
    Write-Host "  Local : $($t.LocalSource)"
    Write-Host "  Stage : ${RemoteUserHost}:$stagingPosix"
    Write-Host "  Final : ${RemoteUserHost}:$remoteTarget"

    $createStageScript = @'
$stage = Join-Path $HOME "{0}"
New-Item -ItemType Directory -Force -Path $stage | Out-Null
'@ -f "$StagingSubPath/$($t.Name)"
    Invoke-RemotePowerShell -UserHost $RemoteUserHost -Script $createStageScript

    Write-Host "Staging via scp..." -ForegroundColor Yellow
    & scp -r $t.LocalSource "${RemoteUserHost}:$stagingPosix/"
    if ($LASTEXITCODE -ne 0) {
        Write-Error "scp failed for $($t.Name) with exit code $LASTEXITCODE."
    }

    $copyScript = @'
$ErrorActionPreference = "Stop"
$stage = Join-Path $HOME "{0}"
$target = "{1}"
if (-not ($target.EndsWith('\\') -or $target.EndsWith('/'))) {{ $target = "$target/" }}
New-Item -ItemType Directory -Force -Path $target | Out-Null
Copy-Item -Path (Join-Path $stage '*') -Destination $target -Recurse -Force
Write-Output "Copied from $stage to $target."
'@ -f "$StagingSubPath/$($t.Name)", $remoteTarget
    Write-Host "Copying from staging to final target with remote PowerShell..." -ForegroundColor Yellow
    Invoke-RemotePowerShell -UserHost $RemoteUserHost -Script $copyScript

    Write-Host "Completed $($t.Name)." -ForegroundColor Green
}

Write-Host "All selected targets completed successfully." -ForegroundColor Green
