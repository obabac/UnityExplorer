# Next iteration instructions (Iteration 20)

Focus (do in this order)
1) Audit Mono console/scripts + hooks parity vs `plans/mcp-interface-concept.md`.
2) Expand Mono write smoke to cover ConsoleEval + Add/RemoveComponent + HookAdd/HookRemove (guarded).
3) Add a streams parity contract test for a non-tool notification on both hosts (prefer a deterministic `scenes` snapshot on stream open).

Rules / gates
- Inspector validation is CLI-only (no UI).
- Test-VM validation must happen in the same iteration as any behavior change.
- IL2CPP regression is required when shared query/DTO code changes (even if the change was Mono-motivated): run inspector CLI + smoke + contract tests.
- Keep writes off by default; any write-smoke must reset config at the end.

Targets
- IL2CPP host: `http://192.168.178.210:51477/mcp`
- Mono host: `http://192.168.178.210:51478/mcp`
- Discovery files in repo: `ue-mcp-il2cpp-discovery.json`, `ue-mcp-mono-discovery.json`

Tasks
1) Mono audit (no behavior change unless required)
- Compare Mono `tools/list` + `resources/list` with `plans/mcp-interface-concept.md`.
- Specifically validate Mono `unity://console/scripts` and `unity://hooks` are readable and match the concept doc shapes.
- If the concept doc is wrong/outdated, update it (it is the source of truth). If code is wrong, fix code to match.

2) Expand Mono write smoke (`-EnableWriteSmoke`)
- Update `tools/Run-McpMonoSmoke.ps1` (and underlying helper if needed) to add these steps when `-EnableWriteSmoke` is set:
  - `SetConfig`: `allowWrites=true`, `requireConfirm=true`, `enableConsoleEval=true`, set allowlists needed for the smoke (reflection + component + hooks).
  - `ConsoleEval` with a harmless snippet (e.g., `"1+1"`) and `confirm=true`; assert `ok=true`.
  - `AddComponent` + `RemoveComponent` on a safe SpawnTestUi block using an allowlisted, harmless component type (e.g., `UnityEngine.CanvasGroup`), with `confirm=true`; assert `ok=true`.
  - `HookAdd` then `HookRemove` for a safe allowlisted target; ensure hooks are cleaned up (no leftover hooks after the smoke).
  - Always reset config at the end (`allowWrites=false`, `requireConfirm=true`, and disable `enableConsoleEval` if you enabled it).

3) Streams parity contract test (both hosts)
- Add a deterministic contract test (new `[Fact]`) that asserts `stream_events` emits a non-tool notification on stream open.
  - Prefer `event="scenes"` (snapshot) within a short timeout.
  - If IL2CPP does not emit it today, implement the same behavior and document the event/payload in `plans/mcp-interface-concept.md`.

Validation (must do)
- Mono:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478/mcp`
  - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 5`
  - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 5 -EnableWriteSmoke`
  - `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-mono-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release`
- IL2CPP regression (especially if any shared code changed):
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477/mcp`
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 10`
  - `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release`

Docs
- Keep in sync if anything changes: `plans/mcp-interface-concept.md`, `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `README-mcp.md`.

Commit
- Commits are allowed; commit your changes with a clear message.
