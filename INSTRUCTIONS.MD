# Iteration Instructions (2025-12-11 — Mono Phase B: host bring-up)

High-level goal: move Mono (ML_Mono) from an INTEROP-disabled stub to a minimal MCP read-only host without regressing CoreCLR behaviour.

## Tasks
1) Design Mono transport plan
- Choose a net35-friendly JSON/HTTP stack (e.g., Newtonsoft.Json + HttpListener/TcpListener) that preserves existing DTO/error shapes.
- Decide discovery/config behaviour on Mono (bind/port defaults, enabled flag) and document it in `plans/mcp-interface-concept.md`.
- Note any constraints (ThreadPool usage, Task substitutes) before coding.

2) Implement minimal Mono host
- Add a Mono-safe MCP host that handles initialize/list_tools/read_resource (status/scenes/objects/logs/selection) with current DTOs/error envelope; keep writes disabled.
- Wire McpConfig/McpHost/OptionsPanel so Mono builds start/stop the new host cleanly and still log "disabled" when it’s off.
- Ensure non-INTEROP/CoreCLR code paths stay untouched; avoid breaking existing CoreCLR discovery.

3) Tests/docs
- Add or document a smoke path for Mono (manual or automated) and capture current limitations; update `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`, `README-mcp.md` accordingly.
- Record build/test commands used (e.g., `dotnet build src/UnityExplorer.csproj -c ML_Mono`, any harness steps) and outstanding issues.

## Reminders
- Avoid large refactors; keep component boundaries intact.
- Prefer PowerShell one-liners when sharing commands.
- Keep plans/todo in sync while working; commit with a clear Mono Phase B summary.
