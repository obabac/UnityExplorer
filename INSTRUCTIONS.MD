# Iteration Instructions (2025-12-10 — Next)

High-level goal for next iteration: finish inspector schema/UX validation (forms render from list_tools) while keeping the Space Shooter MCP host stable and the contract suite green.

## Tasks
1) Inspector/harness validation
- Connect `@modelcontextprotocol/inspector` (http) to `http://192.168.178.210:51477` and verify list_tools-rendered forms work for all tools (MousePick enum/defaults, required args, `additionalProperties=false`).
- Run `call-mcp.ps1` scenarios (`initialize`, `list_tools`, `status`, `logs`, `camera`, `mouse-world`, `events`) and capture payloads/notes for docs; highlight any schema/UX issues.

2) Keep Space Shooter running + contract tests green
- Ensure `SpaceShooter.exe` is running or start with `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`; base URL `http://192.168.178.210:51477`.
- Run `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20` to confirm readiness, then `pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release -BaseUrl http://192.168.178.210:51477` (tests now derive scenes/objects from `unity://scenes`, no scene-0 assumption). Grab `MelonLoader/Latest.log` if failures.

3) Hardening and follow-ups
- Watch for UI dropdown cast fix (Il2Cpp) so we can drop the Test-VM `UeMcpHeadless.dll` workaround once upstreamed.
- Keep docs in sync: plans, README-mcp, `plans/mcp-interface-concept.md` when shapes change; avoid game-specific assumptions in new tests/docs.

## Current state
- Dotnet contract tests (Release) compile/run locally: 45 passed, 1 skipped placeholder (`Status_Tool_And_Resource_Available`). Tests now use discovered scene/object IDs instead of assuming scene 0; README example updated to reference `scene/{sceneId}`.
- Inspector UI validation still pending; list_tools already emits per-argument schemas (`additionalProperties=false`, enums/defaults present).
- Host endpoint last known: `http://192.168.178.210:51477`; smoke run previously succeeded (Ready=True, scenes=1, logs returned).

## Reminders
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` aligned with code/DTOs/tests.
- Avoid game-specific assumptions; writes stay guarded (`allowWrites` false by default). Stop the game before deploying via `Update-Mod-Remote.ps1`, then restart and tail via `Get-ML-Log.ps1`.
- Update this file at the end of the iteration and commit changes.
