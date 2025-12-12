# Space Shooter Deterministic Test Plan (Unity Explorer + MCP)

## Candidate Overview
- Use the Unity Learn “Space Shooter” tutorial project (lightweight top-down 3D). If downloading via Unity Hub, pick the official Learn template; for a repo fallback use any clean fork and upgrade to 2020.3/2021.3 LTS.
- Target Windows x86_64, IL2CPP, .NET 4.x; MelonLoader supports 2018.4+, so 2020.3 or 2021.3 keeps both IL2CPP stability and loader support.
- Scene baseline (Main): Main Camera, Directional Light, Player (with Boundary box), Background, GameController, UI Canvas (score/restart/help), Audio.

## Current Test-VM Setup (validated)
- **Testing environment ownership:** Agents maintain the Space Shooter MCP hosts on the Test‑VM (IL2CPP/CoreCLR and Mono) so E2E tests stay runnable.
- Game source project: `C:\codex-workspace\space-shooter` (Unity project on Test‑VM).
- Game build (IL2CPP/CoreCLR host): `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP` (Unity 2021.3.45f1, IL2CPP, x86_64).
- Mono host: `C:\codex-workspace\space-shooter-build\SpaceShooter_Mono` running at `http://192.168.178.210:51478`; `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3` passes (2 streamed lines including `tool_result`) after switching the harness to a PowerShell HttpClient line reader.
- Unity Editor installed at `C:\Program Files\Unity 2021.3.45f1` (product version `2021.3.45f1_0da89fac8e79`).
- Loader/Mods: MelonLoader 0.7.2-ci (nightly) + `Mods\UnityExplorer.ML.IL2CPP.CoreCLR.dll` 4.12.8.
- MCP config: `Mods\sinai-dev-UnityExplorer\mcp.config.json` (`Enabled=true`, `BindAddress=0.0.0.0`, `Port=51477`, `AuthToken=changeme`, writes disabled).
- UE UI issue: dropdown Il2Cpp cast throws; mitigated with `Mods\UeMcpHeadless.dll` (small Melon patch that swallows InitUI exception) so MCP still starts. Keep it loaded until a real UE fix lands.
- Discovery file: `%TEMP%\unity-explorer-mcp.json` (refresh by deleting before launch).
- Launch: `Start-Process 'C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe' -ArgumentList '-seed=1234'`.
- Deployment note: stop `SpaceShooter.exe` before `Update-Mod-Remote.ps1` (SCP fails on locked DLLs), then restart the game.
- 2025-12-12: Smoke (`pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477`) passes after rebuilding/deploying UnityExplorer 4.12.8 (SetTimeScale value reporting fix + test helper clones), but `Run-McpContractTests.ps1` now fails: `WriteToolsContractTests` cause connection reset/`SpaceShooter.exe` exit; `SelectObject` requests currently hang the server (even with writes disabled) and TimeScale responses remain `value=0` after setting to 1.

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
