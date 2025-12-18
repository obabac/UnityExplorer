# Space Shooter Deterministic Test Plan (Unity Explorer + MCP)

## Candidate Overview
- Use the Unity Learn “Space Shooter” tutorial project (lightweight top-down 3D). If downloading via Unity Hub, pick the official Learn template; for a repo fallback use any clean fork and upgrade to 2020.3/2021.3 LTS.
- Target Windows x86_64, IL2CPP, .NET 4.x; MelonLoader supports 2018.4+, so 2020.3 or 2021.3 keeps both IL2CPP stability and loader support.
- Scene baseline (Main): Main Camera, Directional Light, Player (with Boundary box), Background, GameController, UI Canvas (score/restart/help), Audio.

## Current Test-VM Setup (validated)
- **Testing environment ownership:** Agents maintain the Space Shooter MCP hosts on the Test‑VM (IL2CPP/CoreCLR and Mono) so E2E tests stay runnable.
- Game source project: `C:\codex-workspace\space-shooter` (Unity project on Test‑VM).
- Unity projects: `C:\codex-workspace\space-shooter\unity-space-shooter-2019` (IL2CPP target) and `C:\codex-workspace\space-shooter\unity-space-shooter-2019-mono` (Mono target).
  - Sync `Assets/Editor/BuildCommands.cs` into both with `pwsh ./tools/Update-SpaceShooter-BuildScripts-Remote.ps1` (uses `scp` to the two project paths above).
  - Remote builds: `pwsh ./tools/Build-SpaceShooter-Remote.ps1 -SkipMono` (IL2CPP) or `-SkipIl2Cpp` (Mono); logs land in `C:\codex-workspace\space-shooter-build\logs\` and outputs stay under `...\SpaceShooter_IL2CPP` / `...\SpaceShooter_Mono`.
  - Build script now auto-picks the first enabled build scene, or falls back to `Assets/Scenes/Main.unity` → `Assets/Scenes/SampleScene.unity` → the first `.unity` asset if none are enabled; in batchmode it pins lighting to CPU and disables denoisers to avoid the OptiX headless crash.
- Allowed: modify this Unity project to keep IL2CPP + Mono builds repeatable and as similar as possible.
- Game build (IL2CPP/CoreCLR host): `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP` (Unity 2021.3.45f1, IL2CPP, x86_64).
- Mono host: `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono` running at `http://192.168.178.210:51478`; `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3` should pass (2 streamed lines including `tool_result`). Mono exposes `SpawnTestUi` / `DestroyTestUi` behind `allowWrites+confirm` and `Reparent` / `DestroyObject` gated to those test blocks.
  - Note: MelonLoader loads from `Release/UnityExplorer.MelonLoader.Mono/Mods/UnityExplorer.ML.Mono.dll`. `dotnet build src/UnityExplorer.csproj -c ML_Mono` now copies into `Mods/`, but if Mono seems stale run: `sha256sum Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll Release/UnityExplorer.MelonLoader.Mono/Mods/UnityExplorer.ML.Mono.dll`.
- Unity Editor installed at `C:\Program Files\Unity 2021.3.45f1` (product version `2021.3.45f1_0da89fac8e79`).
- Loader/Mods: MelonLoader 0.7.2-ci (nightly) + `Mods\UnityExplorer.ML.IL2CPP.CoreCLR.dll` 4.12.8.
- MCP config: `Mods\sinai-dev-UnityExplorer\mcp.config.json` (`Enabled=true`, `BindAddress=0.0.0.0`, `Port=51477`, `AuthToken=changeme`, writes disabled).
- UE UI dropdown refresh guard added; log shows a warning `Failed to refresh UI dropdowns; continuing without failsafe: ...` on init. `Mods\UeMcpHeadless.dll` is renamed to `.disabled` on the Test-VM.
- Discovery file: `%TEMP%\unity-explorer-mcp.json` (refresh by deleting before launch).
- Launch: `Start-Process 'C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe' -ArgumentList '-seed=1234'`.
- Deployment note: `Update-Mod-Remote.ps1` stages to `$HOME/ue-mcp-stage/<Target>/` via `scp` and then copies with remote PowerShell to the final game folder (avoids scp drive-letter issues). Use `-Target Il2Cpp|Mono|Both` (default Il2Cpp) and `-StopGame` if `SpaceShooter.exe` is running/locked. Defaults target `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP` and `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono\Mods\`.
- 2025-12-13: Mono host responds on `http://192.168.178.210:51478` (Mono smoke + inspector CLI smoke should pass). IL2CPP host on `http://192.168.178.210:51477` is up after the dropdown guard; inspector CLI smoke, Invoke-McpSmoke, and MCP contract tests (47 passed, 1 skipped) pass without `UeMcpHeadless.dll`.
- Gate: run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl <baseUrl>` early for any protocol/DTO changes (both hosts).
- Inspector validation is CLI-only: use `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl <baseUrl>` or the direct `npx @modelcontextprotocol/inspector --cli` one-liners in `README-mcp.md`.
- win-dev control plane (host automation): scheduled tasks `McpProxy8082` (`mcp-proxy` → `desktop-commander.cmd`) and `McpProxy8083` (`mcp-proxy` → `mcp-control.cmd`) listen on `http://192.168.178.210:808{2,3}/mcp`; logs: `C:\codex-workspace\logs\mcp-proxy-808{2,3}.log`. Patched to create a transport when `mcp-session-id` is provided on the first `initialize`; after a proxy restart, seed the client session id with one initialize so `win-dev-vm-command/get_config` (8082) and `win-dev-vm-ui/get_screen_size` (8083) succeed. Requests need `Accept: application/json, text/event-stream`; methods are `tools/list` + `tools/call` (e.g., `get_config`, `get_screen_size`). Restart with `Start-ScheduledTask -TaskName 'McpProxy8082'/'McpProxy8083'` if down.
- Pending: monitor the dropdown guard on other IL2CPP titles and keep `UeMcpHeadless.dll` disabled; maintain repeatable build automation for both outputs.

## Determinism Hardening
- Frame pacing: `Application.targetFrameRate = 60`, vSync off, Fixed Timestep 0.02, Max Allowed Timestep 0.333.
- Random seed: add a tiny boot MonoBehaviour (e.g., `DeterministicConfig`) run first (`DefaultExecutionOrder(-1000)`):
  - Parse `-seed=<int>` (default 1234) from `Environment.GetCommandLineArgs()`.
  - Call `Random.InitState(seed)` and store globally (static).
  - Optional: write seed to a `DeterminismStatus` ScriptableObject for later read.
- Spawners (GameController/SpawnWaves): replace `Random.Range` spawn X with a precomputed array or seeded PRNG (e.g., `System.Random seed`). Keep wave timings deterministic (constant `startWait`, `spawnWait`, `waveWait`).
- Enemy AI (EvasiveManeuver) and rotators: replace `Random.Range` with seeded `System.Random`.
- Drops/powerups: force fixed drop table order or disable randomness.
- Physics: keep rigidbodies kinematic where possible; avoid physics-based randomness. If using forces, keep masses and drag fixed and avoid friction-dependent variance.
- Audio/UI: leave as-is; does not affect determinism but validates UE hierarchy.

## Build Configuration (Unity)
- Player Settings: IL2CPP, x86_64, .NET 4.x, API Compatibility Level .NET Standard 2.1, Scripting Backend IL2CPP.
- Resolution: Windowed 1920x1080, DPI scaling off, Run in Background on (prevents pause).
- Disable “Resizable Window” to keep aspect; disable “Fullscreen” to simplify captures.
- Project Settings:
  - Quality: disable VSync, fixed timestep 0.02.
  - Time: set Maximum Allowed Timestep 0.333, set `Time.captureFramerate = 60` in boot if you want stricter determinism.
  - Input: keep default axes; no randomness here.

## MelonLoader/UnityExplorer Injection
- Build the game once; install MelonLoader (latest stable) into the built folder.
- Drop UnityExplorer + MCP build into `Mods/`.
- Ensure `mcp.config.json` sets `bindAddress/port`, `allowWrites` as needed; confirm discovery file is written under `%TEMP%/unity-explorer-mcp.json`.

## MCP Test Flow (minimal but high signal)
- Boot check: game launches with MelonLoader + UE logs clean; MCP discovery file present.
- Handshake: `initialize`, `ping` succeed.
- Resources:
  - `unity://status` returns correct Unity version, scene count, selection empty.
  - `unity://scenes` shows `Main` loaded.
  - `unity://scene/{id}/objects` includes Player, Boundary, GameController, Main Camera, Directional Light, UI Canvas, Background.
  - `unity://object/{id}` and `/components` resolve for Player (Transform, Rigidbody, Collider, PlayerController) and for GameController (game flow script).
  - `unity://search?name=Player` and `?type=GameController` return deterministic IDs across runs.
  - `unity://camera/active` reports Main Camera.
  - `unity://selection` updates when selecting Player and an enemy.
- Streams: selection change events and spawned enemy/bullet events surface via `stream_events`.
- Determinism probe:
  - Run with `-seed=1234`; after wave 1 completes (e.g., at t≈25s), count enemies destroyed/spawned; repeat run and assert identical counts and object paths.
  - Optionally capture object list at fixed timestamps (t=5s, 15s, 25s) and diff.
- Guarded write sanity check (only with `allowWrites=true`): toggle an inactive debug object or adjust `Time.timeScale` to 0.5 via a safe tool, then restore.

## Edge Cases / Gotchas
  - Unity version drift: upgrading beyond 2021.3 can change physics determinism; keep to 2020.3/2021.3 for now.
  - IL2CPP build must be Development + Script Debugging off (to match player behavior) unless you need symbols for injection debugging.
  - If project uses legacy UnityWebPlayer assets, strip them; keep only standalone build settings.
  - Ensure all randomness funnels through the seeded `System.Random` or `Random.InitState` path; search for `Random.` to confirm.

## Quick MCP Smoke (PowerShell, Test-VM)
- File: `C:\codex-workspace\ue-mcp-headless\call-mcp.ps1` (reads body from `req.json`, posts to MCP, pretty prints JSON).
- Examples (set `req.json` then run script):
  - `initialize` → `{"protocolVersion":"2024-11-05","serverInfo":{"name":"UnityExplorer.Mcp","version":"4.12.8.0"}}`
  - `list_tools` → 10 tools (GetStatus/ListScenes/ListObjects/GetObject/GetComponents/SearchObjects/GetCameraInfo/MousePick/TailLogs/GetSelection).
  - `GetStatus` → Ready=true, Unity 2021.3.45f1, ScenesLoaded=1.
  - `ListScenes` → Main scene, RootCount=7.
  - `ListObjects` → 13 objects including Main Camera, GameController, UI canvas/text, Background.
  - `GetComponents` on `obj:<id>` → Transform/Camera/AudioListener, etc.
  - `TailLogs` → shows MCP bind line `MCP (streamable-http) listening on http://0.0.0.0:51477`.

## Latest live payloads (2025-12-10)
- `list_tools` returns typed schemas with enums/defaults and currently lists: GetStatus, ListScenes, ListObjects, GetObject, GetComponents, GetVersion, SearchObjects, GetCameraInfo, MousePick, TailLogs, GetSelection, SetConfig, GetConfig, SetActive, ConsoleEval, SetMember, CallMethod, AddComponent, RemoveComponent, HookAdd, HookRemove, Reparent, DestroyObject, SelectObject, GetTimeScale, SetTimeScale, SpawnTestUi, DestroyTestUi.
- `GetStatus` (call_tool): `{ Version="0.1.0", UnityVersion="2021.3.45f1", Platform="WindowsPlayer", Runtime="IL2CPP", ExplorerVersion="4.12.8", Ready=true, ScenesLoaded=1, Selection=[] }`.
- `GetCameraInfo`: `{ IsFreecam=false, Name="Main Camera", Fov=60, Pos={X=0,Y=10,Z=5}, Rot={X=90,Y=0,Z=0} }`.
- `MousePick` world (normalized center 0.5/0.5): `{ Mode="world", Hit=true, Id="obj:<id>", Items=null }`.
- `TailLogs` count=5: UniverseLib init + MCP bind lines with `Source="unity"`, `Category=null`.

## MCP Harness Coverage (Space Shooter)
- Harness path: `C:\codex-workspace\ue-mcp-headless\call-mcp.ps1` (reads JSON from `req.json` in the same folder and POSTs to `/message`).
- The script now ships built-in scenarios: `-Scenario search|camera|mouse-world|mouse-ui|status|logs|selection|initialize|list_tools|events` (default `custom` reads `req.json`). It resolves BaseUrl from `-BaseUrl` or discovery (`UE_MCP_DISCOVERY` or `%TEMP%/unity-explorer-mcp.json`); when run on the Test-VM it can fall back to `http://127.0.0.1:51477`, while from the Linux dev machine you must target `http://192.168.178.210:51477`. The script applies the bearer token if set. Examples:
  - `pwsh C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario search`
  - `pwsh C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario mouse-world`
  - `pwsh C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario mouse-ui -StreamLines 3` (after `SpawnTestUi`)
  - `pwsh C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario events -StreamLines 5`
- Use the following `req.json` payloads to cover the MCP surface on Space Shooter (set `baseUrl` in the script or use discovery / the Test-VM address `http://192.168.178.210:51477`):

SearchObjects (name + type):
```json
{"jsonrpc":"2.0","id":"search","method":"call_tool","params":{"name":"SearchObjects","arguments":{"query":"Player","type":"GameController","limit":5,"offset":0}}}
```
Expected: `Items` contains Player and GameController entries with stable `/Main/...` paths.

GetCameraInfo (baseline + freecam):
```json
{"jsonrpc":"2.0","id":"camera","method":"call_tool","params":{"name":"GetCameraInfo","arguments":{}}}
```
Expected baseline: `IsFreecam=false`, `Name` "Main Camera", `Fov` ~60, position matches the main camera. Toggle UE Freecam in the UI and rerun to see `IsFreecam=true` with `Name` "UE_Freecam" (or `Main Camera` when "Use Game Camera" is on); position/rotation should match the freecam transform.

MousePick world (center ray):
```json
{"jsonrpc":"2.0","id":"mouse-world","method":"call_tool","params":{"name":"MousePick","arguments":{"mode":"world","normalized":true,"x":0.5,"y":0.5}}}
```
Expected: `Mode="world"`, `Hit=true` with an `Id` near the player/camera forward path; when nothing is hit, `Hit=false` and `Id=null`.

MousePick UI (normalized; requires SpawnTestUi):
```json
{"jsonrpc":"2.0","id":"mouse-ui","method":"call_tool","params":{"name":"MousePick","arguments":{"mode":"ui","normalized":true,"x":0.25,"y":0.25}}}
```
Preparation: enable writes (`SetConfig allowWrites=true requireConfirm=true`), call `SpawnTestUi` with `confirm=true`, then run the pick. Expected: `Items` contains `McpTestBlock_*` entries; `Id` equals the first item. Clean up with `DestroyTestUi` and reset config.

stream_events sanity:
```json
{"jsonrpc":"2.0","id":"events","method":"stream_events","params":{}}
```
Run the stream in one terminal, then call `GetStatus`/`TailLogs` in another; expect a `tool_result` notification plus any `log`/`scenes` updates. Closing the stream should end cleanly without retries.

## Safe Write Scenarios (Space Shooter)
- Enable guarded writes briefly:
```json
{"jsonrpc":"2.0","id":"cfg-on","method":"call_tool","params":{"name":"SetConfig","arguments":{"allowWrites":true,"requireConfirm":true}}}
```
- Toggle a benign object and revert:
```json
{"jsonrpc":"2.0","id":"set-active","method":"call_tool","params":{"name":"SetActive","arguments":{"objectId":"obj:<id>","active":false,"confirm":true}}}
```
Use a harmless object (e.g., a debug helper) and set it back to `true` afterwards. Selection can be nudged with `SelectObject` the same way. Always finish by re-enabling defaults:
```json
{"jsonrpc":"2.0","id":"cfg-off","method":"call_tool","params":{"name":"SetConfig","arguments":{"allowWrites":false,"requireConfirm":true}}}
```
