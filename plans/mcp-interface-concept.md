# Unity Explorer MCP Interface — Source of Truth

- Updated: 2025-12-19
- Hosts: CoreCLR IL2CPP + Mono in-process MCP servers (INTEROP builds)
- Transport: streamable HTTP via `McpSimpleHttp` (no ASP.NET); JSON-RPC 2.0 on `POST /message` (alias `POST /mcp`); convenience `GET /read?uri=unity://...`; discovery file `%TEMP%/unity-explorer-mcp.json` advertises `{ pid, baseUrl, port, modeHints: ["streamable-http"], startedAt }`.
- Default policy: read-only. All mutating tools require `allowWrites=true`; many also require `requireConfirm=true`.

## Transport & Handshake

- Endpoints
  - `POST /message` (alias `POST /mcp`) — JSON-RPC 2.0 methods: `initialize`, `notifications/initialized`, `ping`, `list_tools`, `list_resources`, `call_tool`, `read_resource`, `stream_events`.
  - `GET /read?uri=unity://...` — wrappers `unity://...` resources; returns HTTP 200/4xx JSON when available.
  - `GET /` (or `/?…`) with `Accept: text/event-stream` — Server-Sent Events channel that mirrors JSON-RPC notifications/results/errors as `data: <json>\n\n` frames until the client disconnects (no chunked encoding). `/mcp` (or `/mcp?...`) accepts the same SSE stream for clients using a pathful base URL.
- CORS: All responses include `Access-Control-Allow-Origin: *`, `Access-Control-Allow-Headers: Content-Type, Authorization`, and `Access-Control-Allow-Methods: GET, POST, OPTIONS` (max-age 86400). `OPTIONS /message` (and other paths) returns HTTP 204 + these headers for browser preflights (inspector UI, SSE).
- `initialize` response: `{ protocolVersion: "2024-11-05", capabilities: { tools: { listChanged: true }, resources: { listChanged: true }, experimental: { streamEvents: {} } }, serverInfo: { name, version }, instructions: string }` (CoreCLR name `UnityExplorer.Mcp`, Mono name `UnityExplorer.Mcp.Mono`).
- JSON-RPC notifications that omit `id` return HTTP 202 with an empty body (e.g., `notifications/initialized` from inspector); when an `id` is present, the server returns a normal JSON-RPC `{ result: { ok: true } }` body.
- `list_tools`: each tool has `name`, `description`, `inputSchema` (JSON Schema; enums for constrained args such as `MousePick.mode`); cancellation tokens are omitted so inspector call forms stay clean.
- `list_resources`: `resources: [{ uri, name, description, mimeType }]` for every `unity://...` resource listed below.
- `call_tool` result wrapper: `{ content: [{ type: "text", mimeType: "application/json", text: "<json>", json: <object> }] }`; broadcasts a `tool_result` notification (`ok=true/false`).
- `read_resource` result wrapper: `{ contents: [{ uri, mimeType: "application/json", text: "<json>" }] }`.
- `stream_events`: chunked JSON lines; notifications use `{ "jsonrpc":"2.0", "method":"notification", "params":{ "event": "<name>", "payload": { ... } } }`. Disconnects clean up their slots; HTTP connection stays open until client closes.
- Concurrency: 32 in-flight requests (`RateLimited` error message `"Cannot have more than 32 parallel requests. Please slow down."`).

## Resources (read-only)

- `unity://status` — `StatusDto { Version, UnityVersion, Platform, Runtime, ExplorerVersion, Ready, ScenesLoaded, Selection: string[] }` (selection mirrors inspector targets, e.g., `obj:<instanceId>`).
- `unity://scenes` — `Page<SceneDto> { Total, Items: [{ Id, Name, Index, IsLoaded, RootCount }] }`; may include pseudo-scenes `scn:ddol` (DontDestroyOnLoad, `Index=-1`) and `scn:hide` (HideAndDontSave, `Index=-2`, representing UnityExplorer’s asset/Resources scene: all root GameObjects whose `scene.IsValid()==false`).
- `unity://scene/{sceneId}/objects?limit&offset` — `Page<ObjectCardDto> { Total, Items: [{ Id, Name, Path, Tag, Layer, Active, ComponentCount }] }`; accepts pseudo-scene ids (`scn:ddol`, `scn:hide`) and returns a normal page (possibly empty).
- `unity://object/{id}` — `ObjectCardDto` (same shape as list items; no transform payload today).
- `unity://object/{id}/components?limit&offset` — `Page<ComponentCardDto> { Total, Items: [{ Type, Summary }] }`.
- `unity://object/{id}/component/members?type=<fullType>&includeMethods=&limit=&offset=` — `Page<InspectorMemberDto> { Total, Items: [{ Name, Kind (field|property|method), Type, CanRead, CanWrite }] }`.
- `unity://object/{id}/component/member?type=<fullType>&name=<memberName>` — `{ ok: true, type, valueText, valueJson? }`; `valueText` is truncated at 1024 chars and UnityEngine.Object values return `obj:<id>` strings (no raw objects); `valueJson` is populated for primitives/vectors/ids only.
- `unity://object/{id}/children?limit&offset` — `Page<ObjectCardDto>`; lists direct children only; empty pages are valid when there are no children.
- `unity://search?query=&name=&type=&path=&activeOnly=&limit=&offset=` — `Page<ObjectCardDto>` using the same card shape as `ListObjects`.
- `unity://selection` — `SelectionDto { ActiveId, Items[] }`; emits a `selection` stream event when selection changes (same payload as the resource).
- `unity://clipboard` — `ClipboardDto { HasValue, Type, Preview, ObjectId? }`; Preview uses `ToStringWithType` and truncates to 256 chars; `ObjectId` is present when the clipboard holds a live `UnityEngine.Object` (`obj:<instanceId>`).
- `unity://camera/active` — `CameraInfoDto { IsFreecam, Name, Fov, Pos{X,Y,Z}, Rot{X,Y,Z} }`; falls back to `Camera.main`/first camera or `<none>` when missing.
- `unity://freecam` — `FreecamDto { Enabled, UsingGameCamera, Speed, Pos{X,Y,Z}, Rot{X,Y,Z} }`; prefers the active freecam camera transform, else the cached pose or `Camera.main`.
- `unity://logs/tail?count=` — `LogTailDto { Items: [{ T, Level, Message, Source, Category? }] }`; `[MCP] error ...` lines are written here when requests fail.
- `unity://console/scripts` — `Page<ConsoleScriptDto> { Total, Items: [{ Name, Path }] }`.
- `unity://hooks` — `Page<HookDto> { Total, Items: [{ Signature, Enabled }] }` (Harmony signatures such as `System.Void UnityEngine.GameObject::SetActive(System.Boolean)`).

## Tools

- DTOs: `BuildSceneDto { Index, Name, Path }` (Name derives from the file name in build settings; Path uses the SceneUtility path and may be empty on stripped builds). `ListBuildScenes` returns an empty page when Unity strips build scenes.

### Read tools (no allowWrites required)
- `GetStatus()` → `StatusDto` (same as `unity://status`).
- `ListScenes(limit?, offset?)` → `Page<SceneDto>`; includes pseudo-scenes `scn:ddol` / `scn:hide` with negative indexes when present.
- `ListBuildScenes(limit?, offset?)` → `Page<BuildSceneDto>`; returns build-indexed scenes when available and an empty page when Unity strips scene metadata.
- `ListObjects(sceneId?, name?, type?, activeOnly?, limit?, offset?)` → `Page<ObjectCardDto>`.
- `GetObject(id)` → `ObjectCardDto` by `obj:<instanceId>`.
- `GetComponents(objectId, limit?, offset?)` → `Page<ComponentCardDto>`.
- `ListComponentMembers(objectId, componentType, includeMethods=false, limit?, offset?)` → `Page<InspectorMemberDto>`; includes fields/properties (methods when requested) with `{ Name, Kind, Type, CanRead, CanWrite }`.
- `ReadComponentMember(objectId, componentType, name)` → `{ ok: true, type, valueText, valueJson? }`; `valueText` capped at 1024 chars; UnityEngine.Object values become `obj:<id>` strings; `valueJson` only for primitives/vectors/ids to avoid huge payloads.
- `ListChildren(objectId, limit?, offset?)` → `Page<ObjectCardDto>` of direct children only.
- `GetVersion()` → `VersionInfoDto { ExplorerVersion, McpVersion, UnityVersion, Runtime }`.
- `SearchObjects(query?, name?, type?, path?, activeOnly?, limit?, offset?)` → `Page<ObjectCardDto>`.
- `GetCameraInfo()` → `CameraInfoDto`.
- `GetFreecam()` → `FreecamDto` (same shape as `unity://freecam`).
- `MousePick(mode="world"|"ui", x?, y?, normalized=false)` → `PickResultDto { Mode, Hit, Id?, Items? }`; world mode omits `Items` (null); UI mode uses EventSystem ordering (top-most first) and `Id` mirrors the first resolvable hit (hits are filtered to GameObjects that `GetObject` can resolve; if none, Items is empty/Id null) on both IL2CPP and Mono.
- `TailLogs(count=200)` → `LogTailDto`.
- `ReadConsoleScript(path)` → `ConsoleScriptFileDto { Name, Path, Content, SizeBytes, LastModifiedUtc, Truncated }` (max 256KB; strips leading BOM).
- `GetSelection()` → `SelectionDto { ActiveId, Items[] }`.
- `GetClipboard()` → `ClipboardDto` (same shape as `unity://clipboard`).
- `GetTimeScale()` → `{ ok: true, value: float, locked: bool }`.

### Guarded writes (require `allowWrites=true`; `requireConfirm=true` forces `confirm=true` where supported)
- Config: `SetConfig(allowWrites?, requireConfirm?, enableConsoleEval?, componentAllowlist?, reflectionAllowlistMembers?, hookAllowlistSignatures?, restart=false)` → `{ ok }`; `GetConfig()` → `{ ok, enabled, bindAddress, port, allowWrites, requireConfirm, exportRoot, logLevel, componentAllowlist, reflectionAllowlistMembers, enableConsoleEval, hookAllowlistSignatures }` (sanitized).
  - Note: despite the name, `hookAllowlistSignatures` currently contains **type full names** allowed for `HookAdd` (example: `["UnityEngine.GameObject"]`).
- Object state: `SetActive(objectId, active, confirm?)` → `{ ok }`; `SelectObject(objectId)` → `{ ok }` and triggers a `selection` notification (still gated by `allowWrites`).
- Clipboard: `SetClipboardText(text, confirm?)` / `SetClipboardObject(objectId, confirm?)` / `ClearClipboard(confirm?)` → `{ ok }` (guarded by `allowWrites`; confirmation enforced when `requireConfirm=true`; `SetClipboardObject` expects `obj:<instanceId>`).
- Reflection writes: `SetMember(objectId, componentType, member, jsonValue, confirm?)` → `{ ok }` (enforces `reflectionAllowlistMembers` entries `<Type>.<Member>`). `CallMethod(objectId, componentType, method, argsJson="[]", confirm?)` → `{ ok, result }` is available on both IL2CPP and Mono; allowlist enforced via `reflectionAllowlistMembers`.
- Component writes: `AddComponent(objectId, type, confirm?)` / `RemoveComponent(objectId, typeOrIndex, confirm?)` → `{ ok }`; `componentAllowlist` must include the type when removing by type or adding.
- Hooks: `HookAdd(type, methodOrSignature, confirm?)` / `HookRemove(signature, confirm?)` / `HookSetEnabled(signature, enabled, confirm?)` / `HookSetSource(signature, source, confirm?)` → `{ ok }`; allowlist via `hookAllowlistSignatures` (type full names). `HookSetSource` requires `enableConsoleEval=true`. `HookAdd` accepts either a method name (may be ambiguous) or a full `MethodInfo.FullDescription()` signature string for deterministic overload selection.
- Hierarchy: `Reparent(objectId, newParentId, confirm?)` and `DestroyObject(objectId, confirm?)` → `{ ok }`; Mono host restricts these to the `SpawnTestUi` hierarchy, CoreCLR currently allows any object.
- Console: `ConsoleEval(code, confirm?)` → `{ ok, result }` (requires `enableConsoleEval=true` in config).
- Console scripts: `WriteConsoleScript(path, content, confirm?)` / `DeleteConsoleScript(path, confirm?)` → `{ ok }` (paths validated to Scripts folder; max 256KB; BOM normalized).
- Scenes: `LoadScene(name, mode="single"|"additive", confirm?)` → `{ ok, name, mode }` (guarded by `allowWrites`; when `requireConfirm=true` resend with `confirm=true`; mode applies `SceneManager.LoadScene`).
- Time: `SetTimeScale(value, lock?, confirm?)` → `{ ok, value, locked }` (value clamped 0–4; `lock=true` uses the Explorer widget lock when available).
- Freecam: `SetFreecamEnabled(enabled, confirm?)` → `{ ok }`; `SetFreecamSpeed(speed, confirm?)` → `{ ok, speed }` (speed clamped to a safe range); `SetFreecamPose(pos, rot, confirm?)` → `{ ok }` (updates live camera when active and cached pose otherwise). All guard with `allowWrites` + `requireConfirm`.
- Test UI helpers: `SpawnTestUi(confirm?)` → `{ ok, rootId, blocks: [{ name, id }] }` (id strings are `obj:<instanceId>`); `DestroyTestUi(confirm?)` → `{ ok }`.
- Tool errors: `{ ok: false, error: { kind, message, hint? } }` where `kind` mirrors the JSON-RPC error kinds below.

### Clipboard

- DTO: `ClipboardDto { HasValue: bool, Type: string?, Preview: string?, ObjectId?: string }`.
- Preview uses `ToStringWithType` output capped at 256 characters to avoid large payloads; no object graphs are serialized.
- `ObjectId` is populated when the clipboard holds a live `UnityEngine.Object` and uses `obj:<instanceId>`; otherwise it is null.
- Resource: `unity://clipboard` (same payload as `GetClipboard`).
- Tools: `GetClipboard()` (read-only) and guarded writes `SetClipboardText(text, confirm?)`, `SetClipboardObject(objectId, confirm?)`, `ClearClipboard(confirm?)` (require `allowWrites=true`; `requireConfirm=true` enforces `confirm=true`).

### Freecam

- DTO: `FreecamDto { Enabled: bool, UsingGameCamera: bool, Speed: float, Pos: Vector3Dto, Rot: Vector3Dto }` (`Rot` is Euler degrees).
- Resource: `unity://freecam` (prefers the live freecam camera when active, otherwise the cached pose or `Camera.main`).
- Read tool: `GetFreecam()` → `FreecamDto` (same payload as the resource).
- Guarded tools (respect `allowWrites` + `requireConfirm`): `SetFreecamEnabled(enabled, confirm?)` → `{ ok }`; `SetFreecamSpeed(speed, confirm?)` → `{ ok, speed }` (speed clamped to a safe range); `SetFreecamPose(pos, rot, confirm?)` → `{ ok }` (updates active freecam transform and cached pose so it persists between toggles).

## Console scripts + Hooks parity (current)

These surfaces are implemented in part (console scripts) and implemented (hooks advanced). Remaining planned items are explicitly marked as **Planned** below and tracked in `plans/unity-explorer-mcp-todo.md`.

### Console scripts (file-level parity)

Goal: expose UnityExplorer’s `Scripts/` folder (under `ExplorerCore.ExplorerFolder`) so an agent can list, read, write, and run scripts safely.

- Resources
  - `unity://console/scripts` — `Page<ConsoleScriptDto> { Total, Items: [{ Name, Path }] }` (**Implemented**).
  - `unity://console/script?path=<relativeOrAbsolute>` — `ConsoleScriptFileDto { Name, Path, Content, SizeBytes, LastModifiedUtc, Truncated }` (**Implemented**).
    - `path` is validated to stay inside the Scripts folder; `.cs` only.
    - `Content` is truncated at a fixed max size (256 KB) to avoid multi‑MB transfers.
    - BOM: reads strip a leading UTF‑8 BOM; writes emit UTF‑8 without BOM.

- Read-only tools
  - `ReadConsoleScript(path)` → `ConsoleScriptFileDto` (**Implemented**; same as the resource).
  - `GetStartupScript()` → `{ ok, enabled, path, content? }` (**Implemented**; when `startup.cs` is missing but `startup.disabled.cs` exists, it returns `enabled=false` with the disabled file content so agents can restore it).

- Guarded tools
  - `WriteConsoleScript(path, content, confirm?)` → `{ ok }` (**Implemented**; requires `allowWrites=true`; requires `confirm=true` when `requireConfirm=true`).
  - `DeleteConsoleScript(path, confirm?)` → `{ ok }` (**Implemented**; same gating as above).
  - `RunConsoleScript(path, confirm?)` → `{ ok, result }` (**Implemented**; requires `enableConsoleEval=true` in addition to write gating; max 256KB content, BOM stripped).
  - Startup script control (maps to the built-in `startup.cs` behavior) (**Implemented**):
    - `SetStartupScriptEnabled(enabled, confirm?)` → `{ ok }` (renames `startup.cs` ↔ `startup.disabled.cs`).
    - `WriteStartupScript(content, confirm?)` → `{ ok }` (`enableConsoleEval` not required for writing; overwrites `startup.cs` and removes the disabled copy if present).
    - `RunStartupScript(confirm?)` → `{ ok, result, path }` (requires `enableConsoleEval=true`; runs `startup.cs` when present, otherwise `startup.disabled.cs`; max 256KB, BOM stripped).

Errors: `InvalidArgument` for invalid paths (outside Scripts folder), `NotFound` for missing files, `PermissionDenied` for gating.

### Hooks (advanced parity)

Goal: let an agent discover safe hook targets (within allowlist), toggle hooks, and (optionally) edit/apply patch source.

- Read-only tools
  - `HookListAllowedTypes()` → `{ ok, items: string[] }` (mirrors current `hookAllowlistSignatures` config values).
  - `HookListMethods(type, filter?, limit?, offset?)` → `Page<HookMethodDto> { Total, Items: [{ Name, Signature }] }`.
  - `HookGetSource(signature)` → `{ ok, signature, source }`.

- Guarded tools
  - `HookSetEnabled(signature, enabled, confirm?)` → `{ ok }`.
  - `HookSetSource(signature, source, confirm?)` → `{ ok }` (requires `enableConsoleEval=true` because it compiles patch code).

- Back-compat note for `HookAdd(type, method)`
  - `method` may be either a method name or a full `MethodInfo.FullDescription()` signature string.
  - Prefer the full signature for overloads; name-based lookup can be ambiguous and should return `InvalidArgument` with a hint to pass the signature.

## Streams & Notifications

- `stream_events` emits chunked JSON notifications until the client disconnects; on open it emits deterministic snapshots for `scenes` and `selection`.
- `GET /` with `Accept: text/event-stream` delivers the same JSON-RPC payloads as SSE frames (`data: <json>\n\n`) for clients that prefer the inspector-style receive channel.
  - `log`: `{ level, message, source, category?, t }` (mirrors `logs/tail`).
  - `selection`: `SelectionDto { ActiveId, Items[] }` (same as `unity://selection`).
  - `scenes`: `Page<SceneDto>` (same as `unity://scenes`), emitted once per `stream_events` connection on open.
  - `scenes_diff`: `{ added: [sceneId], removed: [sceneId] }` when scenes load/unload.
  - `tool_result`: `{ name, ok: true, result }` or `{ name, ok: false, error: { code, message, data } }` reflecting `call_tool` outcomes.
- Minimal agent-first event set (stable): `scenes`, `selection`, `log`, `tool_result`.
- Backpressure: IL2CPP + Mono stream writes use per-stream bounded send queues (limit 256) dropping oldest pending items when full and logging a one-time warning.
- JSON-RPC results and errors for other requests are also mirrored onto the stream for connected clients.

## Error Envelope & Rate Limits

- JSON-RPC errors: `{ error: { code, message, data: { kind, hint?, detail? } } }` with `code` from the JSON-RPC spec or domain codes:
  - `NotReady` (-32002, HTTP 503)
  - `PermissionDenied` (-32003, HTTP 403)
  - `NotFound` (-32004, HTTP 404)
  - `RateLimited` (-32005, HTTP 429, message `"Cannot have more than 32 parallel requests. Please slow down."`)
  - `InvalidArgument` (-32602, HTTP 400)
  - `Internal` (-32603, HTTP 500)
- Tool-level failures reuse the same `kind/hint` fields inside `{ ok: false, error: { kind, message, hint? } }`.
- Errors log `[MCP] error <code>: <message>` into the MCP log buffer, visible via `unity://logs/tail` or `TailLogs`.

## Example Payloads (IL2CPP host 192.168.178.210:51477)

- `unity://status`
```json
{
  "Version": "0.1.0",
  "UnityVersion": "2021.3.45f1",
  "Platform": "WindowsPlayer",
  "Runtime": "IL2CPP",
  "ExplorerVersion": "4.12.8",
  "Ready": true,
  "ScenesLoaded": 1,
  "Selection": []
}
```

- `unity://scene/scn:0/objects?limit=2`
```json
{
  "Total": 2,
  "Items": [
    { "Id": "obj:2134", "Name": "EventSystem", "Path": "/EventSystem", "Tag": "Untagged", "Layer": 0, "Active": true, "ComponentCount": 4 },
    { "Id": "obj:2136", "Name": "Lighting", "Path": "/Lighting", "Tag": "Untagged", "Layer": 0, "Active": true, "ComponentCount": 1 }
  ]
}
```

- `MousePick(mode="world")` when no world hit is under the cursor
```json
{ "Mode": "world", "Hit": false, "Id": null, "Items": null }
```

- `MousePick(mode="ui")` after `SpawnTestUi` (shape)
```json
{ "Mode": "ui", "Hit": true, "Id": "obj:<topHit>", "Items": [{ "Id": "obj:<topHit>", "Name": "McpTestBlock_Left", "Path": "/McpTestCanvas/McpTestBlock_Left" }, { "Id": "obj:<other>", "Name": "McpTestBlock_Right", "Path": "/McpTestCanvas/McpTestBlock_Right" }] }
```

- `unity://console/script?path=mcp-test.cs` (shape)
```json
{ "Name": "mcp-test.cs", "Path": "<ScriptsFolder>/mcp-test.cs", "Content": "return 123;", "SizeBytes": 11, "LastModifiedUtc": "2025-12-17T18:00:00Z", "Truncated": false }
```

- `WriteConsoleScript` (JSON-RPC call_tool shape)
```json
{"jsonrpc":"2.0","id":"write-script","method":"call_tool","params":{"name":"WriteConsoleScript","arguments":{"path":"mcp-test.cs","content":"return 123;","confirm":true}}}
```

Use these examples to keep DTOs, tests, and docs in sync. Any shape change must be reflected here, in DTOs, in contract tests, and in `README-mcp.md`.
