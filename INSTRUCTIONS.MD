# Iteration 3/10 — Expand Mono guarded writes (destroy/reparent)

Rule
- Validation first: rerun the required checks (below) in the same iteration whenever MCP behaviour changes.

Goal
- Add the next safe Mono guarded writes and validate them end-to-end using the test UI so we avoid game-specific assumptions.

Scope (do these)
1) Mono MCP: add guarded write tools (matching CoreCLR names) for:
   - `DestroyObject(confirm=false)` (safe target: test UI objects from SpawnTestUi)
   - `Reparent(confirm=false)` (safe target: reparent one test UI block under the other)
   Both must be gated by `allowWrites=true` and `requireConfirm`/`confirm=true`; update tool list and instructions strings accordingly.

2) Mono smoke: extend `tools/Invoke-McpSmokeMono.ps1` so `-EnableWriteSmoke`:
   - enables writes (`SetConfig allowWrites=true requireConfirm=true`)
   - spawns the test UI, reparents/destroys a test UI block with `confirm=true` (use IDs from `SpawnTestUi` output or `MousePick`)
   - cleans up: destroy the test UI and reset config in `finally` (`allowWrites=false requireConfirm=true`)

3) Docs sync:
   - Update `plans/space-shooter-test-plan.md` with the new Mono write coverage (reparent/destroy via test UI).
   - Update `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md` only if the surface or validation status changed.
   - Add any new response shapes to `plans/mcp-interface-concept.md` (single source; avoid duplication).

4) Test-VM E2E:
   - Build Mono (`dotnet build src/UnityExplorer.csproj -c ML_Mono`), copy the updated DLL from `Release/.../Mods/`, restart the Mono game, and verify MCP still binds on `:51478`.
   - Required checks: inspector CLI both hosts; `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`; trigger a controlled error (bad `read_resource`) and confirm `[MCP] error ...` appears in `unity://logs/tail?count=50` on both hosts.

Housekeeping
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
- `allowWrites=false` at rest on both hosts.
- Commits are allowed; keep them small and scoped.
