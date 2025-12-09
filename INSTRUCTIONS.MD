# Iteration Instructions (2025-12-09 — Iteration 5 of 5)

High-level goal for this iteration: validate the MCP inspector and Space Shooter harness against freecam/mouse/camera flows using the updated call-mcp script; capture real payloads and keep docs/tests aligned.

## Tasks for this worker

1) Inspector + smoke validation
- Run `@modelcontextprotocol/inspector` against a running MCP build; verify `list_tools` inputSchema renders; exercise `initialize` → `notifications/initialized` → `list_tools` → `call_tool`/`read_resource` → `stream_events` and fix code/docs/tests if issues appear.
- Run `pwsh ./tools/Invoke-McpSmoke.ps1` (with `-BaseUrl` or discovery) and ensure the summary/output/exit code stay correct.

2) Freecam camera validation
- Toggle UE Freecam in-game and call `GetCameraInfo`; confirm `IsFreecam=true` and values follow the freecam (or reused game camera when "Use Game Camera" is on). Update `plans/mcp-interface-concept.md` / `README-mcp.md` / tests if the live payload differs.

3) Space Shooter harness runs
- On the Test-VM use `C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario ...` (`search|camera|mouse-world|mouse-ui|status|logs|selection|initialize|list_tools|events`, default discovery/127.0.0.1; `-Scenario custom` reads `req.json`). Run:
  - `SearchObjects` (name+type), `GetCameraInfo`, `MousePick` world (normalized center), `MousePick` UI (normalized; run `SpawnTestUi` under allowWrites+confirm), and a `stream_events` check.
  - Capture observed payloads/notes in `plans/space-shooter-test-plan.md`; keep writes guarded and reset config after `SpawnTestUi`/`SetActive`/`SelectObject`.

4) Tests
- Run `pwsh ./tools/Run-McpContractTests.ps1` before handoff.

## Current state
- `call-mcp.ps1` on the Test-VM now supports `-Scenario` presets (search/camera/mouse-world/mouse-ui/events/etc.), resolves BaseUrl from discovery/override, and reads `req.json` for custom bodies. Default `req.json` payload is `SearchObjects` (query `Player`, type `GameController`). Docs updated in `plans/space-shooter-test-plan.md` and `plans/unity-explorer-mcp-plan.md`.
- MCP contract tests pass locally (Release); inspector schema work and live freecam validation are still outstanding.

## Constraints & reminders
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` in sync with code/docs/tests.
- Avoid game-specific assumptions beyond Space Shooter; writes stay guarded (`allowWrites=false`, `requireConfirm=true`).
- Use streamable HTTP only; prefer PowerShell one-liners for tooling. Reset configs after enabling writes.
- Update this file at end of iteration and commit changes.
