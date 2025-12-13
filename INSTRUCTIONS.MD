# Iteration 4/10 — Mono mouse-pick & selection parity

Rule
- Validation first: rerun required checks in the same iteration whenever MCP behaviour changes.

Goal
- Align Mono mouse-pick (UI multi-hit) and selection/stream behaviour with CoreCLR and keep the smoke harness up to date.

Scope (do these)
1) Mono MousePick: ensure UI mode returns the Items list + top-most Id like CoreCLR when using SpawnTestUi. Adjust handlers/DTOs if needed; keep allowWrites/confirm gates intact.
2) Selection/streams: verify Mono selection responses + notifications match CoreCLR. Add minimal coverage (smoke or test) if gaps appear.
3) Docs sync: update `plans/mcp-interface-concept.md` for any shape changes; refresh `plans/unity-explorer-mcp-plan.md` / `plans/unity-explorer-mcp-todo.md` / `plans/space-shooter-test-plan.md` if surfaces or validation steps change.
4) Test-VM E2E: build Mono (`dotnet build src/UnityExplorer.csproj -c ML_Mono`), copy the DLL to `Release/.../Mods/`, restart the Mono game, and rerun `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3` (use `-EnableWriteSmoke` if write paths changed). Confirm inspector CLI and a controlled bad `read_resource` still surface `[MCP] error` in `unity://logs/tail` on both hosts.

Housekeeping
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM; `allowWrites=false` at rest on both hosts. Use SpawnTestUi blocks as safe targets for any write smoke. Commits allowed; keep them small and scoped.
