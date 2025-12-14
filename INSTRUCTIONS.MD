# Iteration 10/10 — Validate Inspector UI notifications (SSE)

Context
- SSE receive channel lives on `GET /` when `Accept: text/event-stream`; JSON-RPC notifications without `id` return HTTP 202 so clients open SSE. Contract suite updated and passing (52 passed, 1 skipped) using `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`. Inspector CLI passes on IL2CPP (51477) and Mono (51478).

Rule
- Keep Space Shooter running in an interactive GPU session (avoid Microsoft Basic Render Driver); restart via interactive session if logs show fallback.
- Leave `allowWrites=false` and `requireConfirm=true` when done; reset any config you touch.

Hosts
- IL2CPP: http://192.168.178.210:51477
- Mono: http://192.168.178.210:51478

Baselines
- Inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `-BaseUrl http://192.168.178.210:51478`.
- Contract suite: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`.
- Mono smoke (writes enabled): `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`.

Tasks
0) Ensure both games run on NVIDIA; tail Latest.log/Player.log for Basic Render Driver; restart via interactive session if needed.
1) Use inspector browser UI on the Test-VM for IL2CPP: connect, call `GetStatus`, then verify the notifications pane receives at least one server notification (e.g., `tool_result` via SSE). Repeat for Mono (second inspector instance).
2) If notifications are missing, debug the SSE path (`GET /` with `Accept: text/event-stream`) and JSON-RPC 202 handling; adjust `McpSimpleHttp` + tests + concept doc as needed.
3) Re-run baselines after any fix; keep `allowWrites` reset; commit.

Housekeeping
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
- Prefer `pwsh ./tools/Get-ML-Log.ps1 -Stream` to watch startup.
