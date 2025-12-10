# Iteration Instructions (2025-12-11 — Mono Parity + Inspector Validation)

High-level goal: tighten Mono MCP parity and validate inspector UX now that Mono `stream_events` and the smoke harness are available.

## 1) Mono parity + tests
- Extend Mono coverage toward CoreCLR (mouse pick, camera/freecam, selection streaming, guarded writes when safe) and add contract/CI hooks; reuse `tools/Invoke-McpSmokeMono.ps1` for quick checks.
- Add Mono `stream_events` coverage (log/selection/scene/tool_result) in tests; ensure disconnect cleanup and error envelope stay aligned with CoreCLR.

## 2) Inspector validation
- Run `@modelcontextprotocol/inspector` against CoreCLR and Mono hosts; fix schema/UX issues and capture any deltas in `plans/mcp-interface-concept.md` and the TODO.

## 3) Docs & plans
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`, and `README-mcp.md` in sync with code/tests.
- Record smoke/contract test results (Test-VM BaseUrl `http://192.168.178.210:51477`) and update Definition of Done checkboxes.

Working style: prefer PowerShell helpers; keep commits focused; avoid touching `.codex` logs/history; continue using streamable HTTP transport.
