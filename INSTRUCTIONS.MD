# Iteration Instructions (2025-12-11 — Mono MousePick + Inspector follow-up)

High-level goal: push Mono MCP toward parity and validate inspector schemas without regressing CoreCLR.

## 1) Mono parity
- Continue Phase C: add remaining Mono parity items (guarded writes/time-scale/select tools) and a Mono-specific contract/CI entry; keep shapes aligned with CoreCLR (MousePick now present).
- Validate Mono MousePick world/ui flows in smoke/inspector runs; log any gaps versus IL2CPP in the plans/todo docs.
- Keep writes disabled by default; if adding writes, gate them with allowWrites+confirm and document shapes/errors alongside DTO changes.

## 2) Inspector validation
- Run `@modelcontextprotocol/inspector` against the CoreCLR host (`http://192.168.178.210:51477`) and any available Mono host; capture schema/UX deltas (inputSchema, error envelopes).
- Record findings and shape updates in `plans/mcp-interface-concept.md` and `plans/unity-explorer-mcp-todo.md`.

## 3) Docs/tests
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`, and `README-mcp.md` in sync with code/DTOs/tests.
- Prefer PowerShell helpers for smoke/tests; run contract tests (`pwsh ./tools/Run-McpContractTests.ps1`) after changes when possible.
- Avoid touching `.codex` logs/history; leave non-MCP refactors out.
