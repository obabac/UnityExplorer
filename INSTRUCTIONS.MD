# Iteration Instructions (2025-12-11 — Mono Phase A: build unlock)

High-level goal: make ML_Mono compile again and land a Mono MCP skeleton/stub without changing CoreCLR behavior.

## Tasks
1) Fix ML_Mono build break
- Guard `ExplorerCore`/`OptionsPanel` MCP calls when `INTEROP` is off (current ML_Mono: net35, MONO+ML, IncludeMcpPackages=false).
- Add the minimal stub/flag so McpHost references compile on Mono; decide whether to define INTEROP for Mono or keep a disabled stub.
- Re-run the ML_Mono build command (document command + output path) and capture any remaining errors (include snippets in summary/docs).

2) Mono MCP skeleton
- Introduce a Mono-friendly placeholder transport/host (no full HTTP yet) that keeps DTO shapes/error envelope reachable, or explicitly disable MCP with clear discovery/config behavior.
- Keep CoreCLR behavior unchanged; avoid touching IL2CPP/Test-VM paths.

3) Docs/plan updates
- Update `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`, and `README-mcp.md` to match changes/decisions.
- Note build results and open Mono gaps; keep doc map as single source of truth.

## Reminders
- Avoid large refactors; keep existing boundaries.
- Use PowerShell one-liners when sharing commands.
- Keep plans/todo in sync while working; commit with a clear Mono Phase A summary.
