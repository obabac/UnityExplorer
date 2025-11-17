param(
    # Maximum time to wait for UnityExplorer / MelonLoader to finish startup
    [int]$TimeoutSeconds = 60,
    # How often to re-check the log while waiting
    [int]$PollIntervalSeconds = 2,
    # If set, stream the log live (tail -f style) instead of waiting for readiness
    [switch]$Stream
)

$ErrorActionPreference = "Stop"

$remoteUserHost = "GPUVM@192.168.178.210"
$logPath = "C:\Program Files (x86)\Steam\steamapps\common\Soulstone Survivors\MelonLoader\Latest.log"

if ($Stream) {
    Write-Host "Streaming MelonLoader log from $remoteUserHost..." -ForegroundColor Cyan
    Write-Host "  Log path : $logPath"
    Write-Host "  (press Ctrl+C to stop)" -ForegroundColor Yellow

    ssh -T $remoteUserHost pwsh -NoLogo -NoProfile -Command "Get-Content '$logPath' -Tail 200 -Wait"
    exit $LASTEXITCODE
}

Write-Host "Waiting for UnityExplorer to finish startup (via MelonLoader log) on $remoteUserHost..." -ForegroundColor Cyan
Write-Host "  Log path : $logPath"
Write-Host "  Timeout  : $TimeoutSeconds s (poll every $PollIntervalSeconds s)"

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

while ($true) {
    $result = ssh -T $remoteUserHost pwsh -NoLogo -NoProfile -Command "
        if (Test-Path '$logPath') {
            Get-Content '$logPath' |
                Select-String -SimpleMatch -Pattern 'UnityExplorer v','missing the following dependencies' |
                Select-Object -First 1
        }
    "

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "ssh exited with code $LASTEXITCODE while reading the log; retrying until timeout."
    }

    if ($result) {
        break
    }

    if ((Get-Date) -gt $deadline) {
        throw "UnityExplorer did not finish startup (no matching log lines) within $TimeoutSeconds seconds. Check '$logPath' manually."
    }

    Start-Sleep -Seconds $PollIntervalSeconds
}

Write-Host "UnityExplorer startup detected in log. Fetching log contents..." -ForegroundColor Green

ssh -T $remoteUserHost pwsh -NoLogo -NoProfile -Command "Get-Content '$logPath'"
