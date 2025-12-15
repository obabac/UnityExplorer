param(
    [string]$BaseUrl = "http://192.168.178.210:51477",
    [string]$Transport = "streamable-http",
    [string]$LogPath = "C:\\codex-workspace\\mcp-inspector-ui.log",
    [switch]$NoBrowser,
    [int]$Port = 6277
)

$ErrorActionPreference = "Stop"

function Assert-Npx {
    $cmd = Get-Command npx -ErrorAction SilentlyContinue
    if (-not $cmd) {
        $cmd = Get-Command npx.cmd -ErrorAction SilentlyContinue
    }
    if (-not $cmd) {
        throw "npx is required. Install Node.js/npm so @modelcontextprotocol/inspector can launch."
    }
}

function Start-InspectorProcess {
    param(
        [string]$BaseUrl,
        [int]$Port
    )

    $logDir = Split-Path -Parent $LogPath
    if ($logDir -and -not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir | Out-Null
    }

    if (Test-Path $LogPath) {
        Remove-Item $LogPath -Force
    }

    $errorLog = "$LogPath.err"
    if (Test-Path $errorLog) {
        Remove-Item $errorLog -Force
    }

    Write-Host "Starting inspector UI (logging to $LogPath)..." -ForegroundColor Cyan
    $cmdLine = "set `"PORT=$Port`" && set `"DANGEROUSLY_OMIT_AUTH=true`" && npx.cmd --yes @modelcontextprotocol/inspector@latest -- --transport http --server-url `"$BaseUrl`""
    $proc = Start-Process -FilePath "cmd.exe" -ArgumentList "/c", $cmdLine -RedirectStandardOutput $LogPath -RedirectStandardError $errorLog -PassThru -WindowStyle Hidden
    $global:LastInspectorErrorLog = $errorLog
    return $proc
}

function Get-InspectorUrlFromLog {
    param(
        [int]$TimeoutSeconds = 30,
        [int]$Port
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $LogPath) {
            $text = Get-Content $LogPath -Raw
            if (-not $text) { $text = "" }
            $match = [regex]::Match($text, "http://localhost:\d+[^\s]*")
            if ($match.Success) {
                return $match.Value.Trim()
            }
            if ($text -match "localhost:$Port") {
                return "http://localhost:$Port/"
            }
        }
        Start-Sleep -Seconds 1
    }

    throw "Inspector UI URL not found in $LogPath within $TimeoutSeconds seconds. Check the log for errors."
}

try {
    Assert-Npx

    $proc = Start-InspectorProcess -BaseUrl $BaseUrl -Port $Port

    $uiUrl = Get-InspectorUrlFromLog -TimeoutSeconds 45 -Port $Port

    Add-Type -AssemblyName System.Web
    $builder = [System.UriBuilder]$uiUrl
    $query = [System.Web.HttpUtility]::ParseQueryString($builder.Query)
    if (-not $query["MCP_PROXY_AUTH_TOKEN"] -and $env:MCP_PROXY_AUTH_TOKEN) {
        $query["MCP_PROXY_AUTH_TOKEN"] = $env:MCP_PROXY_AUTH_TOKEN
    }
    $query["transport"] = $Transport
    $query["serverUrl"] = $BaseUrl
    $builder.Query = $query.ToString()

    $launchUrl = $builder.Uri.AbsoluteUri
    Write-Host "Inspector UI: $launchUrl" -ForegroundColor Green
    Write-Host "Process Id : $($proc.Id)" -ForegroundColor DarkGray
    Write-Host "Port       : $Port" -ForegroundColor DarkGray
    Write-Host "Log file   : $LogPath" -ForegroundColor Yellow
    Write-Host "Error log  : $LogPath.err" -ForegroundColor DarkGray

    if (-not $NoBrowser) {
        $edge = Get-Command "msedge.exe" -ErrorAction SilentlyContinue
        if (-not $edge) {
            $edge = Get-Command "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe" -ErrorAction SilentlyContinue
        }

        if ($edge) {
            Start-Process -FilePath $edge.Source -ArgumentList $launchUrl
        }
        else {
            Write-Warning "Edge was not found. Open the URL manually."
        }
    }
}
catch {
    Write-Error $_
    exit 1
}
