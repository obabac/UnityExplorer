# Iteration 5/10 — Inspector UI validation (browser) on both hosts

Goal
- Validate the browser inspector UI on IL2CPP + Mono using the Test‑VM (no manual typing).
- Explicitly verify `MousePick` UI multi-hit ordering/primary Id and `GetCameraInfo` payloads in the inspector UI.

Rules
- Any behavior change ⇒ validate on the Test-VM in the SAME iteration.
- Inspector is a gate: use the browser UI (not only CLI) on both hosts, then run inspector CLI + smokes.
- Use Space Shooter on the Test-VM; avoid game-specific assumptions.
- Keep configs safe when done (`allowWrites=false`, `requireConfirm=true`). Leave `Mods\\UeMcpHeadless.dll` disabled.
- Keep docs in sync (`plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md` if shapes change).

Test-VM hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

Tasks
0) Deploy current repo build to the Test‑VM first:
   - IL2CPP/CoreCLR: `pwsh ./build-ml-coreclr.ps1` (build + deploy)
   - Mono: `dotnet build src/UnityExplorer.csproj -c ML_Mono` then deploy to `SpaceShooter_Mono` using:
     `pwsh ./tools/Update-Mod-Remote.ps1 -LocalSource "./Release/UnityExplorer.MelonLoader.Mono/*" -RemoteTarget "C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/"`
   - Restart both games in an interactive GPU session and confirm both hosts respond.

1) Browser inspector UI (on the Test‑VM Windows session):
   - Ensure `C:\\codex-workspace\\Start-McpInspectorUi.ps1` exists on the VM (copy from repo if needed).
   - Launch UI for IL2CPP:
     `pwsh C:\\codex-workspace\\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51477 -LogPath C:\\codex-workspace\\mcp-inspector-il2cpp-ui.log -Port 6277`
   - Launch UI for Mono:
     `pwsh C:\\codex-workspace\\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51478 -LogPath C:\\codex-workspace\\mcp-inspector-mono-ui.log -Port 6278`
   - In EACH UI, verify:
     - Tools/resources lists load without schema errors.
     - Call `GetCameraInfo` and confirm the DTO shape matches `plans/mcp-interface-concept.md`.
     - UI MousePick multi-hit check:
       1) `SetConfig` → `{ allowWrites:true, requireConfirm:true }`
       2) `SpawnTestUi` with `confirm:true`
       3) `MousePick` with `{ mode:"ui", normalized:true, x:0.25, y:0.25 }`
          - Expect `Items` length >= 1 and `Id == Items[0].Id`.
       4) `DestroyTestUi` with `confirm:true`
       5) `SetConfig` → `{ allowWrites:false, requireConfirm:true }`
     - World MousePick shape check:
       - `MousePick` with `{ mode:"world", normalized:true, x:0.5, y:0.5 }`
       - Expect `Items` is null/absent (consistent across hosts).

2) CLI gates (after UI validation):
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke`
   - Ensure configs end with `allowWrites=false` / `requireConfirm=true`.

3) Contract tests:
   - `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`

Finish
- Update `plans/unity-explorer-mcp-plan.md` snapshot + `plans/unity-explorer-mcp-todo.md` status with the *actual* UI findings.
- Write next iteration instructions into `INSTRUCTIONS.MD` (Iteration 6/10), then commit.
