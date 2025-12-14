# Iteration 12/10 — Inspector browser validation (GET /read CORS)

Context
- CORS now covered on POST/OPTIONS/SSE and `GET /read`; contract suite reports 54 passed, 1 skipped via `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`.
- SSE receive channel remains `GET /` with `Accept: text/event-stream`; JSON-RPC notifications without `id` return HTTP 202 (e.g., `notifications/initialized`). Inspector CLI/UI should see `tool_result` notifications.
- Defaults: `allowWrites=false`, `requireConfirm=true`; reset configs after any write smoke.

Rule
- Keep Space Shooter running in an interactive NVIDIA session (restart if logs show Microsoft Basic Render Driver).
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
- Leave `allowWrites=false` and `requireConfirm=true` when done; revert any config toggles.

Hosts
- IL2CPP: http://192.168.178.210:51477
- Mono: http://192.168.178.210:51478

Baselines
- Contract suite: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release` (expect 54 passed, 1 skipped).
- Inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `-BaseUrl http://192.168.178.210:51478`.
- Mono smoke (writes enabled for test UI): `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`.

Tasks
0) If MCP changes, rebuild/deploy to IL2CPP and Mono (`cd UnityExplorer; pwsh ./build-ml-coreclr.ps1`; keep `Mods\\UeMcpHeadless.dll` disabled), and restart Space Shooter via an interactive session if needed.
1) In inspector browser UI (IL2CPP then Mono) call `GetStatus`/`Scenes` via the UI (`GET /read`) and confirm the notifications pane receives at least one SSE `tool_result`; note any CORS or SSE errors.
2) If browser flows fail, debug `McpSimpleHttp` CORS on `GET /read`/SSE and update code/tests/`plans/mcp-interface-concept.md` accordingly.
3) Re-run baselines after fixes; reset `allowWrites`/`requireConfirm`; commit.

Housekeeping
- Prefer `pwsh ./tools/Get-ML-Log.ps1 -Stream` to watch startup.
- Stop SpaceShooter before copying mods (`Update-Mod-Remote.ps1`) if needed; restart after deployment.
