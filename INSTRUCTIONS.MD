# Iteration Instructions (Mono SpaceShooter build via GUI)

Goal: finish SpaceShooter_Mono build + deploy UE Mono + run Mono MCP smoke on the Test-VM.

1) Configure Player settings
- In Unity Editor on the Test-VM open `C:\codex-workspace\space-shooter\unity-space-shooter-2019`.
- Target platform Windows (x86_64), Scripting Backend = Mono (Other Settings), .NET 4.x; leave scenes unchanged.

2) Build SpaceShooter_Mono via GUI
- File > Build Settings: Windows target, Build to `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono`.
- Create the folder if missing; keep defaults elsewhere.

3) Install MelonLoader + deploy UE Mono
- Install MelonLoader into the Mono build.
- Copy `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll` into `SpaceShooter_Mono\Mods\`.
- Enable MCP in `SpaceShooter_Mono\Mods\sinai-dev-UnityExplorer\mcp.config.json` with `{"enabled":true,"bindAddress":"0.0.0.0","port":51478}`.

4) Start Mono game + run smoke
- Launch `SpaceShooter.exe` from the Mono build on the Test-VM.
- From Linux run `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3` and capture the result.

5) Docs/plan/commit
- Update `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` with Mono build path/status and smoke outcome.
- Commit all changes.

Notes: Previous GUI attempt to change Player settings to Mono/x86_64 was unfinished—prioritize completing that step first.
