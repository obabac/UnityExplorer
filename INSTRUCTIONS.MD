# Iteration 7/10 — Fix inspector UI proxy + Mono MousePick parity

Context
- Test-VM MCP control plane is already listening on 8083 (`node .../mcp-control/build/index.js --sse --port 8083`); `curl http://192.168.178.210:8083/mcp` returns an SSE header then waits.
- `Start-McpInspectorUi.ps1` spins up proxies (last seen 6274/6277) but Edge headless shows the UI disconnected (localhost refused or default forms). No UI tool calls were executed yet.
- IL2CPP host: http://192.168.178.210:51477 ; Mono host: http://192.168.178.210:51478 . Contract tests pass locally; configs were left untouched.

Goal
1) Get the browser MCP inspector UI connecting via the proxy on both IL2CPP and Mono.
2) Run the UI MousePick/read flow; fix Mono world-mode `Items` parity (`null` vs `[]`) if still divergent.

Tasks
1) Quick health checks
   - `curl -sS -m 2 http://192.168.178.210:8083/mcp || true`
   - `ssh GPUVM@192.168.178.210 "powershell -NoProfile -Command \"netstat -ano | findstr :8083\""` to confirm the node PID.
2) Clean up stale proxies and relaunch the UI proxy
   - Stop stray `node`/`msedge` if needed.
   - For IL2CPP: `pwsh C:\\codex-workspace\\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51477 -LogPath C:\\codex-workspace\\mcp-inspector-il2cpp-ui.log -Port 6277 -NoBrowser`
   - For Mono: same with `-BaseUrl http://192.168.178.210:51478 -Port 6278`.
   - If Start-McpInspectorUi.ps1 stays disconnected, run npx manually with `PORT=6277 DANGEROUSLY_OMIT_AUTH=true npx --yes @modelcontextprotocol/inspector -- --transport http --server-url http://192.168.178.210:51477 --port 6277 --no-open` (adjust port/base URL for Mono) and screenshot via headless Edge.
3) Browser UI validation (each host)
   - Confirm tools/resources load without schema errors.
   - MousePick UI flow:
     1) `SetConfig {allowWrites:true, requireConfirm:true}`
     2) `SpawnTestUi(confirm:true)`
     3) `MousePick {mode:"ui", normalized:true, x:0.25, y:0.25}` → expect `Items.length>=1` and `Id==Items[0].Id`
     4) `DestroyTestUi(confirm:true)`
     5) `SetConfig {allowWrites:false, requireConfirm:true}`
   - World MousePick: `MousePick {mode:"world", normalized:true, x:0.5, y:0.5}` → expect `Items` null/absent. If Mono still returns `[]`, fix parity in code, redeploy, and rerun.
4) CLI + smoke gates
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke`
   - `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`
5) Reset configs to safe defaults (`allowWrites=false`, `requireConfirm=true`).

Finish
- Update `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` if shapes change.
- Capture any fixes applied to Start-McpInspectorUi or Mono MousePick parity.
- Commit your changes.
