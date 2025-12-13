# Iteration Instructions — IL2CPP host bring-up + inspector smoke

Goal: Bring the IL2CPP Space Shooter MCP host (streamable-http) back online and validate inspector + contract coverage (including new list_resources/call_tool content shape).

Steps
1) Restart/redeploy the IL2CPP host on the Test-VM (port 51477): stop `SpaceShooter.exe`, then from `UnityExplorer/` run `pwsh ./build-ml-coreclr.ps1` (and `pwsh ./tools/Update-Mod-Remote.ps1` if needed), then start the game with `Start-Process "C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe" -ArgumentList "-seed=1234"`. Keep `Mods\UeMcpHeadless.dll` loaded if the dropdown cast crash still occurs; capture `MelonLoader\Latest.log` if it does.
2) Smoke with inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and confirm initialize → list_tools/resources/list/read `unity://status` → call_tool GetStatus succeed.
3) Run contract tests against that host (ensure `%TEMP%\unity-explorer-mcp.json` exists or set `UE_MCP_DISCOVERY`): `dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj`. New tests cover `list_resources` and inspector-friendly `call_tool` content; keep them green.
4) If the host fails to start or crashes (dropdown cast), grab `Latest.log` + stack and attempt a quick fix or note the blocker for the next iteration.
