# Iteration Instructions (Mono MCP host bring-up)

High-level goal: bring up a Mono Space Shooter MCP host on the Test-VM and validate the MCP smoke.

## 1) Build + deploy Mono host on Test-VM
- On the Windows VM open `C:\codex-workspace\space-shooter` in Unity 2021.3.45f1.
- Switch scripting backend to Mono (Windows x86_64, .NET 4.x); keep other player settings identical to the IL2CPP build.
- Build to `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono` (do not overwrite IL2CPP).
- Install MelonLoader (same line as IL2CPP) into the Mono build.

## 2) Deploy UnityExplorer Mono MCP
- From repo root: `dotnet build src/UnityExplorer.csproj -c ML_Mono` (already passes; reuse the fresh output if present).
- Copy `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll` to `Mods\` in the Mono build.
- Add/update `Mods\sinai-dev-UnityExplorer\mcp.config.json` with `enabled=true`, `bindAddress=0.0.0.0`, `port=51478` (or another free port), writes disabled.
- Ensure `%TEMP%\unity-explorer-mcp.json` is written on launch; pick a port that does not conflict with the IL2CPP host (51477).

## 3) Validate Mono MCP
- Start the Mono build (`C:\codex-workspace\space-shooter-build\SpaceShooter_Mono\SpaceShooter.exe`) and wait for the discovery file.
- Run `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:<port> -LogCount 10 -StreamLines 3` from this repo (or on the VM if bound to localhost).
- Capture results: pass/fail, log snippets, endpoint URL/port, Unity version.

## 4) Docs + plans
- Update `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` with Mono host path/port/smoke result and any gaps.
- Keep `plans/mcp-interface-concept.md` consistent if shapes change (none expected).
- Keep `README-mcp.md` user-facing notes in sync if steps change.

## 5) Working style
- Keep responses concise; avoid questions unless blocked.
- Keep plan/todo files in sync with code/docs; do not revert unrelated work.
- Preserve IL2CPP host defaults; avoid enabling writes unless needed for smoke.
- Commit changes at the end of the iteration.
