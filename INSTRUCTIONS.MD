# Iteration Instructions (2025-12-11 — Mono Phase B follow-up)

High-level goal: finish Mono MCP host parity (read-only) and validation.

## Tasks
1) Mono stream/events + host polish
- Decide if Mono should expose `stream_events`; either implement basic streaming or explicitly surface NotReady in capabilities/docs and inspector flows.
- Clean up nullable warnings in Mono DTO/config/host; ensure discovery write has non-null defaults.

2) Mono validation harness
- Add a Mono smoke harness (script/tests) for initialize → list_tools → read_resource/call_tool (status/scenes/objects/components/search/logs/selection/camera).
- Document how to run the Mono harness and expected outputs.

3) Docs/tests
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`, `README-mcp.md` in sync with Mono limitations (read-only, writes off, stream_events TBD).
- Re-run `dotnet build src/UnityExplorer.csproj -c ML_Mono` and MCP contract/smoke tests when a host is available; record results.

## Reminders
- Avoid refactors outside MCP; keep CoreCLR behaviour unchanged.
- Prefer PowerShell one-liners when sharing commands.
- Keep plans/todo in sync as you work; commit with a clear Mono Phase B summary.
