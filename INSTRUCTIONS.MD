# Iteration 4/10 — Inspector UX gate + Test-VM deploy + Mono smoke

Goal
- Validate inspector UI/CLI on IL2CPP + Mono after the Mono MousePick/Camera parity change.
- Ensure we are testing the *deployed* build on the Test‑VM (not a stale DLL).
- Keep configs safe when done (`allowWrites=false`, `requireConfirm=true`; leave `Mods\\UeMcpHeadless.dll` disabled).

Rules
- Any behavior change ⇒ validate on the Test-VM in the same iteration.
- Inspector is a gate: run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` (IL2CPP) and `-BaseUrl http://192.168.178.210:51478` (Mono) after changes.
- Use Space Shooter on the Test-VM; avoid game-specific assumptions.
- Keep docs in sync (`plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md` if shapes change).

Test-VM hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

Tasks
0) Deploy the current repo build to the Test‑VM before validating:
   - IL2CPP/CoreCLR: `pwsh ./build-ml-coreclr.ps1` (build + deploy)
   - Mono: `dotnet build src/UnityExplorer.csproj -c ML_Mono` then deploy to `SpaceShooter_Mono` using `pwsh ./tools/Update-Mod-Remote.ps1 -LocalSource "./Release/UnityExplorer.MelonLoader.Mono/*" -RemoteTarget "C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/"`
   - Restart both games in an interactive GPU session and confirm each host responds.
1) Inspector UX: run `tools/Start-McpInspectorUi.ps1` (or inspector CLI) on both hosts; verify MousePick UI multi-hit ordering/primaryId and camera payloads; fix any `inputSchema`/metadata issues and record findings in `plans/unity-explorer-mcp-todo.md`.
2) Safety gates:
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`
   - Ensure configs end with `allowWrites=false` / `requireConfirm=true`.
3) Contract tests: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`; note results in the plan/todo.

Finish
- Update `plans/unity-explorer-mcp-plan.md` snapshot + `plans/unity-explorer-mcp-todo.md` status (and `plans/mcp-interface-concept.md` if shapes changed).
- Write next iteration instructions into `INSTRUCTIONS.MD` (Iteration 5/10), then commit.
