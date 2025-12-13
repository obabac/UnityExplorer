# Iteration Instructions — IL2CPP dropdown guard live (keep host healthy)

State
- UIManager dropdown refresh is wrapped in try/catch; expect warning `Failed to refresh UI dropdowns; continuing without failsafe: ...` on init.
- `Mods\UeMcpHeadless.dll` is renamed to `.disabled` on the Test-VM; keep it disabled.
- IL2CPP host `http://192.168.178.210:51477` is running UnityExplorer 4.12.8; inspector CLI + Invoke-McpSmoke + MCP contract tests (47 passed, 1 skipped) just passed.
- Mono host `http://192.168.178.210:51478` unchanged (smoke + inspector pass).

Do next
1) If you restart the host: stop SpaceShooter, start it via PowerShell (`Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`), and tail logs with `pwsh ./tools/Get-ML-Log.ps1 -Stream` until you see the MCP bind line on :51477 (dropdown warning is okay, no crash).
2) Keep `UeMcpHeadless.dll` disabled; if it comes back, rename to `.disabled` again before launch.
3) For any protocol/DTO changes, rerun smokes/tests:
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20`
   - `UE_MCP_DISCOVERY=./ue-mcp-il2cpp-discovery.json pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release`
4) Stretch: validate the dropdown guard on another IL2CPP title (no `UeMcpHeadless.dll`), watch MelonLoader logs for any new exceptions.

Notes
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and related docs in sync with code and test outcomes.
- Avoid game-specific assumptions; Space Shooter remains the baseline.
