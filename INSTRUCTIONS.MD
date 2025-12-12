# Iteration Instructions (Fix Mono build + host)

Goal: create the SpaceShooter_Mono build on the Test-VM and bring up the Mono MCP host.

## 1) Repair the Mono build on the Test-VM
- Inspect `C:\codex-workspace\space-shooter\unity-space-shooter-2019` build scripts; add/restore `BuildCommands.BuildWindows64Mono` (or adjust the batch command) so the CLI build works.
- Retry the Unity batch build that writes `C:\codex-workspace\space-shooter\build.log`, targeting `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono`.

## 2) If batch still fails
- Open Unity GUI on the Test-VM, switch backend to Mono, and build Windows x86_64 to `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono`.
- Capture any errors or screenshots for later debugging.

## 3) Deploy MelonLoader + UE Mono + MCP
- Install MelonLoader (same version as IL2CPP) into the new Mono build folder.
- Copy `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll` into `Mods/`.
- Enable MCP on port 51478 (keep writes off / confirm on), start the game, and confirm `%TEMP%\unity-explorer-mcp.json` is written.

## 4) Run Mono smoke from Linux
- From `UnityExplorer/`, run:
  - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3`
- Record pass/fail and any key log lines.

## 5) Docs + commit
- Update `plans/space-shooter-test-plan.md` with Mono build/smoke results.
- Keep `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md` in sync.
- Commit the changes.
