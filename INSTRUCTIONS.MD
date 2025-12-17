# Next iteration instructions (Iteration 21)

Focus (do in this order)
1) Implement Mono `CallMethod` guarded write tool parity with IL2CPP.
2) Extend Mono write smoke (`-EnableWriteSmoke`) to cover `CallMethod` with a harmless, allowlisted call.
3) Add a small contract test that asserts `unity://console/scripts` and `unity://hooks` are listed and readable on both hosts.

Rules / gates
- Inspector validation is CLI-only (no UI).
- Test-VM validation must happen in the same iteration as any behavior change.
- IL2CPP regression is required when shared query/DTO code changes: run inspector CLI + smoke + contract tests.
- Keep writes off by default; any write-smoke must reset config at the end.

Targets
- IL2CPP host: `http://192.168.178.210:51477/mcp`
- Mono host: `http://192.168.178.210:51478/mcp`
- Discovery files in repo: `ue-mcp-il2cpp-discovery.json`, `ue-mcp-mono-discovery.json`

Tasks

1) Mono `CallMethod` parity
- Add `CallMethod(objectId, componentType, method, argsJson="[]", confirm=false)` to the Mono host.
- Enforce the same gating semantics as other guarded tools:
  - `allowWrites=true` required.
  - if `requireConfirm=true`, require `confirm=true`.
  - enforce `reflectionAllowlistMembers` entries using the same key format as IL2CPP (`<Type>.<Member>`).
- Return shape should match the concept doc: `{ ok: true, result: <value|null> }` or `{ ok: false, error: { kind, message, hint? } }`.
- Update `plans/mcp-interface-concept.md` only if you must (it is the source of truth).

2) Extend Mono write smoke (`tools/Invoke-McpSmokeMono.ps1`)
- When `-EnableWriteSmoke` is set, add a `CallMethod` step with a harmless target.
  - Recommendation: call `GetInstanceID` on the `UnityEngine.UI.Image` component on a SpawnTestUi block.
  - Add `UnityEngine.UI.Image.GetInstanceID` to `reflectionAllowlistMembers` for the smoke.
- Keep the existing reset behavior: always end with `allowWrites=false` and restore `enableConsoleEval`/allowlists.

3) Contract test for Mono console/hooks resources
- Add a deterministic contract test (new `[Fact]`) that:
  - Calls `resources/list` and asserts `unity://console/scripts` and `unity://hooks` are present.
  - Calls `read_resource` (or `/read`) for both URIs and asserts the payload has `{ Total, Items }`.
- Run the test against BOTH hosts.

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
