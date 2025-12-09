# Iteration Instructions (2025-12-09 — Next)

High-level goal for next iteration: run the full MCP contract suite on the Space Shooter host (new typed `list_tools` schemas) without the game exiting, and validate inspector/harness flows end-to-end.

## Tasks

1) Keep Space Shooter running + contract tests
- Stop `SpaceShooter.exe` before deploying; start via `Start-Process "C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe"` (or win-dev start_process) and confirm MCP at `http://192.168.178.210:51477`.
- Set `UE_MCP_DISCOVERY` to a discovery file that points to the Test-VM base URL (override `baseUrl` to `http://192.168.178.210:51477/`).
- Run `pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release`; if the game exits mid-run, grab `MelonLoader/Latest.log` and rerun the failing slice to stabilize.

2) Inspector/harness validation with typed schemas
- Use `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477` and `curl` to confirm `list_tools` schemas (MousePick enum, required fields, defaults, `additionalProperties=false`).
- Exercise inspector/headless harness: `call-mcp.ps1 -Scenario initialize|list_tools|status|logs|camera|mouse-world|events` and capture payloads for docs.

3) Deployment hygiene
- Stop the game before `Update-Mod-Remote.ps1` to avoid SCP lock errors, then restart and verify via `Get-ML-Log.ps1` (look for MCP bind line).

## Current state
- New MCP build deployed to the Test-VM; smoke succeeds at `http://192.168.178.210:51477` with typed `list_tools` schemas.
- Targeted contract tests (`ListTools_Includes_InputSchema_For_All_Tools`, `CallTool_GetStatus_JsonRpc_Response_If_Server_Available`) pass; the full suite failed previously because SpaceShooter exited mid-run (connection reset/refused).
- SpaceShooter is currently running on the VM; writes remain disabled.

## Reminders
- Keep `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` in sync with code/DTOs/tests.
- Avoid game-specific assumptions; keep writes guarded and reset config after any write tests.
- Update this file at the end of the iteration and commit changes.
