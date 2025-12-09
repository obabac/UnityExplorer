# Iteration Instructions (2025-12-09 — Next)

High-level goal for next iteration: validate the MCP inspector + Space Shooter harness on a live MCP host (with Freecam/mouse/camera flows), capture real payloads, and keep docs/tests/DTOs aligned.

## Tasks

1) Bring up MCP host + smoke
- Start Space Shooter on the Test-VM with UnityExplorer MCP enabled; ensure discovery file exists (`%TEMP%/unity-explorer-mcp.json`).
- Run `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://<vm>:51477` (or discovery) and keep summary/output; fix failures or record why (e.g., connection/refused) and update docs.

2) Inspector validation
- Run `npx @modelcontextprotocol/inspector --transport http --server-url http://<vm>:51477`.
- Exercise `initialize` → `notifications/initialized` → `list_tools` (verify inputSchema renders) → `call_tool` (Status/Logs/Camera/MousePick) → `read_resource` → `stream_events`.
- If schemas/payloads differ, update DTOs/tests and sync `plans/mcp-interface-concept.md` + `README-mcp.md`.

3) Space Shooter harness + Freecam
- On the Test-VM run `C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario search|camera|mouse-world|mouse-ui|events|status|logs|selection` (or `custom` with `req.json`).
- Toggle UE Freecam and re-run `GetCameraInfo` (expect `IsFreecam=true`); capture payloads for freecam + game camera, MousePick world/ui (with `SpawnTestUi`), and note results in `plans/space-shooter-test-plan.md` and `plans/unity-explorer-mcp-plan.md`.
- Keep writes guarded; reset config after `SpawnTestUi`/`SetActive`/`SelectObject`.

4) Tests
- Run `pwsh ./tools/Run-McpContractTests.ps1` (Release) after changes; keep test output.

## Current state
- Contract tests pass locally (Release); status tool/resource test skips when discovery is missing.
- MCP runtime was not running locally; `Invoke-McpSmoke.ps1 -BaseUrl http://127.0.0.1:51477` failed with connection refused, so live inspector/harness validation is still outstanding.

## Reminders
- Keep `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` in sync with code/DTOs/tests.
- Avoid game-specific assumptions beyond Space Shooter; writes stay guarded (`allowWrites=false`, `requireConfirm=true`) when finishing.
- Update this file at end of iteration and commit changes.
