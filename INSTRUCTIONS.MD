# Iteration Instructions — SpaceShooter builds + IL2CPP host recovery

Goal: bring the IL2CPP MCP host back online without the headless workaround while keeping Space Shooter builds repeatable.

Context:
- BuildCommands now falls back to a default scene (Main → SampleScene → first .unity) and forces CPU lighting/denoisers off in batchmode to avoid the OptiX crash; synced to both Unity projects via `Update-SpaceShooter-BuildScripts-Remote.ps1`.
- Remote builds succeeded on 2025-12-13: `pwsh ./tools/Build-SpaceShooter-Remote.ps1 -SkipIl2Cpp` (Mono log `build-mono-20251213-030649.log`) and `-SkipMono` (IL2CPP log `build-il2cpp-20251213-030728.log`). Outputs live under `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono` / `...\SpaceShooter_IL2CPP`; `Mods/` and `MelonLoader/` directories stayed in place.

Next steps:
1) Restart/redeploy the IL2CPP SpaceShooter build on the Test-VM and verify MCP availability at `http://192.168.178.210:51477` (`Run-McpInspectorCli` + `Invoke-McpSmoke` / contract tests). Collect MelonLoader + latest build logs if it still fails.
2) Fix the UnityExplorer dropdown Il2Cpp cast crash and remove `Mods\\UeMcpHeadless.dll`; redeploy the mod and rerun inspector/contract tests once fixed.
3) Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` in sync with any changes; retain the BuildCommands scene fallback + headless lighting defaults and rerun `pwsh ./tools/Build-SpaceShooter-Remote.ps1` after edits to confirm repeatable builds.
