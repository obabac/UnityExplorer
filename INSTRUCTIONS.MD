# Iteration Instructions (IL2CPP SelectObject hang)

Goals:
- Fix the IL2CPP MCP host so `UnityExplorer.Mcp.ContractTests` pass again; current blocker is `SelectObject` hanging the server and killing `SpaceShooter.exe` during `WriteToolsContractTests`, plus `SetTimeScale` still reporting `value=0` after setting to 1.
- Re-run smoke + contract tests on SpaceShooter IL2CPP once fixed and record results in the plan docs.

Context:
- BaseUrl: `http://192.168.178.210:51477` (SpaceShooter_IL2CPP on the Test-VM).
- Discovery file used for tests: `/home/onur/p-unity-explorer-mcp/ue-mcp-il2cpp-discovery.json`.
- Current build deployed: UnityExplorer 4.12.8 with JsonElement clone fixes, TimeScale reporting uses `Time.timeScale`, and `SelectObject` returns `NotReady` when inspector UI is missing (still hangs).

Repro (current state):
1) Start host: stop any `SpaceShooter.exe`, then run `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`; wait ~10s; verify with `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 5`.
2) Hang: even with writes disabled, `curl -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":"sel","method":"call_tool","params":{"name":"SelectObject","arguments":{"objectId":"obj:2132"}}}' http://192.168.178.210:51477/message` causes the MCP server to stop responding (GetStatus fails afterward).
3) Contract suite: `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-il2cpp-discovery.json pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release` → failures/connection resets in `WriteToolsContractTests` (SelectObject/SetTimeScale path) and host exit.
4) Single test: `dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release --filter FullyQualifiedName~WriteToolsContractTests.SelectObject_Updates_Selection_RoundTrip` reproduces the hang/connection reset.

Next steps (suggested):
- Make `SelectObject` non-blocking when UI is unavailable/headless (avoid Unity UI touch entirely; consider short-circuit before `MainThread.RunAsync` or a safe fallback selection storage).
- Investigate why the request never returns despite the `NotReady` guard (check MelonLoader `Latest.log` or attach debugger/logging around `SelectObject`).
- Verify `SetTimeScale` now returns the applied value and does not destabilize the host; adjust tests/docs if behavior changes.
- After fixes: rebuild/deploy (`pwsh ./build-ml-coreclr.ps1`), restart SpaceShooter, rerun smoke + full contract tests, and refresh `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` with the results.
