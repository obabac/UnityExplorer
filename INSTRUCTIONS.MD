# Iteration 6/10 — Browser inspector UI recheck + Mono world-pick parity

Goal
- Bring back win-dev-vm MCP @8083 so we can drive the Windows browser inspector UI.
- Re-run the inspector UI (actual browser) on IL2CPP 51477 and Mono 51478; confirm GetCameraInfo + MousePick UI/world shapes and note UI schema state.
- Resolve/record Mono world-mode MousePick Items shape (null vs []), keeping configs safe.

Rules
- Any behavior change ⇒ validate on the Test-VM in the SAME iteration.
- Inspector is a gate: browser UI on both hosts + inspector CLI + Mono smoke after the UI pass.
- Use Space Shooter on the Test-VM; avoid game-specific assumptions.
- Keep configs safe when done (`allowWrites=false`, `requireConfirm=true`). Leave `Mods\\UeMcpHeadless.dll` disabled.
- Keep docs in sync (`plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md` if shapes change).

Test-VM hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

Tasks
0) If builds are stale, rebuild + deploy first:
   - IL2CPP/CoreCLR: `pwsh ./build-ml-coreclr.ps1`
   - Mono: `dotnet build src/UnityExplorer.csproj -c ML_Mono` then deploy via `pwsh ./tools/Update-Mod-Remote.ps1 -LocalSource "./Release/UnityExplorer.MelonLoader.Mono/*" -RemoteTarget "C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/"`
   - Restart both games in the GPU session and confirm hosts respond.
1) Restore win-dev-vm UI MCP on :8083 so `win-dev-vm-ui` tools work. If blocked, capture what you tried and the error.
2) Browser inspector UI on Windows (actual UI):
   - Ensure `C:\\codex-workspace\\Start-McpInspectorUi.ps1` exists; launch for IL2CPP (`-BaseUrl http://192.168.178.210:51477 -LogPath C:\\codex-workspace\\mcp-inspector-il2cpp-ui.log -Port 6277`) and Mono (`-BaseUrl http://192.168.178.210:51478 -LogPath C:\\codex-workspace\\mcp-inspector-mono-ui.log -Port 6278`).
   - In EACH UI, verify: tools/resources lists load without schema errors; call `GetCameraInfo` and confirm it matches `plans/mcp-interface-concept.md`; MousePick UI flow: `SetConfig {allowWrites:true, requireConfirm:true}` → `SpawnTestUi` (confirm) → `MousePick {mode:"ui", normalized:true, x:0.25, y:0.25}` expect `Items` length >=1 and `Id==Items[0].Id` → `DestroyTestUi` (confirm) → `SetConfig {allowWrites:false, requireConfirm:true}`.
   - World MousePick: `MousePick {mode:"world", normalized:true, x:0.5, y:0.5}`; expect `Items` null/absent. Capture actual IL2CPP vs Mono and align if possible.
3) CLI gates after UI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`, same for 51478, then `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke`. Ensure configs end with `allowWrites=false` / `requireConfirm=true`.
4) Contract tests: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`.
5) Update `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md` with the UI findings, world-pick parity notes, and any blockers; update `plans/mcp-interface-concept.md` if shapes change.
6) Write next iteration instructions into `INSTRUCTIONS.MD` (iteration 7/10), then commit.

Finish
- Leave configs safe (`allowWrites=false`, `requireConfirm=true`).
- Keep notes on any 8083/inspector UI blockers.
