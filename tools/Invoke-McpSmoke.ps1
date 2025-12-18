param(
    [string]$BaseUrl,
    [string]$DiscoveryPath = $env:UE_MCP_DISCOVERY,
    [int]$LogCount = 20,
    [int]$TimeoutSeconds = 10,
    [switch]$EnableWriteSmoke
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

function Get-JsonContent {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Result
    )
    $contentNode = $Result.result.content | Where-Object { $_.json -ne $null -or $_.text -ne $null } | Select-Object -First 1
    if (-not $contentNode) { return $null }
    if ($contentNode.json) { return $contentNode.json }
    try { return ($contentNode.text | ConvertFrom-Json) } catch { return $null }
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

    $writeSummary = $null
    if ($EnableWriteSmoke) {
        Write-Host "[smoke] guarded write smoke (console scripts + startup)"
        $scriptName = "mcp-smoke-$(New-Guid).cs"
        $scriptContent = "return 21+21;"
        $startupContent = 'return "startup-smoke";'
        $resetConfig = @{ name = "SetConfig"; arguments = @{ allowWrites = $false; requireConfirm = $true; enableConsoleEval = $false } }
        try {
            $prevCfg = Get-JsonContent -Result (Invoke-McpRpc -Id "get-config-before-smoke" -Method "call_tool" -Params @{ name = "GetConfig"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds)
            if ($prevCfg) {
                if ($prevCfg.allowWrites -ne $null) { $resetConfig.arguments.allowWrites = $prevCfg.allowWrites }
                if ($prevCfg.requireConfirm -ne $null) { $resetConfig.arguments.requireConfirm = $prevCfg.requireConfirm }
                if ($prevCfg.enableConsoleEval -ne $null) { $resetConfig.arguments.enableConsoleEval = $prevCfg.enableConsoleEval }
            }

            Invoke-McpRpc -Id "set-config-enable" -Method "call_tool" -Params @{ name = "SetConfig"; arguments = @{ allowWrites = $true; requireConfirm = $true; enableConsoleEval = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null

            $writeRes = Invoke-McpRpc -Id "write-console-script" -Method "call_tool" -Params @{ name = "WriteConsoleScript"; arguments = @{ path = $scriptName; content = $scriptContent; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $writeJson = Get-JsonContent -Result $writeRes

            $runRes = Invoke-McpRpc -Id "run-console-script" -Method "call_tool" -Params @{ name = "RunConsoleScript"; arguments = @{ path = $scriptName; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $runJson = Get-JsonContent -Result $runRes

            $delRes = Invoke-McpRpc -Id "delete-console-script" -Method "call_tool" -Params @{ name = "DeleteConsoleScript"; arguments = @{ path = $scriptName; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $delJson = Get-JsonContent -Result $delRes

            $writeStartupRes = Invoke-McpRpc -Id "write-startup" -Method "call_tool" -Params @{ name = "WriteStartupScript"; arguments = @{ content = $startupContent; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $writeStartupJson = Get-JsonContent -Result $writeStartupRes

            $disableStartupRes = Invoke-McpRpc -Id "disable-startup" -Method "call_tool" -Params @{ name = "SetStartupScriptEnabled"; arguments = @{ enabled = $false; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $disableStartupJson = Get-JsonContent -Result $disableStartupRes

            $runStartupRes = Invoke-McpRpc -Id "run-startup" -Method "call_tool" -Params @{ name = "RunStartupScript"; arguments = @{ confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $runStartupJson = Get-JsonContent -Result $runStartupRes

            $enableStartupRes = Invoke-McpRpc -Id "enable-startup" -Method "call_tool" -Params @{ name = "SetStartupScriptEnabled"; arguments = @{ enabled = $true; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $enableStartupJson = Get-JsonContent -Result $enableStartupRes

            $writeSummary = [ordered]@{
                writeConsole  = $writeJson
                runConsole    = $runJson
                deleteConsole = $delJson
                writeStartup  = $writeStartupJson
                disableStartup = $disableStartupJson
                runStartup    = $runStartupJson
                enableStartup = $enableStartupJson
            }
        }
        finally {
            try { Invoke-McpRpc -Id "cleanup-startup-enabled" -Method "call_tool" -Params @{ name = "SetStartupScriptEnabled"; arguments = @{ enabled = $true; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null } catch { }
            try { Invoke-McpRpc -Id "cleanup-script" -Method "call_tool" -Params @{ name = "DeleteConsoleScript"; arguments = @{ path = $scriptName; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null } catch { }
            try { Invoke-McpRpc -Id "reset-config" -Method "call_tool" -Params $resetConfig -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null } catch { }
        }
    }

    Write-Host "Status (tool): $($status.ExplorerVersion) / Ready=$($status.Ready) / Scenes=$($status.ScenesLoaded)"
    Write-Host "Scenes (read): $($sceneDoc.Total) total"
    Write-Host "Logs (tool): $($logs.Items.Count) items (count requested: $LogCount)"
    Write-Host "Logs (read): $($logDoc.Items.Count) items"
    if ($EnableWriteSmoke -and $writeSummary) {
        $runResultText = if ($writeSummary.runConsole) { $writeSummary.runConsole.result } else { $null }
        $startupResultText = if ($writeSummary.runStartup) { $writeSummary.runStartup.result } else { $null }
        Write-Host "Write smoke: console ok=$($writeSummary.writeConsole.ok) runResult=$runResultText startupOk=$($writeSummary.writeStartup.ok) runStartupResult=$startupResultText"
    }
}
catch {
    Write-Error $_
    exit 1
}
