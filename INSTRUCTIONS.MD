# Iteration Instructions (2025-12-10 — Next)

High-level goal for next iteration: complete MCP inspector schema/UX validation on the Test-VM while keeping the Space Shooter host stable and the contract suite green.

## Tasks
1) Inspector validation
- Connect `@modelcontextprotocol/inspector` (http) to `http://192.168.178.210:51477` and run initialize → notifications/initialized → list_tools → call_tool (status/logs/search/mouse/camera/selection) → stream_events. Confirm forms render with enums/defaults and `additionalProperties=false`; note any schema or UX gaps.
- Run `call-mcp.ps1` scenarios (`initialize`, `list_tools`, `status`, `logs`, `camera`, `mouse-world`, `events`) and capture payloads/notes for docs (`plans/mcp-interface-concept.md`, `plans/space-shooter-test-plan.md`).

2) Keep Space Shooter running + tests green
- Ensure `SpaceShooter.exe` is running (start with `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`). Base URL `http://192.168.178.210:51477`.
- Run `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20` and `pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release -BaseUrl http://192.168.178.210:51477`. Grab `MelonLoader/Latest.log` if failures.

3) Docs/config follow-ups
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` in sync with any new payloads/shapes. Update `plans/mcp-interface-concept.md` if schemas change.
- Track the Il2Cpp dropdown cast crash; remove the Test-VM `UeMcpHeadless.dll` workaround only after a proper fix is merged and validated.

## Current state
- Space Shooter MCP host at `http://192.168.178.210:51477`; `Invoke-McpSmoke.ps1` and `Run-McpContractTests.ps1 -Configuration Release` both green (45 passed, 1 skipped placeholder `Status_Tool_And_Resource_Available`).
- `list_tools` exposes per-argument schemas with enums/defaults; inspector UI validation still pending.

## Reminders
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` aligned with code/DTOs/tests. Update `plans/mcp-interface-concept.md` when shapes change.
- Avoid game-specific assumptions; writes stay guarded (`allowWrites=false` by default). Stop the game before deploying via `Update-Mod-Remote.ps1`, then restart and tail via `Get-ML-Log.ps1`.
- Update this file at the end of the iteration and commit changes.
