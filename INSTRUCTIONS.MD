# Iteration Instructions (2025-12-09)

What changed this iteration
- Added `tools/Run-McpContractTests.ps1` (Release by default) and documented it in `README-mcp.md` + the plan.
- Updated `README-mcp.md` to group tools/resources (read vs guarded), added log tail and MousePick UI examples, and pointed readers to `plans/mcp-interface-concept.md` as the schema source of truth.
- Synced `.plans/unity-explorer-mcp-todo.md` (rate-limit/error items marked done, README/CI items done) and noted the new test helper in `.plans/unity-explorer-mcp-plan.md`.

Tests run
- `pwsh ./tools/Run-McpContractTests.ps1` (Release): Passed (44), Skipped (1: Status_Tool_And_Resource_Available when server not running), Failed (0).

Next steps / open items
- Remaining TODOs: hook lifecycle tests, inspector schema/rendering verification, smoke CLI script for initialize/list_tools/GetStatus/TailLogs, camera/freecam validation, Space Shooter validation tasks, and other unchecked items in `plans/unity-explorer-mcp-todo.md`.
- Keep docs in sync (`plans/mcp-interface-concept.md`, plan, todo, README) if DTOs or errors change.
- Prefer PowerShell one-liners for CI/dev commands and rerun the MCP contract tests after changes.
