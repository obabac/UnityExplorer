# Next iteration instructions (Iteration 15)

Focus
- Stabilize the IL2CPP host so the MCP contract suite runs without dropping connections; keep the `/read` CORS fix in place.

What’s broken right now
- SpaceShooter_IL2CPP shuts down / port 51477 closes during the contract tests (connection reset/refused after the first requests), so most tests fail even though `/read` now returns CORS headers.

Tasks
- Relaunch SpaceShooter_IL2CPP before testing (`Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`); confirm it stays up with `Get-Process SpaceShooter` and that `curl http://192.168.178.210:51477/read?uri=unity://status` works with CORS headers.
- Tail `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\MelonLoader\Latest.log` during and after the contract run to catch the crash/offline cause; fix the stability issue so the host survives the suite.
- Re-run IL2CPP contract tests once stable:
  - `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release`
- If the host stays up, re-run quick CLI gates:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477/mcp`
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 10`

Docs sync
- Update `plans/mcp-interface-concept.md` only if shapes/errors change.
- Keep `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md` aligned with the stability status and test results.

Safety
- Keep `allowWrites=false` by default; reset any write toggles after use.
