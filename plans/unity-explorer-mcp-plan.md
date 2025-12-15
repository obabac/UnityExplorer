# Unity Explorer MCP Integration Plan (Updated)

- Updated: 2025-12-15  
- Owner: Unity Explorer MCP (p-unity-explorer-mcp)  
- Goal: Expose Unity Explorer’s runtime capabilities through an in‑process MCP server with **streamable HTTP** transport, making game state and safe controls available as MCP resources, tools, and streams.

This plan merges the original scope, the current implementation snapshot, and the TODO list into a single up‑to‑date document.

### Latest iteration snapshot (2025-12-15)
- UnityExplorer MCP game hosts remain up on the Test-VM: IL2CPP `http://192.168.178.210:51477` and Mono `http://192.168.178.210:51478` (`/message` returns 200).
- win-dev control plane alias surface validated: `McpProxy8083` (mcp-control) now exposes the harness tool names; seed the session after a restart with `initialize` that includes `Accept: application/json, text/event-stream` plus a `clientInfo` payload (e.g., `protocolVersion=2024-11-05`, `capabilities={}`) and the `win-dev-vm-ui/State-Tool` + `win-dev-vm-ui/Powershell-Tool` succeed. Logs: `C:\codex-workspace\logs\mcp-proxy-808{2,3}.log`.
- Contract tests pass against IL2CPP (55 passed / 1 skipped). Mono world MousePick parity gap persists (`Items=[]` vs `null`).
- Inspector UI (IL2CPP) launched via `Start-McpInspectorUi.ps1`; headless Edge capture saved to `C:\codex-workspace\inspector-il2cpp-ui.png` (interactive page still needs a visible render through win-dev UI). Next: rerun the UI flow on IL2CPP + Mono with visible rendering, then close remaining parity gaps.

---

### Decisions (2025-12-13)

- Mono now ships guarded writes (SetConfig/SetActive/SelectObject/TimeScale) behind `allowWrites` + `requireConfirm`; use the opt-in Mono write smoke when validating on hosts.
- Space Shooter project changes on the Test‑VM are allowed to improve repeatable Mono/IL2CPP rebuilds (source: `C:\\codex-workspace\\space-shooter`).
- Dropdown Il2Cpp cast crash is guarded; keep watching logs across titles and leave `UeMcpHeadless.dll` disabled.
- Treat inspector validation as a first-class gate: run `tools/Run-McpInspectorCli.ps1` early on both hosts for any wire/schema change.

### Planned next 10 iterations (planner)

Pre-reqs done: inspector CLI gate (`tools/Run-McpInspectorCli.ps1`), removed the `UeMcpHeadless.dll` workaround, Mono baseline guarded writes, repeatable Space Shooter IL2CPP+Mono rebuilds, and the win-dev control plane restored via `McpProxy8082/8083` on 8082/8083.

1) Inspector UI gate: run `tools/Start-McpInspectorUi.ps1` against IL2CPP + Mono via the restored win-dev command/UI endpoints and exercise a minimal UI flow (tools list, read `unity://status`, call `GetStatus`/`TailLogs`, observe one `tool_result` on stream).
2) Fix Mono MousePick world parity (`Items` should be null/omitted like IL2CPP) and add a contract test to lock the shape.
3) Add Mono discovery helper + contract run: add `ue-mcp-mono-discovery.json` and run `UnityExplorer.Mcp.ContractTests` against the Mono host.
4) Expand Mono guarded writes (tier 2): implement safe `AddComponent`/`RemoveComponent`/`CallMethod` behind allowlists + confirm and extend `Run-McpMonoSmoke.ps1 -EnableWriteSmoke`.
5) Mono console + hooks parity: enable behind `enableConsoleEval`/allowlists, add a small gated test (mirror existing IL2CPP hook tests).
6) Streams parity: add/extend contract tests for at least one non-tool notification on both hosts (selection/scenes/log) and keep rate-limit behavior stable.
7) Space Shooter env hardening: keep IL2CPP + Mono builds as similar as possible, keep build scripts stable, and document any required Unity project changes.
8) Cross-title IL2CPP regression: validate the dropdown refresh guard on another IL2CPP title (or document why not available).
9) Final DoD sweep: run IL2CPP inspector CLI + Invoke-McpSmoke + contract tests and Mono inspector CLI + Mono smoke (with writes); check every TODO.
10) Add a lightweight healthcheck/watchdog for the win-dev MCP proxies (8082/8083) so failures are detected and restarted quickly (document the commands + log paths).

## 1) Context & Assumptions

- UnityExplorer lives under `UnityExplorer/` and is injected into Unity games (BepInEx, MelonLoader, standalone).
- Unity Explorer already provides:
  - Scene/Object Explorer (Scene Explorer, Object Search)
  - Inspectors (components, reflection, static/instance, dnSpy integration)
  - C# Console with editor features (line numbers, syntax highlighting, auto-complete, script load/save/compile)
  - Hook Manager
  - Mouse Inspect / Freecam (including UI Inspector Results panel)
  - Log panel with optional Unity debug forwarding and disk logging
  - Time-scale widget and keybinds (lock/pause/speed change)
  - Settings, Clipboard, etc.
- MCP clients (IDEs, agents, inspector tools) connect over HTTP to inspect and, when allowed, control the running game.
- Target: Windows first, CoreCLR IL2CPP builds. Mono/legacy MelonLoader builds are now in scope as a follow‑up MCP phase and tracked as a priority in TODO section 11. For MCP deployment we use `Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR` (and `Release/UnityExplorer.MelonLoader.IL2CPP` if needed); full build.ps1 legacy packages are optional.

---

## 2) Architecture & Transport

- **Placement:** In‑process C# server inside the CoreCLR UnityExplorer DLL (`#if INTEROP`).
- **Server:** `UnityExplorer.Mcp.McpSimpleHttp`
  - Binds to `bindAddress:port` from `mcp.config.json` (defaults: `0.0.0.0:51477`).
  - Discovery file: `%TEMP%/unity-explorer-mcp.json` with:
    - `pid`, `port`, `baseUrl`, `modeHints: ["streamable-http"]`, `startedAt`.
- **Endpoints:**
  - `POST /message`
    - JSON‑RPC 2.0 requests:
      - `initialize`
      - `ping`
      - `list_tools`
      - `call_tool`
      - `read_resource`
      - `stream_events`
  - `GET /read?uri=unity://...`
    - Convenience wrapper over `read_resource` for quick testing.
- **Threading:**
  - All Unity access is marshalled via `MainThread.Run/RunAsync`.
- **Security:**
  - Configurable `bindAddress` and `port`.
  - Writes disabled by default (`allowWrites=false`); guarded tools require explicit opt‑in and often confirmation.

---

## 3) MCP Surface (Resources, Tools, Streams)

### 3.1 Resources (`unity://…`)

Already implemented:

- `unity://status`  
  Status snapshot (`StatusDto`): Unity version, platform, runtime, Explorer version, scenes loaded, ready flag, selection summary.

- `unity://scenes`  
  Paged `Page<SceneDto>`: `{ Id, Name, Index, IsLoaded, RootCount }`.

- `unity://scene/{sceneId}/objects`  
  Paged `Page<ObjectCardDto>`: object cards with id, name, path, tag, layer, active flag, component count.

- `unity://object/{id}`  
  Object details card (id, name, path, basic transform, components summary).

- `unity://object/{id}/components`  
  Paged `Page<ComponentCardDto>`: `{ Type, Summary }`.

- `unity://search?query=&name=&type=&path=&activeOnly=&limit=&offset=`  
  Object search across scenes using Explorer’s object traversal.

- `unity://selection`  
  `SelectionDto`: `{ ActiveId, Items[] }` from Inspector state.

- `unity://camera/active`  
  `CameraInfoDto`: `{ IsFreecam, Name, Fov, Pos, Rot }`; honors the UE Freecam state (uses the freecam or reused game camera when active, falls back to `Camera.main`/first camera, `<none>` when missing).

- `unity://logs/tail?count=`  
  `LogTailDto`: `{ Items: [{ T, Level, Message }, …] }` from MCP log buffer.

- `unity://console/scripts`  
  `Page<ConsoleScriptDto>`: `{ Total, Items: [{ Name, Path }] }` pulled from the console scripts folder.

- `unity://hooks`  
  `Page<HookDto>`: `{ Total, Items: [{ Signature, Enabled }] }` using Harmony-style method signatures (e.g. `System.Void UnityEngine.GameObject::SetActive(System.Boolean)`).

All payloads are DTO‑based (`Dto.cs`), aiming for compact, LLM‑friendly JSON.

### 3.2 Tools

**Read tools** (`UnityReadTools`, all `[McpServerTool]`):

- `GetStatus(ct)` — returns `StatusDto`.
- `ListScenes(limit?, offset?, ct)` — returns `Page<SceneDto>`.
- `ListObjects(sceneId?, name?, type?, activeOnly?, limit?, offset?, ct)` — returns `Page<ObjectCardDto>`.
- `GetObject(id, ct)` — returns object card.
- `GetComponents(objectId, limit?, offset?, ct)` — returns `Page<ComponentCardDto>`.
- `SearchObjects(query?, name?, type?, path?, activeOnly?, limit?, offset?, ct)` — returns `Page<ObjectCardDto>`.
- `GetCameraInfo(ct)` — returns `CameraInfoDto`.
- `MousePick(mode="world", ct)` — returns `PickResultDto` with optional `obj:<instanceId>`.
- `TailLogs(count=200, ct)` — returns `LogTailDto`.
- `GetSelection(ct)` — returns `SelectionDto`.

**Meta / protocol methods** (implemented in `McpSimpleHttp`):

- `initialize`  
  - Accepts MCP `InitializeRequest`.  
  - Returns:
    - `protocolVersion` (currently `"2024-11-05"`),
    - `capabilities` (tools/resources + experimental `streamEvents`),
    - `serverInfo` (`name = "UnityExplorer.Mcp"`, `version`),
    - `instructions` string describing high‑level usage.

- `notifications/initialized`  
  - Accepted and ignored; returns a trivial `result` so inspector doesn’t error.

- `ping`  
  - Simple liveness check; returns empty `{}` result.

- `list_tools`  
  - Returns `{ tools: [{ name, description, inputSchema }, …] }`.  
  - `inputSchema` is a JSON Schema per-tool and per-argument, including enums for constrained arguments such as `MousePick.mode`; inspector should be able to render call forms from this shape.

- `call_tool`  
  - Dispatches to `UnityReadTools` / `UnityWriteTools` via `McpReflection.InvokeToolAsync` using reflection and argument coercion.

- `read_resource`  
  - Dispatches to `McpReflection.ReadResourceAsync` for `unity://...` URIs.

- `stream_events`  
  - Opens a chunked JSON line stream.  
  - Publishes:
    - `notification` events: `log`, `selection`, `scenes`, `scenes_diff`, `inspected_scene`, `tool_result`, etc.
    - Mirrored `result`/`error` payloads for other requests.
  - Stream connections now watch for client disconnects and clean up their slots to avoid leaked streams; reconnect flow covered by contract tests.

Legacy `/sse` endpoint has been removed from the active surface; new clients use only `stream_events`.

### 3.3 Guarded Write Tools

Implemented in `UnityWriteTools` (behind `allowWrites` + `requireConfirm`):

- `SetConfig(allowWrites?, requireConfirm?, restart)`  
  Update `mcp.config.json` and optionally restart `McpHost`.

- `SetActive(objectId, active, confirm = false, ct)`  
  Toggle GameObject active state (`obj:<instanceId>`), with confirmation semantics and error codes (`PermissionDenied`, `ConfirmationRequired`, etc.).

- `SetMember(objectId, componentType, member, jsonValue, confirm = false, ct)`  
  Allowlisted reflection writes for component members (uses `reflectionAllowlistMembers`).

Planned guarded writes (Phase 2+):

- Selection change, basic transform tools, component add/remove, hook operations, console eval, and exporters — all strictly opt‑in and allowlist‑controlled.

---

## 4) Config & Options

Config file: `{ExplorerFolder}/mcp.config.json`:

- `enabled`: start/stop server.
- `bindAddress`: default `"0.0.0.0"`.
- `port`: default `51477` (0 = ephemeral).
- `transportPreference`: `"auto"` (currently effectively `"streamable-http"`).
- `allowWrites`, `requireConfirm`.
- `reflectionAllowlistPath`, `reflectionAllowlistMembers`, `componentAllowlist`.
- `exportRoot`, `logLevel`.

Options panel (planned / partial wiring):

- Toggles:
  - Enable MCP, Allow Writes, Require Confirm.
- Controls:
  - Restart MCP, Copy Endpoint.
- Status:
  - Endpoint and “MCP Status” (Disabled / Enabled & Running).

---

## 5) Testing Strategy & Contract Tests

### 5.1 Dotnet Contract Tests

Project: `UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests` — these form the technical contract MCP changes must respect.

- JSON‑RPC methods:
  - `ListTools_JsonRpc_Response_If_Server_Available`
  - `ListTools_Includes_InputSchema_For_All_Tools`
  - `CallTool_GetStatus_JsonRpc_Response_If_Server_Available`
  - `ReadResource_Status_JsonRpc_Response_If_Server_Available`
  - `Initialize_JsonRpc_Response_If_Server_Available`
  - `Ping_JsonRpc_Response_If_Server_Available`
  - `StreamEvents_Endpoint_Responds_With_Chunked_Json_When_Server_Available`
  - `StreamEvents_Emits_ToolResult_Notification_When_Tool_Called`
  - `StreamEvents_Notification_Has_Generic_Shape_When_Present`

- HTTP convenience endpoints:
  - `Read_Status_Resource_If_Server_Available` (`/read?uri=unity://status`).
  - `Read_Scenes_If_Server_Available` (`/read?uri=unity://scenes`).

- Resource tests:
  - `Read_Scene_Objects_If_Server_Available`
  - `Read_Selection_If_Server_Available`
  - `Read_Camera_Active_If_Server_Available`
  - `Read_Logs_Tail_If_Server_Available`
  - `Read_Search_If_Server_Available`
  - `Hook_Lifecycle_Add_List_Remove_When_Flag_Enabled` (runs only when `UE_MCP_HOOK_TEST_ENABLED=1`; set `hookAllowlistSignatures` to include a safe type such as `UnityEngine.GameObject` and keep `requireConfirm=true`)

These tests must stay green whenever MCP code is changed; use `pwsh ./tools/Run-McpContractTests.ps1` (Release) locally/CI to run them after a CoreCLR build.

### 5.2 Manual & MCP Inspector Flows

- Build + deploy:
  - `cd UnityExplorer && pwsh ./build-ml-coreclr.ps1`
- Launch game (Test‑VM):
  - `Start-Process "C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe"`
- Logs:
  - `pwsh ./tools/Get-ML-Log.ps1` (with `-Stream` for live tail).
- Inspector:
  - `npx @modelcontextprotocol/inspector --transport http --server-url http://192.168.178.210:51477`
  - Use “List Tools”, “Call Tool”, and “Read Resource” to smoke‑test the surface.
- Smoke CLI:
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20`
  - Falls back to `%TEMP%/unity-explorer-mcp.json` or `UE_MCP_DISCOVERY` when `-BaseUrl` is omitted; runs initialize → notifications/initialized → list_tools → GetStatus/TailLogs → read status/scenes/logs and exits non-zero on errors.
- Test-VM harness:
  - `pwsh C:\codex-workspace\ue-mcp-headless\call-mcp.ps1 -Scenario search|camera|mouse-world|mouse-ui|status|logs|selection|initialize|list_tools|events`
  - `-Scenario custom` reads `req.json`; when run on the Test-VM, BaseUrl resolves from `-BaseUrl` or discovery (default `http://127.0.0.1:51477`); from the Linux dev machine, target `http://192.168.178.210:51477` instead; `-StreamLines` controls how many `stream_events` chunks are printed.

### 5.3 Mono smoke entry

- `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 10 -StreamLines 3` for Mono/net35 hosts (initialize → list_tools → GetStatus/TailLogs/MousePick → read status/scenes/logs → stream_events; fails if `tool_result` is missing).

### 5.4 Mono host validation checklist

- Step‑by‑step human checklist lives in `README-mcp.md` (Mono Host Validation Checklist).
- Current blocker: no Mono MCP host is running on this dev machine, so smoke/inspector validation is pending until a Mono game is available.

---

## 6) Roadmap / Remaining Work

Fine‑grained TODOs live in `plans/unity-explorer-mcp-todo.md`. High‑level themes:

- Mono / MelonLoader MCP host support (UnityExplorer.MelonLoader.Mono) follows a three-phase path: (A) get ML_Mono compiling again with an MCP host stub and guard non-INTEROP callers, (B) bring up a minimal read-only Mono MCP surface with a lighter JSON/HTTP stack, (C) close parity gaps and add Mono-specific smoke/contract coverage. See TODO section 11 for the detailed checklist.

1. **Transport & protocol polish**
   - Remove SSE leftovers, refine error codes, add light rate limiting.
2. **Read‑only parity**
   - Finish any gaps between implemented DTOs and the conceptual spec in `mcp-interface-concept.md`, including live validation of Freecam camera info and harness coverage for SearchObjects/MousePick on Space Shooter.
3. **Streams & notifications**
   - Document events, add tests for non‑tool notifications (logs, scenes, selection).
4. **Guarded write tools**
   - Solidify `SetActive`/`SetMember` and design next tier of safe mutations.
5. **Console & hooks**
   - ConsoleEval and hook list/add/remove are implemented; hook lifecycle contract tests are gated by `UE_MCP_HOOK_TEST_ENABLED` and expect `hookAllowlistSignatures` entries.
6. **Config**
   - MCP tools around `McpConfig` and recommended configs per environment.
7. **Inspector & DX**
   - Ensure all tools/resources have stable schemas; add CLI smoke scripts.
8. **Docs & AGENTS.md**
   - Finalize `README-mcp.md` and a short MCP quick‑start in `UnityExplorer/AGENTS.md`.

---

## 7) References

- Concept draft: `plans/mcp-interface-concept.md`
- TODOs: `plans/unity-explorer-mcp-todo.md`
- Space Shooter validation plan: `plans/space-shooter-test-plan.md`
- Non-MCP runtime feature summary: `docs/unity-explorer-game-interaction.md`
- Code:
  - `UnityExplorer/src/Mcp/*`
  - `UnityExplorer/src/ExplorerCore.cs`
  - `UnityExplorer/tools/*`
  - `UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests/*`

This plan is now the single source of truth for current design and future work; other `plans/*` files provide historical context and deeper detail.
