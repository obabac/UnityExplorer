param(
    [string]$RemoteUserHost = "GPUVM@192.168.178.210",
    [string]$UnityPath = "C:/Program Files/Unity 2021.3.45f1/Editor/Unity.exe",
    [string]$RemoteIl2CppProjectPath = "C:/codex-workspace/space-shooter/unity-space-shooter-2019",
    [string]$RemoteMonoProjectPath = "C:/codex-workspace/space-shooter/unity-space-shooter-2019-mono",
    [string]$LogDir = "C:/codex-workspace/space-shooter-build/logs",
    [switch]$SkipIl2Cpp,
    [switch]$SkipMono
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command ssh -ErrorAction SilentlyContinue)) {
    Write-Error "The 'ssh' command was not found on PATH. Install the OpenSSH client to continue."
    exit 1
}

function Invoke-RemoteBuild {
    param(
        [Parameter(Mandatory = $true)][string]$MethodName,
        [Parameter(Mandatory = $true)][string]$LogLabel,
        [Parameter(Mandatory = $true)][string]$ProjectPath
    )

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $logPath = "$LogDir/$LogLabel-$timestamp.log"

    Write-Host "Starting $LogLabel build on $RemoteUserHost" -ForegroundColor Cyan
    Write-Host "  Method : $MethodName"
    Write-Host "  Project: $ProjectPath"
    Write-Host "  Log    : $logPath"

    $remoteScript = @"
`$ErrorActionPreference = 'Stop'
Write-Host "[remote] Ensuring log directory: $LogDir"
New-Item -ItemType Directory -Force -Path '$LogDir' | Out-Null
`$argumentList = @(
  "-batchmode",
  "-nographics",
  "-projectPath", "$ProjectPath",
  "-executeMethod", "$MethodName",
  "-buildTarget", "Win64",
  "-logFile", "$logPath",
  "-quit"
)
Write-Host "[remote] Launching Unity -> $MethodName"
`$proc = Start-Process -FilePath '$UnityPath' -ArgumentList `$argumentList -Wait -PassThru
`$code = `$proc.ExitCode
Write-Host "[remote] Unity exit code: `$code"
exit `$code
"@

    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($remoteScript))
    ssh -T $RemoteUserHost pwsh -NoLogo -NoProfile -EncodedCommand $encoded
    if ($LASTEXITCODE -ne 0) {
        throw "Unity build '$MethodName' failed with exit code $LASTEXITCODE. Check log at $logPath"
    }

    Write-Host "$LogLabel build completed" -ForegroundColor Green
}

if (-not $SkipIl2Cpp) {
    Invoke-RemoteBuild -MethodName "SpaceShooter.EditorTools.BuildCommands.BuildWindows64Il2Cpp" -LogLabel "build-il2cpp" -ProjectPath $RemoteIl2CppProjectPath
}
else {
    Write-Host "Skipping IL2CPP build as requested." -ForegroundColor Yellow
}

if (-not $SkipMono) {
    Invoke-RemoteBuild -MethodName "SpaceShooter.EditorTools.BuildCommands.BuildWindows64Mono" -LogLabel "build-mono" -ProjectPath $RemoteMonoProjectPath
}
else {
    Write-Host "Skipping Mono build as requested." -ForegroundColor Yellow
}
