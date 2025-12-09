# Iteration Instructions (2025-12-10 — Next)

High-level goal for next iteration: finish inspector UX validation (schema-rendered forms) while keeping the Space Shooter MCP host stable and the contract suite green.

## Tasks

1) Inspector/harness validation
- Connect `@modelcontextprotocol/inspector` (http) to `http://192.168.178.210:51477` and verify list_tools-rendered forms work for all tools (MousePick enum/defaults, required args, `additionalProperties=false`).
- Run `call-mcp.ps1` scenarios (`initialize`, `list_tools`, `status`, `logs`, `camera`, `mouse-world`, `events`) and capture payloads/notes for docs; highlight any schema/UX issues.

2) Keep Space Shooter running + contract tests green
- Ensure `SpaceShooter.exe` is running or start with `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`; base URL `http://192.168.178.210:51477`.
- Run `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20` to confirm readiness, then `pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release -BaseUrl http://192.168.178.210:51477` to keep the suite green (current result: 45 passed, 1 skipped placeholder `Status_Tool_And_Resource_Available`). Grab `MelonLoader/Latest.log` if failures.

3) Hardening and game-agnostic checks
- Review contract tests/docs for any Space Shooter-specific assumptions; adjust expectations so suite remains host-agnostic while validated on Space Shooter.
- Track the UnityExplorer dropdown Il2Cpp cast crash fix; plan to remove the Test-VM `UeMcpHeadless.dll` workaround once upstreamed.

## Current state
- Smoke run succeeds against `http://192.168.178.210:51477` (Ready=True, Scenes=1, logs returned).
- Full MCP contract suite (Release, BaseUrl `http://192.168.178.210:51477`) passes: 45 tests passed, 1 skipped placeholder; host remained stable during the run.
- `list_tools` emits per-argument JSON Schemas (enums/defaults/`additionalProperties=false`); inspector UI validation still pending.

## Reminders
- Keep `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` in sync with code/DTOs/tests.
- Avoid game-specific assumptions; writes stay guarded (`allowWrites` false by default). Stop the game before deploying via `Update-Mod-Remote.ps1`, then restart and tail via `Get-ML-Log.ps1`.
- Update this file at the end of the iteration and commit changes.
