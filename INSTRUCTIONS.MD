# Next Iteration — Fix Inspector UI + Mono smoke

Priorities
- Rerun the browser inspector UI on the Test-VM for both hosts (IL2CPP 51477, Mono 51478): tools/list, resources/list, call GetStatus, read unity://status + unity://scenes, and confirm Server Notifications shows a tool_result via SSE. Fix the current UI URL garbling/proxy issue that prevented notifications on IL2CPP; then repeat on Mono.
- Keep Space Shooter running in an interactive GPU session; stop the game before copying mods. Leave allowWrites=false and requireConfirm=true when done; UeMcpHeadless.dll stays disabled.
- Mono write smoke failed (Tool not found: Reparent). Either expose Reparent/DestroyObject on the Mono tool surface or adjust the smoke to the supported set, then rerun `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke` and reset config.
- After fixes, rerun inspector CLI smokes on both hosts (`pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `...:51478`) and record results in `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md`.

Reference commands
- Build + deploy IL2CPP: `pwsh ./build-ml-coreclr.ps1`
- Build Mono DLL: `dotnet build src/UnityExplorer.csproj -c ML_Mono`
- Deploy Mono: `pwsh ./tools/Update-Mod-Remote.ps1 -LocalSource './Release/UnityExplorer.MelonLoader.Mono/*' -RemoteTarget 'C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/'`
- Start IL2CPP game: `Start-Process 'C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe'`
- Start Mono game: `Start-Process 'C:\codex-workspace\space-shooter-build\SpaceShooter_Mono\SpaceShooter.exe'`
- Inspector UI log example: `C:\codex-workspace\mcp-inspector-il2cpp.log`
