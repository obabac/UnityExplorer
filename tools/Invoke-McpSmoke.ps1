param(
    [string]$BaseUrl,
    [string]$DiscoveryPath = $env:UE_MCP_DISCOVERY,
    [int]$LogCount = 20,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"

function Get-BaseUrl {
    param([string]$BaseUrl, [string]$DiscoveryPath)
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

try {
    $resolvedBase = Get-BaseUrl -BaseUrl $BaseUrl -DiscoveryPath $DiscoveryPath
    $messageUrl = "$resolvedBase/message"

    Write-Host "MCP base URL: $resolvedBase"
    $init = Invoke-McpRpc -Id "init" -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"
        clientInfo = @{ name = "mcp-smoke"; version = "0.1.0" }
        capabilities = @{}
    } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds

    Invoke-McpRpc -Id "notified" -Method "notifications/initialized" -Params @{} -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null

    $tools = Invoke-McpRpc -Id "list" -Method "list_tools" -Params @{} -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $toolNames = ($tools.result.tools | ForEach-Object { $_.name }) -join ", "
    Write-Host "Tools: $toolNames"

    $statusTool = Invoke-McpRpc -Id "get-status" -Method "call_tool" -Params @{ name = "GetStatus"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $statusContent = ($statusTool.result.content | Where-Object { $_.json -ne $null -or $_.text -ne $null })
    $statusPart = $statusContent[0]
    $status = if ($statusPart.json) { $statusPart.json } else { ($statusPart.text | ConvertFrom-Json) }

    $logsTool = Invoke-McpRpc -Id "tail-logs" -Method "call_tool" -Params @{ name = "TailLogs"; arguments = @{ count = $LogCount } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $logsContent = ($logsTool.result.content | Where-Object { $_.json -ne $null -or $_.text -ne $null })
    $logsPart = $logsContent[0]
    $logs = if ($logsPart.json) { $logsPart.json } else { ($logsPart.text | ConvertFrom-Json) }

    $readStatus = Invoke-McpRpc -Id "read-status" -Method "read_resource" -Params @{ uri = "unity://status" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $readScenes = Invoke-McpRpc -Id "read-scenes" -Method "read_resource" -Params @{ uri = "unity://scenes" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
    $readLogs = Invoke-McpRpc -Id "read-logs" -Method "read_resource" -Params @{ uri = "unity://logs/tail?count=$LogCount" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds

    $statusDoc = ($readStatus.result.contents[0].text | ConvertFrom-Json)
    $sceneDoc = ($readScenes.result.contents[0].text | ConvertFrom-Json)
    $logDoc = ($readLogs.result.contents[0].text | ConvertFrom-Json)

    Write-Host "Status (tool): $($status.ExplorerVersion) / Ready=$($status.Ready) / Scenes=$($status.ScenesLoaded)"
    Write-Host "Scenes (read): $($sceneDoc.Total) total"
    Write-Host "Logs (tool): $($logs.Items.Count) items (count requested: $LogCount)"
    Write-Host "Logs (read): $($logDoc.Items.Count) items"
}
catch {
    Write-Error $_
    exit 1
}
