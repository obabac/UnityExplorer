# Iteration 11/10 — Browser inspector notifications (CORS + SSE)

Context
- CORS headers + OPTIONS preflight now implemented on MCP HTTP (GET/POST/SSE/stream_events) and covered by contract tests; Access-Control-Allow-Origin:* etc. Contract suite now 53 passed, 1 skipped via `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`.
- SSE receive channel remains `GET /` with `Accept: text/event-stream`; JSON-RPC notifications without `id` return HTTP 202 (e.g., `notifications/initialized`). Inspector CLI passes on IL2CPP (51477) and Mono (51478).
- Default safety: allowWrites=false, requireConfirm=true; keep configs reset after any write smoke.

Rule
- Keep Space Shooter running in an interactive NVIDIA session (restart if logs show Microsoft Basic Render Driver).
- Leave allowWrites=false and requireConfirm=true when done; revert any config toggles.
- Avoid enabling `Mods\\UeMcpHeadless.dll` on the Test-VM.

Hosts
- IL2CPP: http://192.168.178.210:51477
- Mono: http://192.168.178.210:51478

Baselines
- Contract suite: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release` (expect 53 passed, 1 skipped).
- Inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `-BaseUrl http://192.168.178.210:51478`.
- Mono smoke (writes enabled for test UI): `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`.

Tasks
0) Build and deploy the latest MCP (with CORS) to IL2CPP and Mono (`cd UnityExplorer; pwsh ./build-ml-coreclr.ps1`; use Update-Mod-Remote.ps1), ensure `Mods\\UeMcpHeadless.dll` stays disabled, and restart Space Shooter via an interactive session if needed.
1) On the Test-VM, open the inspector browser UI for IL2CPP then Mono; call `GetStatus` and confirm the notifications pane receives at least one server notification (`tool_result` via SSE). Note any CORS or SSE errors.
2) If notifications are missing, debug `GET /` `Accept:text/event-stream` + CORS handling in `McpSimpleHttp` (CoreCLR/Mono) and update code/tests/concept doc accordingly.
3) Re-run baselines after fixes; reset allowWrites/requireConfirm; commit.

Housekeeping
- Prefer `pwsh ./tools/Get-ML-Log.ps1 -Stream` to watch startup.
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM; stop SpaceShooter before copying mods if needed.
