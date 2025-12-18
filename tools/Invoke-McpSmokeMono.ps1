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
        Write-Host "[mono-smoke] guarded write smoke (SetConfig -> ConsoleEval -> SpawnTestUi -> Add/RemoveComponent -> SetMember(Image.color) -> HookAdd/HookRemove -> Reparent -> DestroyObject -> DestroyTestUi + TimeScale)"
        $resetParams = @{ name = "SetConfig"; arguments = @{ allowWrites = $false; requireConfirm = $true; enableConsoleEval = $false; componentAllowlist = @(); reflectionAllowlistMembers = @(); hookAllowlistSignatures = @() } }
        $prevConfigJson = $null
        $spawned = $false
        $destroyUiJson = $null
        $destroyBlockJson = $null
        $reparentJson = $null
        $setMemberJson = $null
        $consoleEvalJson = $null
        $addComponentJson = $null
        $removeComponentJson = $null
        $hookAddJson = $null
        $hookRemoveJson = $null
        $hookSignature = $null
        $selectionEvent = $null
        $blockIds = @()
        $consoleScriptName = $null
        $consoleScriptRunJson = $null
        $consoleScriptWriteJson = $null
        $consoleScriptDeleteJson = $null
        $startupWriteJson = $null
        $startupRunJson = $null
        $startupOriginal = $null
        $callMethodJson = $null
        try {
            $prevConfigJson = Get-JsonContent -Result (Invoke-McpRpc -Id "get-config-before-smoke" -Method "call_tool" -Params @{ name = "GetConfig"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds)
            if ($prevConfigJson -and $prevConfigJson.ok) {
                if ($prevConfigJson.enableConsoleEval -eq $true) { $resetParams.arguments.enableConsoleEval = $true }
                if ($prevConfigJson.componentAllowlist) { $resetParams.arguments.componentAllowlist = @($prevConfigJson.componentAllowlist) | Where-Object { $_ } }
                if ($prevConfigJson.reflectionAllowlistMembers) { $resetParams.arguments.reflectionAllowlistMembers = @($prevConfigJson.reflectionAllowlistMembers) | Where-Object { $_ } }
                if ($prevConfigJson.hookAllowlistSignatures) { $resetParams.arguments.hookAllowlistSignatures = @($prevConfigJson.hookAllowlistSignatures) | Where-Object { $_ } }
            }

            Invoke-McpRpc -Id "set-config-enable" -Method "call_tool" -Params @{ name = "SetConfig"; arguments = @{ allowWrites = $true; requireConfirm = $true; enableConsoleEval = $true; componentAllowlist = @("UnityEngine.CanvasGroup"); reflectionAllowlistMembers = @("UnityEngine.UI.Image.color", "UnityEngine.Transform.GetInstanceID"); hookAllowlistSignatures = @("UnityEngine.GameObject") } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null

            $consoleScriptName = "mono-smoke-$([guid]::NewGuid().ToString('N')).cs"
            $startupOriginal = Get-JsonContent -Result (Invoke-McpRpc -Id "get-startup-before" -Method "call_tool" -Params @{ name = "GetStartupScript"; arguments = @{} } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds)
            $consoleScriptWrite = Invoke-McpRpc -Id "write-console-script" -Method "call_tool" -Params @{ name = "WriteConsoleScript"; arguments = @{ path = $consoleScriptName; content = "return 5*5;"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $consoleScriptWriteJson = Get-JsonContent -Result $consoleScriptWrite
            $consoleScriptRun = Invoke-McpRpc -Id "run-console-script" -Method "call_tool" -Params @{ name = "RunConsoleScript"; arguments = @{ path = $consoleScriptName; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $consoleScriptRunJson = Get-JsonContent -Result $consoleScriptRun
            $consoleScriptDelete = Invoke-McpRpc -Id "delete-console-script" -Method "call_tool" -Params @{ name = "DeleteConsoleScript"; arguments = @{ path = $consoleScriptName; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $consoleScriptDeleteJson = Get-JsonContent -Result $consoleScriptDelete

            $startupWrite = Invoke-McpRpc -Id "write-startup" -Method "call_tool" -Params @{ name = "WriteStartupScript"; arguments = @{ content = "return \"mono-startup\";"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $startupWriteJson = Get-JsonContent -Result $startupWrite
            $startupDisable = Invoke-McpRpc -Id "disable-startup" -Method "call_tool" -Params @{ name = "SetStartupScriptEnabled"; arguments = @{ enabled = $false; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $startupDisableJson = Get-JsonContent -Result $startupDisable
            $startupRun = Invoke-McpRpc -Id "run-startup" -Method "call_tool" -Params @{ name = "RunStartupScript"; arguments = @{ confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $startupRunJson = Get-JsonContent -Result $startupRun
            $startupEnable = Invoke-McpRpc -Id "enable-startup" -Method "call_tool" -Params @{ name = "SetStartupScriptEnabled"; arguments = @{ enabled = $true; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $startupEnableJson = Get-JsonContent -Result $startupEnable

            $eval = Invoke-McpRpc -Id "console-eval" -Method "call_tool" -Params @{ name = "ConsoleEval"; arguments = @{ code = "1+1"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $consoleEvalJson = Get-JsonContent -Result $eval
            if (-not $consoleEvalJson -or -not $consoleEvalJson.ok) { throw "ConsoleEval returned ok=false" }

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

            $addComp = Invoke-McpRpc -Id "add-component" -Method "call_tool" -Params @{ name = "AddComponent"; arguments = @{ objectId = $parentId; type = "UnityEngine.CanvasGroup"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $addComponentJson = Get-JsonContent -Result $addComp
            if (-not $addComponentJson -or -not $addComponentJson.ok) { throw "AddComponent returned ok=false" }

            $removeComp = Invoke-McpRpc -Id "remove-component" -Method "call_tool" -Params @{ name = "RemoveComponent"; arguments = @{ objectId = $parentId; typeOrIndex = "UnityEngine.CanvasGroup"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $removeComponentJson = Get-JsonContent -Result $removeComp
            if (-not $removeComponentJson -or -not $removeComponentJson.ok) { throw "RemoveComponent returned ok=false" }

            $colorJson = (@{ r = 0.25; g = 0.5; b = 0.75; a = 0.9 } | ConvertTo-Json -Compress)
            $setMember = Invoke-McpRpc -Id "set-member" -Method "call_tool" -Params @{ name = "SetMember"; arguments = @{ objectId = $parentId; componentType = "UnityEngine.UI.Image"; member = "color"; jsonValue = $colorJson; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $setMemberJson = Get-JsonContent -Result $setMember
            if (-not $setMemberJson -or -not $setMemberJson.ok) { throw "SetMember returned ok=false" }

            $callMethod = Invoke-McpRpc -Id "call-method" -Method "call_tool" -Params @{ name = "CallMethod"; arguments = @{ objectId = $parentId; componentType = "UnityEngine.Transform"; method = "GetInstanceID"; argsJson = "[]"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $callMethodJson = Get-JsonContent -Result $callMethod

            $hooksBeforeRes = Invoke-McpRpc -Id "read-hooks-before" -Method "read_resource" -Params @{ uri = "unity://hooks?limit=500" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $hooksBeforeDoc = ($hooksBeforeRes.result.contents[0].text | ConvertFrom-Json)
            $beforeSigs = @()
            if ($hooksBeforeDoc -and $hooksBeforeDoc.Items) { $beforeSigs = @($hooksBeforeDoc.Items | ForEach-Object { $_.Signature }) | Where-Object { $_ } }

            $hookAdd = Invoke-McpRpc -Id "hook-add" -Method "call_tool" -Params @{ name = "HookAdd"; arguments = @{ type = "UnityEngine.GameObject"; method = "SetActive"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $hookAddJson = Get-JsonContent -Result $hookAdd
            if (-not $hookAddJson -or -not $hookAddJson.ok) { throw "HookAdd returned ok=false" }

            $hooksAfterRes = Invoke-McpRpc -Id "read-hooks-after" -Method "read_resource" -Params @{ uri = "unity://hooks?limit=500" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $hooksAfterDoc = ($hooksAfterRes.result.contents[0].text | ConvertFrom-Json)
            $afterSigs = @()
            if ($hooksAfterDoc -and $hooksAfterDoc.Items) { $afterSigs = @($hooksAfterDoc.Items | ForEach-Object { $_.Signature }) | Where-Object { $_ } }

            $addedSigs = @($afterSigs | Where-Object { $beforeSigs -notcontains $_ })
            if ($addedSigs.Count -lt 1) { throw "HookAdd produced no new hook signature" }
            $hookSignature = $addedSigs[0]

            $hookRemove = Invoke-McpRpc -Id "hook-remove" -Method "call_tool" -Params @{ name = "HookRemove"; arguments = @{ signature = $hookSignature; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $hookRemoveJson = Get-JsonContent -Result $hookRemove
            if (-not $hookRemoveJson -or -not $hookRemoveJson.ok) { throw "HookRemove returned ok=false" }

            $hooksFinalRes = Invoke-McpRpc -Id "read-hooks-final" -Method "read_resource" -Params @{ uri = "unity://hooks?limit=500" } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds
            $hooksFinalDoc = ($hooksFinalRes.result.contents[0].text | ConvertFrom-Json)
            if ($hooksFinalDoc -and $hooksFinalDoc.Items) {
                foreach ($h in $hooksFinalDoc.Items) {
                    if ($h.Signature -eq $hookSignature) { throw "HookRemove cleanup failed; signature still present" }
                }
            }

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
                consoleEval    = $consoleEvalJson
                addComponent   = $addComponentJson
                removeComponent = $removeComponentJson
                setMember      = $setMemberJson
                callMethod     = $callMethodJson
                hookSignature  = $hookSignature
                hookAdd        = $hookAddJson
                hookRemove     = $hookRemoveJson
                reparent       = $reparentJson
                destroyBlock   = $destroyBlockJson
                destroyUi      = $destroyUiJson
                time           = $getTimeJson
                selectionEvent = $selectionEvent
                consoleScriptWrite = $consoleScriptWriteJson
                consoleScriptRun = $consoleScriptRunJson
                startupWrite   = $startupWriteJson
                startupRun     = $startupRunJson
            }
        }
        finally {
            if ($spawned -and -not $destroyUiJson) {
                try {
                    $destroyUiJson = Get-JsonContent -Result (Invoke-McpRpc -Id "destroy-testui-final" -Method "call_tool" -Params @{ name = "DestroyTestUi"; arguments = @{ confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds)
                } catch { Write-Warning "[mono-smoke] cleanup destroy failed: $_" }
            }
            if ($consoleScriptName) {
                try { Invoke-McpRpc -Id "cleanup-console" -Method "call_tool" -Params @{ name = "DeleteConsoleScript"; arguments = @{ path = $consoleScriptName; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null } catch { }
            }
            if ($startupOriginal) {
                try {
                    if ($startupOriginal.content) {
                        Invoke-McpRpc -Id "restore-startup" -Method "call_tool" -Params @{ name = "WriteStartupScript"; arguments = @{ content = $startupOriginal.content; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null
                        if ($startupOriginal.enabled -eq $false) {
                            Invoke-McpRpc -Id "restore-startup-disable" -Method "call_tool" -Params @{ name = "SetStartupScriptEnabled"; arguments = @{ enabled = $false; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null
                        }
                    }
                    else {
                        Invoke-McpRpc -Id "cleanup-startup-active" -Method "call_tool" -Params @{ name = "DeleteConsoleScript"; arguments = @{ path = "startup.cs"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null
                        Invoke-McpRpc -Id "cleanup-startup-disabled" -Method "call_tool" -Params @{ name = "DeleteConsoleScript"; arguments = @{ path = "startup.disabled.cs"; confirm = $true } } -MessageUrl $messageUrl -TimeoutSeconds $TimeoutSeconds | Out-Null
                    }
                } catch { Write-Warning "[mono-smoke] failed to restore startup script: $_" }
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
    $hasItemsProp = $mousePick.PSObject.Properties.Name -contains "Items"
    if ($mousePick.Mode -eq "world" -and $hasItemsProp -and $mousePick.Items -ne $null) {
        throw "MousePick world Items should be null or omitted"
    }

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
        $consoleEvalOk = if ($writeResult) { $writeResult.consoleEval.ok } else { $false }
        $addCompOk = if ($writeResult) { $writeResult.addComponent.ok } else { $false }
        $removeCompOk = if ($writeResult) { $writeResult.removeComponent.ok } else { $false }
        $hookAddOk = if ($writeResult) { $writeResult.hookAdd.ok } else { $false }
        $hookRemoveOk = if ($writeResult) { $writeResult.hookRemove.ok } else { $false }
        $setMemberOk = if ($writeResult) { $writeResult.setMember.ok } else { $false }
        $callMethodOk = if ($writeResult) { $writeResult.callMethod.ok } else { $false }
        $reparentOk = if ($writeResult) { $writeResult.reparent.ok } else { $false }
        $destroyBlockOk = if ($writeResult) { $writeResult.destroyBlock.ok } else { $false }
        $destroyUiOk = if ($writeResult) { $writeResult.destroyUi.ok } else { $false }
        $blockCount = if ($writeResult -and $writeResult.blocks) { ($writeResult.blocks | Measure-Object).Count } else { 0 }
        $hookSig = if ($writeResult) { $writeResult.hookSignature } else { $null }
        $timeValue = if ($writeResult) { $writeResult.time.value } else { $null }
        $timeLocked = if ($writeResult) { $writeResult.time.locked } else { $null }
        $selectionSeen = if ($writeResult) { $writeResult.selectionEvent -ne $null } else { $false }
        $consoleScriptResult = if ($writeResult) { $writeResult.consoleScriptRun } else { $null }
        $startupResult = if ($writeResult) { $writeResult.startupRun } else { $null }
        Write-Host "Write smoke: blocks=$blockCount spawnOk=$spawnOk consoleEvalOk=$consoleEvalOk addCompOk=$addCompOk removeCompOk=$removeCompOk hookAddOk=$hookAddOk hookRemoveOk=$hookRemoveOk hookSig=$hookSig setMemberOk=$setMemberOk callMethodOk=$callMethodOk reparentOk=$reparentOk destroyBlockOk=$destroyBlockOk destroyUiOk=$destroyUiOk timeValue=$timeValue locked=$timeLocked selectionEvent=$selectionSeen consoleScriptOk=$($consoleScriptResult.ok) startupResult=$($startupResult.result)"
    }
    Write-Host "Stream events captured: $($events.Count) (tool_result observed=$($toolResultEvent -ne $null))"
    Write-Host "[mono-smoke] Done"
}
catch {
    Write-Error $_
    exit 1
}
