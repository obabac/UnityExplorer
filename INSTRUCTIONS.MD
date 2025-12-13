# Iteration 5/10 — Selection streams contract + UI pick ordering (Mono parity)

Rule
- Validation first: run the required checks BEFORE and AFTER behavior changes in this iteration.

Hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

0) Baseline gates (run now, no code changes yet)
- `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
- `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`
- `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`
- Trigger bad `read_resource` on both hosts and confirm `[MCP] error ...` appears in `unity://logs/tail?count=50` (Mono GET `/read?uri=` now works).

1) Selection streams contract (core task)
- Add coverage (prefer contract test) that a selection change emits a `stream_events` `selection` notification with `ActiveId`/`Items` matching `unity://selection` on Mono and CoreCLR.
- Keep selection snapshot used for `unity://selection` and notifications in sync; document any deltas in `plans/mcp-interface-concept.md`.

2) MousePick UI ordering follow-up
- Validate that `MousePick(mode="ui")` on Mono returns the ordered multi-hit list and top-most `Id` using `SpawnTestUi`; add harness/test if needed.

3) Docs sync (single source of truth)
- Update `plans/mcp-interface-concept.md` for any shape/payload changes; refresh `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` only for status/steps (avoid duplicating DTO shapes).

4) Test-VM E2E (after code changes)
- Build Mono (`dotnet build src/UnityExplorer.csproj -c ML_Mono`), deploy the DLL to `SpaceShooter_Mono/Mods`, restart the Mono game, and verify MCP binds on `:51478`.
- Rerun ALL baseline gates from step 0.

Housekeeping
- Keep `Mods\\UeMcpHeadless.dll` disabled.
- `allowWrites=false` at rest on both hosts; use SpawnTestUi blocks as safe targets.
- Commits allowed; keep them small and scoped.
