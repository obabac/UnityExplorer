param(
    [string]$BaseUrl,
    [string]$DiscoveryPath = $env:UE_MCP_DISCOVERY,
    [int]$LogCount = 10,
    [int]$StreamLines = 3,
    [int]$TimeoutSeconds = 10,
    [switch]$EnableWriteSmoke
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

function Read-Chunk {
    param([System.IO.StreamReader]$Reader, [int]$TimeoutMs = 5000)
    $lineTask = $Reader.ReadLineAsync()
    if (-not $lineTask.Wait($TimeoutMs)) { return $null }
    $line = $lineTask.Result
    if ([string]::IsNullOrWhiteSpace($line)) { return $null }
    return $line
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
        Write-Host "[mono-smoke] waiting for event $i"
        try { $chunk = Read-Chunk -Reader $Reader } catch { Write-Host "[mono-smoke] stream read error: $_"; break }
        if (-not $chunk) { Write-Host "[mono-smoke] no chunk"; break }
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

    $writeResult = $null
    if ($EnableWriteSmoke) {
        Write-Host "[mono-smoke] guarded write smoke (SpawnTestUi -> SetMember(Image.color) -> Reparent -> DestroyObject -> DestroyTestUi + SetTimeScale)"
        $resetParams = @{ name = "SetConfig"; arguments = @{ allowWrites = $false; requireConfirm = $true; reflectionAllowlistMembers = @() } }
        $spawned = $false
        $destroyUiJson = $null
        $destroyBlockJson = $null
        $reparentJson = $null
        $setMemberJson = $null
        $selectionEvent = $null
        $blockIds = @()
        try {
            Invoke-McpRpc -Id "set-config-enable" -Method "call_tool" -Params @{ name = "SetConfig"; arguments = @{ allowWrites = $true; requireConfirm = $true; reflectionAllowlistMembers = @("UnityEngine.UI.Image.color") } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null

            $spawn = Invoke-McpRpc -Id "spawn-testui" -Method "call_tool" -Params @{ name = "SpawnTestUi"; arguments = @{ confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $spawnJson = Get-JsonContent -Result $spawn
            if (-not $spawnJson -or -not $spawnJson.ok) { throw "SpawnTestUi returned ok=false" }
            $spawned = $true
            if ($spawnJson.blocks) {
                foreach ($b in $spawnJson.blocks) {
                    if ($b.id) { $blockIds += $b.id }
                }
            }

            if ($blockIds.Count -lt 2) {
                Write-Host "[mono-smoke] falling back to MousePick UI for block ids"
                $pickLeft = Invoke-McpRpc -Id "mouse-ui-left" -Method "call_tool" -Params @{ name = "MousePick"; arguments = @{ mode = "ui"; normalized = $true; x = 0.35; y = 0.5 } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
                $pickLeftJson = Get-JsonContent -Result $pickLeft
                if ($pickLeftJson -and $pickLeftJson.Items -and $pickLeftJson.Items.Count -gt 0) { $blockIds += $pickLeftJson.Items[0].Id }

                $pickRight = Invoke-McpRpc -Id "mouse-ui-right" -Method "call_tool" -Params @{ name = "MousePick"; arguments = @{ mode = "ui"; normalized = $true; x = 0.65; y = 0.5 } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
                $pickRightJson = Get-JsonContent -Result $pickRight
                if ($pickRightJson -and $pickRightJson.Items -and $pickRightJson.Items.Count -gt 0) { $blockIds += $pickRightJson.Items[0].Id }
            }

            $blockIds = $blockIds | Where-Object { $_ } | Select-Object -Unique
            if ($blockIds.Count -lt 2) { throw "Could not resolve two test UI block ids" }

            $childId = $blockIds[1]
            $parentId = $blockIds[0]

            $colorJson = (@{ r = 0.25; g = 0.5; b = 0.75; a = 0.9 } | ConvertTo-Json -Compress)
            $setMember = Invoke-McpRpc -Id "set-member" -Method "call_tool" -Params @{ name = "SetMember"; arguments = @{ objectId = $parentId; componentType = "UnityEngine.UI.Image"; member = "color"; jsonValue = $colorJson; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $setMemberJson = Get-JsonContent -Result $setMember
            if (-not $setMemberJson -or -not $setMemberJson.ok) { throw "SetMember returned ok=false" }

            $reparent = Invoke-McpRpc -Id "reparent" -Method "call_tool" -Params @{ name = "Reparent"; arguments = @{ objectId = $childId; newParentId = $parentId; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $reparentJson = Get-JsonContent -Result $reparent
            if (-not $reparentJson -or -not $reparentJson.ok) { throw "Reparent returned ok=false" }

            $destroyBlock = Invoke-McpRpc -Id "destroy-block" -Method "call_tool" -Params @{ name = "DestroyObject"; arguments = @{ objectId = $childId; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $destroyBlockJson = Get-JsonContent -Result $destroyBlock
            if (-not $destroyBlockJson -or -not $destroyBlockJson.ok) { throw "DestroyObject returned ok=false" }

            $setTime = Invoke-McpRpc -Id "set-timescale" -Method "call_tool" -Params @{ name = "SetTimeScale"; arguments = @{ value = 1.0; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $setTimeJson = Get-JsonContent -Result $setTime
            if (-not $setTimeJson -or -not $setTimeJson.ok) { throw "SetTimeScale returned ok=false" }

            $getTime = Invoke-McpRpc -Id "get-timescale" -Method "call_tool" -Params @{ name = "GetTimeScale"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $getTimeJson = Get-JsonContent -Result $getTime
            if (-not $getTimeJson -or -not $getTimeJson.ok) { throw "GetTimeScale returned ok=false" }

            if ($blockIds.Count -gt 0) {
                $selectionStream = $null
                $selectionEvents = @()
                try {
                    $selectionStream = Open-StreamEvents -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
                    $select = Invoke-McpRpc -Id "select-block" -Method "call_tool" -Params @{ name = "SelectObject"; arguments = @{ objectId = $parentId } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
                    $selectJson = Get-JsonContent -Result $select
                    if (-not $selectJson -or -not $selectJson.ok) { throw "SelectObject returned ok=false" }

                    $selectionEvents = Read-StreamEvents -Reader $selectionStream.Reader -Lines ([Math]::Max($StreamLines, 5))
                    foreach ($chunk in $selectionEvents) {
                        try {
                            $obj = $chunk | ConvertFrom-Json
                            if ($obj.method -eq "notification" -and $obj.params -and $obj.params.event -eq "selection") {
                                $selectionEvent = $obj
                                break
                            }
                        } catch {
                            continue
                        }
                    }

                    if (-not $selectionEvent) { throw "stream_events produced no selection notification" }
                }
                finally {
                    if ($selectionStream) {
                        try { $selectionStream.Reader.Dispose() } catch { }
                        try { $selectionStream.Response.Dispose() } catch { }
                        try { $selectionStream.Client.Dispose() } catch { }
                    }
                }
            }

            $destroyUi = Invoke-McpRpc -Id "destroy-testui" -Method "call_tool" -Params @{ name = "DestroyTestUi"; arguments = @{ confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $destroyUiJson = Get-JsonContent -Result $destroyUi
            if (-not $destroyUiJson -or -not $destroyUiJson.ok) { throw "DestroyTestUi returned ok=false" }

            $writeResult = [ordered]@{
                blocks         = $blockIds
                spawn          = $spawnJson
                setMember      = $setMemberJson
                reparent       = $reparentJson
                destroyBlock   = $destroyBlockJson
                destroyUi      = $destroyUiJson
                time           = $getTimeJson
                selectionEvent = $selectionEvent
            }
        }
        finally {
            if ($spawned -and -not $destroyUiJson) {
                try {
                    $destroyUiJson = Get-JsonContent -Result (Invoke-McpRpc -Id "destroy-testui-final" -Method "call_tool" -Params @{ name = "DestroyTestUi"; arguments = @{ confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds)
                } catch { Write-Warning "[mono-smoke] cleanup destroy failed: $_" }
            }
            try { Invoke-McpRpc -Id "set-config-reset" -Method "call_tool" -Params $resetParams -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null } catch { Write-Warning "[mono-smoke] failed to reset config: $_" }
        }
    }

    Write-Host "[mono-smoke] RPCs completed"

    $streamHandle = $null
    $events = @()
    try {
        $streamHandle = Open-StreamEvents -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
        Invoke-McpRpc -Id "probe-stream" -Method "call_tool" -Params @{ name = "GetStatus"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null
        $streamReadLines = [Math]::Max($StreamLines, 5)
        $events = Read-StreamEvents -Reader $streamHandle.Reader -Lines $streamReadLines
    }
    finally {
        if ($streamHandle) {
            try { $streamHandle.Reader.Dispose() } catch { }
            try { $streamHandle.Response.Dispose() } catch { }
            try { $streamHandle.Client.Dispose() } catch { }
        }
    }

    Write-Host "[mono-smoke] Stream events captured: $($events.Count)"

    $status = Get-JsonContent -Result $statusTool
    $logs = Get-JsonContent -Result $logsTool
    $mousePick = Get-JsonContent -Result $mousePickTool
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

    Write-Host "[mono-smoke] Summary"
    Write-Host "Tools: $($tools.result.tools.Count) returned"
    Write-Host "Status: Ready=$($status.Ready) Scenes=$($status.ScenesLoaded)"
    Write-Host "MousePick: Mode=$($mousePick.Mode) Hit=$($mousePick.Hit)"
    Write-Host "Scenes: $($readScenesDoc.Total) total"
    Write-Host "Logs (tool): $($logs.Items.Count) items (requested $LogCount)"
    Write-Host "Logs (read): $($readLogsDoc.Items.Count) items"
    if ($EnableWriteSmoke) {
        $spawnOk = if ($writeResult) { $writeResult.spawn.ok } else { $false }
        $setMemberOk = if ($writeResult) { $writeResult.setMember.ok } else { $false }
        $reparentOk = if ($writeResult) { $writeResult.reparent.ok } else { $false }
        $destroyBlockOk = if ($writeResult) { $writeResult.destroyBlock.ok } else { $false }
        $destroyUiOk = if ($writeResult) { $writeResult.destroyUi.ok } else { $false }
        $blockCount = if ($writeResult -and $writeResult.blocks) { ($writeResult.blocks | Measure-Object).Count } else { 0 }
        $timeValue = if ($writeResult) { $writeResult.time.value } else { $null }
        $timeLocked = if ($writeResult) { $writeResult.time.locked } else { $null }
        $selectionSeen = if ($writeResult) { $writeResult.selectionEvent -ne $null } else { $false }
        Write-Host "Write smoke: blocks=$blockCount spawnOk=$spawnOk setMemberOk=$setMemberOk reparentOk=$reparentOk destroyBlockOk=$destroyBlockOk destroyUiOk=$destroyUiOk timeValue=$timeValue locked=$timeLocked selectionEvent=$selectionSeen"
    }
    Write-Host "Stream events captured: $($events.Count) (tool_result observed=$($toolResultEvent -ne $null))"
    Write-Host "[mono-smoke] Done"
}
catch {
    Write-Error $_
    exit 1
}
