param(
    [string]$BaseUrl,
    [string]$DiscoveryPath = $env:UE_MCP_DISCOVERY,
    [int]$LogCount = 10,
    [int]$StreamLines = 3,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

function Get-BaseUrl {
    param(
        [string]$BaseUrl,
        [string]$DiscoveryPath
    )
    if ($BaseUrl) { return $BaseUrl.TrimEnd('/') }
    if (-not $DiscoveryPath) { $DiscoveryPath = Join-Path $env:TEMP "unity-explorer-mcp.json" }
    if (-not (Test-Path $DiscoveryPath)) {
        throw "Discovery file not found: $DiscoveryPath (set -BaseUrl or UE_MCP_DISCOVERY)."
    }
    $info = Get-Content $DiscoveryPath -Raw | ConvertFrom-Json
    if (-not $info.baseUrl) { throw "baseUrl missing in discovery file: $DiscoveryPath" }
    return ($info.baseUrl.TrimEnd('/'))
}

function Invoke-McpRpc {
    param(
        [string]$Id,
        [string]$Method,
        [hashtable]$Params,
        [string]$MessageUrl,
        [int]$TimeoutSeconds
    )
    $payload = @{
        jsonrpc = "2.0"
        id = $Id
        method = $Method
        params = $Params
    }
    $json = $payload | ConvertTo-Json -Depth 8
    $res = Invoke-RestMethod -Method Post -Uri $MessageUrl -Body $json -ContentType "application/json" -TimeoutSec $TimeoutSeconds
    if ($res.error) {
        $msg = $res.error.message
        $kind = $res.error.data.kind
        throw "MCP error ($kind): $msg"
    }
    return $res
}

function Read-Chunk {
    param([System.IO.StreamReader]$Reader)
    $lenLine = $Reader.ReadLine()
    if ([string]::IsNullOrWhiteSpace($lenLine)) { return $null }
    $len = 0
    if (-not [int]::TryParse($lenLine, [System.Globalization.NumberStyles]::HexNumber, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$len)) {
        return $null
    }
    if ($len -le 0) { return $null }
    $buffer = New-Object char[] $len
    $read = $Reader.ReadBlock($buffer, 0, $len)
    if ($read -le 0) { return $null }
    $json = -join $buffer[0..($read - 1)]
    $Reader.ReadLine() | Out-Null
    return $json
}

function Open-StreamEvents {
    param(
        [string]$MessageUrl,
        [int]$TimeoutSeconds
    )
    $payload = @{
        jsonrpc = "2.0"
        id = "events"
        method = "stream_events"
        params = @{}
    } | ConvertTo-Json -Depth 4

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromMilliseconds(-1)
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $MessageUrl)
    $request.Content = New-Object System.Net.Http.StringContent($payload, [System.Text.Encoding]::UTF8, "application/json")
    $cts = New-Object System.Threading.CancellationTokenSource
    $cts.CancelAfter([TimeSpan]::FromSeconds($TimeoutSeconds))
    $response = $client.SendAsync($request, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead, $cts.Token).Result
    $cts.Dispose()
    $response.EnsureSuccessStatusCode()
    $stream = $response.Content.ReadAsStream()
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $false, 1024, $false)
    return @{ Client = $client; Response = $response; Reader = $reader }
}

function Read-StreamEvents {
    param(
        [System.IO.StreamReader]$Reader,
        [int]$Lines
    )
    $events = @()
    for ($i = 0; $i -lt $Lines; $i++) {
        if ($Reader.EndOfStream) { break }
        $chunk = Read-Chunk -Reader $Reader
        if (-not $chunk) { break }
        $events += $chunk
        Write-Host "[event] $chunk"
    }
    return $events
}

try {
    $resolvedBase = Get-BaseUrl -BaseUrl $BaseUrl -DiscoveryPath $DiscoveryPath
    $messageUrl = "$resolvedBase/message"
    Write-Host "MCP base URL: $resolvedBase"

    $init = Invoke-McpRpc -Id "init" -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"
        clientInfo = @{ name = "mono-mcp-smoke"; version = "0.1.0" }
        capabilities = @{}
    } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds

    Invoke-McpRpc -Id "notified" -Method "notifications/initialized" -Params @{} -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null

    $tools = Invoke-McpRpc -Id "list" -Method "list_tools" -Params @{} -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds

    $statusTool = Invoke-McpRpc -Id "get-status" -Method "call_tool" -Params @{ name = "GetStatus"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $logsTool = Invoke-McpRpc -Id "tail-logs" -Method "call_tool" -Params @{ name = "TailLogs"; arguments = @{ count = $LogCount } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $mousePickTool = Invoke-McpRpc -Id "mouse-pick" -Method "call_tool" -Params @{ name = "MousePick"; arguments = @{ mode = "world" } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds

    $readStatus = Invoke-McpRpc -Id "read-status" -Method "read_resource" -Params @{ uri = "unity://status" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $readScenes = Invoke-McpRpc -Id "read-scenes" -Method "read_resource" -Params @{ uri = "unity://scenes" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $readLogs = Invoke-McpRpc -Id "read-logs" -Method "read_resource" -Params @{ uri = "unity://logs/tail?count=$LogCount" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds

    $streamHandle = $null
    $events = @()
    try {
        $streamHandle = Open-StreamEvents -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
        Invoke-McpRpc -Id "event-probe" -Method "call_tool" -Params @{ name = "GetStatus"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null
        $events = Read-StreamEvents -Reader $streamHandle.Reader -Lines $StreamLines
    }
    finally {
        if ($streamHandle -and $streamHandle.Reader) { $streamHandle.Reader.Dispose() }
        if ($streamHandle -and $streamHandle.Response) { $streamHandle.Response.Dispose() }
        if ($streamHandle -and $streamHandle.Client) { $streamHandle.Client.Dispose() }
    }

    $status = ($statusTool.result.content | Where-Object { $_.type -eq "json" })[0].json
    $logs = ($logsTool.result.content | Where-Object { $_.type -eq "json" })[0].json
    $mousePick = ($mousePickTool.result.content | Where-Object { $_.type -eq "json" })[0].json
    if (-not $mousePick) { throw "MousePick returned no payload" }
    if (-not $mousePick.Mode) { throw "MousePick returned empty Mode" }

    $readStatusDoc = ($readStatus.result.contents[0].text | ConvertFrom-Json)
    $readScenesDoc = ($readScenes.result.contents[0].text | ConvertFrom-Json)
    $readLogsDoc = ($readLogs.result.contents[0].text | ConvertFrom-Json)

    $toolResultEvent = $null
    foreach ($chunk in $events) {
        try {
            $obj = $chunk | ConvertFrom-Json
            if ($obj.method -eq "notification" -and $obj.params -and $obj.params.event -eq "tool_result") {
                $toolResultEvent = $obj
                break
            }
        } catch {
            continue
        }
    }
    if (-not $toolResultEvent) { throw "stream_events produced no tool_result notification" }

    Write-Host "Tools: $($tools.result.tools.Count) returned"
    Write-Host "Status: Ready=$($status.Ready) Scenes=$($status.ScenesLoaded)"
    Write-Host "MousePick: Mode=$($mousePick.Mode) Hit=$($mousePick.Hit)"
    Write-Host "Scenes: $($readScenesDoc.Total) total"
    Write-Host "Logs (tool): $($logs.Items.Count) items (requested $LogCount)"
    Write-Host "Logs (read): $($readLogsDoc.Items.Count) items"
    Write-Host "Stream events captured: $($events.Count) (tool_result observed=$($toolResultEvent -ne $null))"
}
catch {
    Write-Error $_
    exit 1
}
