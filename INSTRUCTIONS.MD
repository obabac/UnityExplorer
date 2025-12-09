# Iteration Instructions (2025-12-09 — Iteration 4 of 5)

High-level goal for this iteration: validate the inspector + Space Shooter harness against the freecam-aware MCP build and close the remaining camera/mouse pick checks.

## Tasks for this worker

1) Inspector + smoke verification
- Run `@modelcontextprotocol/inspector` against a running MCP build (Space Shooter on the Test-VM); ensure `list_tools` inputSchema renders; exercise `initialize` → `notifications/initialized` → `list_tools` → `call_tool` / `read_resource` → `stream_events` without schema/validation errors. Fix code/docs/tests if inspector shows issues.
- Run `pwsh ./tools/Invoke-McpSmoke.ps1` (with `-BaseUrl` or discovery) and ensure the summary/output/exit code stay correct.

2) Freecam camera validation
- Toggle UE Freecam in-game and call `GetCameraInfo`; confirm `IsFreecam=true` and values follow the freecam camera (or reused game camera when "Use Game Camera" is on). Update `plans/mcp-interface-concept.md` / `README-mcp.md` / tests if the live payload differs.

3) Space Shooter harness updates
- Apply the new payloads from `plans/space-shooter-test-plan.md` to `C:\codex-workspace\ue-mcp-headless\call-mcp.ps1` + `req.json` on the Test-VM: `SearchObjects` (name/type), `GetCameraInfo`, `MousePick` world (normalized center), `MousePick` UI (normalized, `SpawnTestUi`), and a `stream_events` check.
- Capture expected outputs/notes in the plan doc; keep writes guarded and reset config after `SpawnTestUi` / `SetActive` / `SelectObject`.

## Current state
- `GetCameraInfo` now uses the UE Freecam state (falls back to main/first camera; `<none>` when missing); docs + contract test updated; harness payloads and safe write flows documented in `plans/space-shooter-test-plan.md`.
- `plans/mcp-interface-concept.md`, `README-mcp.md`, and contract tests reflect the current DTOs; `Run-McpContractTests` passes locally (Release).

## Constraints & reminders
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` in sync with code/docs/tests.
- Avoid game-specific assumptions beyond Space Shooter; writes stay guarded (`allowWrites=false` by default, `requireConfirm=true`).
- Use streamable HTTP path only; prefer PowerShell one-liners for tooling.
- Run `pwsh ./tools/Run-McpContractTests.ps1` before handoff.
