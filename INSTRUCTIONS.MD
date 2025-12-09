# Iteration Instructions (2025-12-09 — Iteration 2 of 5)

High-level goal for this iteration: polish inspector UX and smoke CLI coverage so tools/resources render cleanly and a simple script can exercise the happy path against a running game.

## Tasks for this worker

1) Inspector schema/UX pass
- Run `@modelcontextprotocol/inspector` against the MCP build and verify tools/resources show without schema errors (initialize → list_tools/call_tool → read_resource → stream_events).
- Fix any schema/description issues (update DTOs/tests/docs as needed). If you cannot run the inspector, reason through the schemas and align `plans/mcp-interface-concept.md` + DTOs/tests.

2) Smoke CLI script
- Add a small scripted flow (PowerShell preferred) that hits initialize → list_tools → GetStatus → TailLogs → read unity://status/scenes/logs, mirroring inspector defaults.
- Place it with existing tooling (see TODO item 0 + 9), document usage in `README-mcp.md`, and keep plans/todo in sync.

3) Sync docs/plans/tests
- Keep `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` aligned with any schema/UX tweaks.
- Update `INSTRUCTIONS.MD` for the next iteration when done, and run `pwsh ./tools/Run-McpContractTests.ps1` (Release) before handing off.

## Current state
- MCP contract tests pass via `pwsh ./tools/Run-McpContractTests.ps1` (Release) with `UE_MCP_HOOK_TEST_ENABLED` unset (hook lifecycle test skips unless set to `1` with `hookAllowlistSignatures` including a safe type such as `UnityEngine.GameObject`).
- Hooks resource shape documented as `{ Signature, Enabled }`; hook lifecycle contract test added under `tests/dotnet/UnityExplorer.Mcp.ContractTests/HooksContractTests.cs`.
- Plans/docs/todo updated to reflect hook shape and test flag.

## Constraints & reminders
- Keep `.plans/unity-explorer-mcp-plan.md` and `.plans/unity-explorer-mcp-todo.md` in sync with code/docs.
- Avoid game-specific assumptions; keep examples/test hosts generic (Space Shooter OK but not required).
- Use PowerShell one-liners when adding docs/scripts. Writes remain guarded; do not loosen defaults.
