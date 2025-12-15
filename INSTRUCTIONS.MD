# Iteration 6/10 — Restore Test-VM MCP control plane (8083) + real browser UI run

Context
- Browser inspector UI validation is currently blocked because the Test‑VM MCP control plane at `http://192.168.178.210:8083/mcp` is unreachable.
- Last run used JSON-RPC as a stand‑in; we still need the REAL browser UI run.

Goal
1) Restore the Test‑VM MCP control plane on port 8083.
2) Re-run the browser inspector UI on IL2CPP (51477) and Mono (51478) and record real UI findings.
3) Fix Mono world-mode `MousePick.Items` parity (`null` vs `[]`) if it’s still divergent.

Rules
- Any behavior change ⇒ validate on the Test-VM in the SAME iteration.
- Inspector is a gate: browser UI on both hosts + inspector CLI + Mono smoke after the UI pass.
- Keep configs safe when done (`allowWrites=false`, `requireConfirm=true`). Leave `Mods\\UeMcpHeadless.dll` disabled.
- Keep docs in sync (`plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md` if shapes change).

Test-VM hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

Tasks
0) Confirm the control plane is down from Linux:
   - `curl -sS -m 2 http://192.168.178.210:8083/mcp || true`

1) Restore `:8083` on the Test‑VM (do this via SSH since the MCP tools are down):
   - Check if anything listens:
     - `ssh GPUVM@192.168.178.210 "powershell -NoProfile -Command \"netstat -ano | findstr :8083\""`
   - Look for a likely service/process and restart it:
     - `ssh GPUVM@192.168.178.210 "powershell -NoProfile -Command \"Get-Service | ? { $_.Name -match 'mcp|codex|desktop|commander' -or $_.DisplayName -match 'mcp|codex|desktop|commander' } | Format-Table -Auto\""`
     - If you find a candidate service: `Restart-Service <name>`.
   - If no service: search `C:\\codex-workspace` for a launcher:
     - `ssh GPUVM@192.168.178.210 "powershell -NoProfile -Command \"Get-ChildItem C:\\codex-workspace -Recurse -File | ? { $_.Name -match 'mcp|desktop|commander|codex' } | Select-Object -First 50 FullName\""`
   - Start the discovered launcher/exe (capture the exact command used).
   - Re-check `curl` to confirm `:8083` responds.

2) If builds are stale, rebuild + deploy first (only if needed):
   - IL2CPP/CoreCLR: `pwsh ./build-ml-coreclr.ps1`
   - Mono: `dotnet build src/UnityExplorer.csproj -c ML_Mono` then deploy via:
     `pwsh ./tools/Update-Mod-Remote.ps1 -LocalSource "./Release/UnityExplorer.MelonLoader.Mono/*" -RemoteTarget "C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/"`
   - Restart both games in the GPU session and confirm hosts respond.

3) Real browser inspector UI run (requires 8083 restored):
   - Use `tools/Start-McpInspectorUi.ps1` on the VM to launch two UI instances:
     - IL2CPP: `pwsh C:\\codex-workspace\\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51477 -LogPath C:\\codex-workspace\\mcp-inspector-il2cpp-ui.log -Port 6277`
     - Mono: `pwsh C:\\codex-workspace\\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51478 -LogPath C:\\codex-workspace\\mcp-inspector-mono-ui.log -Port 6278`
   - In EACH UI, verify:
     - Tools/resources lists load without schema errors.
     - `GetCameraInfo` matches `plans/mcp-interface-concept.md`.
     - UI MousePick multi-hit flow:
       1) `SetConfig {allowWrites:true, requireConfirm:true}`
       2) `SpawnTestUi(confirm:true)`
       3) `MousePick {mode:"ui", normalized:true, x:0.25, y:0.25}` → expect `Items.length>=1` and `Id==Items[0].Id`
       4) `DestroyTestUi(confirm:true)`
       5) `SetConfig {allowWrites:false, requireConfirm:true}`
     - World MousePick: `MousePick {mode:"world", normalized:true, x:0.5, y:0.5}` → expect `Items` null/absent.

4) If Mono world-mode `Items` is still `[]`, fix parity in code (Mono should match IL2CPP), then repeat steps 2–3.

5) CLI gates after UI:
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke`
   - Ensure configs end with `allowWrites=false` / `requireConfirm=true`.

6) Contract tests:
   - `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`

Finish
- Update `plans/unity-explorer-mcp-plan.md` + `plans/unity-explorer-mcp-todo.md` with real UI findings and the 8083 restoration steps.
- Write next iteration instructions into `INSTRUCTIONS.MD` (iteration 7/10), then commit.
