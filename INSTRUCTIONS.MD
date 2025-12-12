# Iteration Instructions (Mono SpaceShooter MCP bring-up)

Goal: create a Mono build of Space Shooter on the Test-VM, install UnityExplorer Mono + MCP, and run the Mono smoke script.

## 1) Local sanity
- Run `dotnet build src/UnityExplorer.csproj -c ML_Mono` (nullable warnings in `McpSimpleHttp` are known); fix any new regressions.

## 2) Build Mono SpaceShooter on Test-VM
- Unity Editor lives at `C:\Program Files\Unity 2021.3.45f1`.
- Open `C:\codex-workspace\space-shooter` in Unity; set Scripting Backend to Mono, Target Platform Windows x86_64, .NET 4.x.
- Build to `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono` (do not overwrite `SpaceShooter_IL2CPP`).
- Install MelonLoader (match IL2CPP host version) into the Mono build.
- Copy `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll` into the Mono build `Mods/` folder.
- Create/update `Mods\sinai-dev-UnityExplorer\mcp.config.json` with `enabled=true`, `bindAddress=0.0.0.0`, and a port not clashing with IL2CPP (use 51478 if 51477 is busy); keep writes disabled by default.

## 3) Validate Mono MCP
- Launch the Mono build `SpaceShooter.exe`; wait for UnityExplorer to load and confirm `%TEMP%\unity-explorer-mcp.json` exists.
- Run `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:<port> -LogCount 10 -StreamLines 3` (from Linux if bindAddress is 0.0.0.0, else run on the VM). Capture PASS/FAIL and errors.
- If MCP is unreachable, collect `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono\MelonLoader\Latest.log` and note blockers.

## 4) Docs & plan sync
- Update `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` with Mono build path, port, MelonLoader version, and smoke result.
- Touch `README-mcp.md` / `plans/mcp-interface-concept.md` only if DTO or config shapes change.

## 5) Commit
- Commit all changes from this iteration with a clear message.
