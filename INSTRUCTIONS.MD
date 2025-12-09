# Iteration Instructions (2025-12-09 — Iteration 3 of 5)

High-level goal for this iteration: validate inspector + smoke flows against a running MCP build and close remaining camera/freecam + Space Shooter harness gaps.

## Tasks for this worker

1) Inspector + smoke verification
- Run `@modelcontextprotocol/inspector` against the MCP build (Space Shooter on Test-VM) and ensure the new per-argument `inputSchema` renders cleanly (initialize → list_tools → call_tool/read_resource → stream_events). Fix schema/docs/tests if inspector surfaces errors.
- Run `pwsh ./tools/Invoke-McpSmoke.ps1` (with `-BaseUrl` or discovery file) against the running game; ensure the summary output and exit codes are correct and adjust as needed.

2) Camera/freecam parity
- Validate `GetCameraInfo` when Freecam is enabled; document any limitations in `plans/mcp-interface-concept.md` + `README-mcp.md`, and update DTO/tests if shape changes.

3) Space Shooter harness updates
- Extend the Test-VM harness to cover `SearchObjects`, `GetCameraInfo`, `MousePick` (world + UI with normalized coords), and a minimal `stream_events` check; sync `docs/space-shooter-test-plan.md` with expected payloads and safe write scenarios (`SetActive`/`SelectObject`).

## Current state
- `list_tools` now emits per-argument `inputSchema` (basic JSON Schema + enum for `MousePick.mode`); docs updated.
- New smoke script: `tools/Invoke-McpSmoke.ps1` (initialize → list_tools → GetStatus/TailLogs → read status/scenes/logs; defaults to discovery file).
- MCP contract tests: `pwsh ./tools/Run-McpContractTests.ps1` (Release) currently passing; one status test skips if discovery missing.

## Constraints & reminders
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` in sync with code/docs/tests.
- Avoid game-specific assumptions beyond Space Shooter; writes stay guarded (do not loosen defaults).
- Prefer PowerShell one-liners for tooling; use streamable HTTP path only.
- Run `pwsh ./tools/Run-McpContractTests.ps1` before handoff.
