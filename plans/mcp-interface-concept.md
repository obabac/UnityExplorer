# Unity Explorer MCP Interface — Source of Truth

- Updated: 2025-12-17
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
- `unity://scenes` — `Page<SceneDto> { Total, Items: [{ Id, Name, Index, IsLoaded, RootCount }] }`.
- `unity://scene/{sceneId}/objects?limit&offset` — `Page<ObjectCardDto> { Total, Items: [{ Id, Name, Path, Tag, Layer, Active, ComponentCount }] }`.
- `unity://object/{id}` — `ObjectCardDto` (same shape as list items; no transform payload today).
- `unity://object/{id}/components?limit&offset` — `Page<ComponentCardDto> { Total, Items: [{ Type, Summary }] }`.
- `unity://search?query=&name=&type=&path=&activeOnly=&limit=&offset=` — `Page<ObjectCardDto>` using the same card shape as `ListObjects`.
- `unity://selection` — `SelectionDto { ActiveId, Items[] }`; emits a `selection` stream event when selection changes (same payload as the resource).
- `unity://camera/active` — `CameraInfoDto { IsFreecam, Name, Fov, Pos{X,Y,Z}, Rot{X,Y,Z} }`; falls back to `Camera.main`/first camera or `<none>` when missing.
- `unity://logs/tail?count=` — `LogTailDto { Items: [{ T, Level, Message, Source, Category? }] }`; `[MCP] error ...` lines are written here when requests fail.
- `unity://console/scripts` — `Page<ConsoleScriptDto> { Total, Items: [{ Name, Path }] }`.
- `unity://hooks` — `Page<HookDto> { Total, Items: [{ Signature, Enabled }] }` (Harmony signatures such as `System.Void UnityEngine.GameObject::SetActive(System.Boolean)`).

## Tools

### Read tools (no allowWrites required)
- `GetStatus()` → `StatusDto` (same as `unity://status`).
- `ListScenes(limit?, offset?)` → `Page<SceneDto>`.
- `ListObjects(sceneId?, name?, type?, activeOnly?, limit?, offset?)` → `Page<ObjectCardDto>`.
- `GetObject(id)` → `ObjectCardDto` by `obj:<instanceId>`.
- `GetComponents(objectId, limit?, offset?)` → `Page<ComponentCardDto>`.
- `GetVersion()` → `VersionInfoDto { ExplorerVersion, McpVersion, UnityVersion, Runtime }`.
- `SearchObjects(query?, name?, type?, path?, activeOnly?, limit?, offset?)` → `Page<ObjectCardDto>`.
- `GetCameraInfo()` → `CameraInfoDto`.
- `MousePick(mode="world"|"ui", x?, y?, normalized=false)` → `PickResultDto { Mode, Hit, Id?, Items? }`; world mode omits `Items` (null); UI mode uses EventSystem ordering (top-most first) and `Id` mirrors the first resolvable hit (hits are filtered to GameObjects that `GetObject` can resolve; if none, Items is empty/Id null) on both IL2CPP and Mono.
- `TailLogs(count=200)` → `LogTailDto`.
- `GetSelection()` → `SelectionDto { ActiveId, Items[] }`.
- `GetTimeScale()` → `{ ok: true, value: float, locked: bool }`.

### Guarded writes (require `allowWrites=true`; `requireConfirm=true` forces `confirm=true` where supported)
- Config: `SetConfig(allowWrites?, requireConfirm?, enableConsoleEval?, componentAllowlist?, reflectionAllowlistMembers?, hookAllowlistSignatures?, restart=false)` → `{ ok }`; `GetConfig()` → `{ ok, enabled, bindAddress, port, allowWrites, requireConfirm, exportRoot, logLevel, componentAllowlist, reflectionAllowlistMembers, enableConsoleEval, hookAllowlistSignatures }` (sanitized).
- Object state: `SetActive(objectId, active, confirm?)` → `{ ok }`; `SelectObject(objectId)` → `{ ok }` and triggers a `selection` notification (still gated by `allowWrites`).
- Reflection writes: `SetMember(objectId, componentType, member, jsonValue, confirm?)` → `{ ok }`; `CallMethod(objectId, componentType, method, argsJson="[]", confirm?)` → `{ ok, result }`. Both enforce `reflectionAllowlistMembers` entries (`<Type>.<Member>`).
- Component writes: `AddComponent(objectId, type, confirm?)` / `RemoveComponent(objectId, typeOrIndex, confirm?)` → `{ ok }`; `componentAllowlist` must include the type when removing by type or adding.
- Hooks: `HookAdd(type, method, confirm?)` / `HookRemove(signature, confirm?)` → `{ ok }`; allowlist via `hookAllowlistSignatures`.
- Hierarchy: `Reparent(objectId, newParentId, confirm?)` and `DestroyObject(objectId, confirm?)` → `{ ok }`; Mono host restricts these to the `SpawnTestUi` hierarchy, CoreCLR currently allows any object.
- Console: `ConsoleEval(code, confirm?)` → `{ ok, result }` (requires `enableConsoleEval=true` in config).
- Time: `SetTimeScale(value, lock?, confirm?)` → `{ ok, value, locked }` (value clamped 0–4; `lock=true` uses the Explorer widget lock when available).
- Test UI helpers: `SpawnTestUi(confirm?)` → `{ ok, rootId, blocks: [{ name, id }] }` (id strings are `obj:<instanceId>`); `DestroyTestUi(confirm?)` → `{ ok }`.
- Tool errors: `{ ok: false, error: { kind, message, hint? } }` where `kind` mirrors the JSON-RPC error kinds below.

## Streams & Notifications

- `stream_events` emits chunked JSON notifications until the client disconnects; on open it emits a deterministic `scenes` snapshot notification.
- `GET /` with `Accept: text/event-stream` delivers the same JSON-RPC payloads as SSE frames (`data: <json>\n\n`) for clients that prefer the inspector-style receive channel.
  - `log`: `{ level, message, source, category?, t }` (mirrors `logs/tail`).
  - `selection`: `SelectionDto { ActiveId, Items[] }` (same as `unity://selection`).
  - `scenes`: `Page<SceneDto>` (same as `unity://scenes`), emitted once per `stream_events` connection on open.
  - `scenes_diff`: `{ added: [sceneId], removed: [sceneId] }` when scenes load/unload.
  - `tool_result`: `{ name, ok: true, result }` or `{ name, ok: false, error: { code, message, data } }` reflecting `call_tool` outcomes.
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

Use these examples to keep DTOs, tests, and docs in sync. Any shape change must be reflected here, in DTOs, in contract tests, and in `README-mcp.md`.
