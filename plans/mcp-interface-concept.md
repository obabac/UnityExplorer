# Unity Explorer MCP Interface — Concept Draft

- Date: 2025-11-09
- Mode: In‑process server (C# SDK), HTTP transport via `ModelContextProtocol.AspNetCore`
- Client transport: `HttpClientTransport` with `HttpTransportMode.AutoDetect | StreamableHttp | Sse`
- Default policy: Read‑only. Writes gated behind config + confirmation + allowlist.

---

## Primitives Overview

- Tools: Actionable RPCs (read‑only first; writes later, gated)
- Resources: URI-addressable, read-only data surfaces (paginated)
- Streams: Server→client notifications for logs/selection/scene events
- Prompts: Optional canned prompts (quality-of-life for LLM UIs)

---

## Namespacing & Conventions

- Resource scheme: `unity://...`
- IDs are opaque strings stable across a session; include `instanceId` and scene context.
- Pagination: `limit`, `offset`; return `{ total, items: [...] }`.
- Large values truncated with `preview` fields and `hasMore` flags.

---

## Resources (Read‑Only)

- `unity://status`
  - Shape: `{ version, unityVersion, platform, runtime: "IL2CPP|Mono", explorerVersion, ready, scenesLoaded, selection: string[] }`
- `unity://scenes`
  - Shape: `{ total, items: [{ id, name, index, isLoaded, rootCount }] }`
- `unity://scene/{sceneId}/objects?limit&offset&depth`
  - Shape: `{ total, items: [{ id, name, path, tag, layer, active, componentCount }] }`
- `unity://object/{id}`
  - Shape: `{ id, name, path, active, tag, layer, transform: { pos, rot, scale }, components: [{ type, idHint }] }`
- `unity://object/{id}/components`
  - Shape: `{ total, items: [{ type, summary, values?: { key: valuePreview } }] }`
- `unity://search?name=&type=&path=&activeOnly=`
  - Shape: `{ total, items: [{ id, name, type, path }] }`
- `unity://selection`
  - Shape: `{ total, items: [objectId...] }`
- `unity://console/scripts`
  - Shape: `{ total, items: [{ name, path }] }`
- `unity://hooks`
  - Shape: `{ total, items: [{ id, target, method, kind, enabled }] }`
- `unity://camera/active`
  - Shape: `{ camera: { name, fov, pos, rot, isFreecam }, freecam: { enabled } }`
- `unity://logs/tail?count=200`
  - Shape: `{ items: [{ t, level, source, message }] }`  
    - `level`: e.g. `info|warn|error|exception`  
    - `source`: e.g. `UnityExplorer` (internal), `UnityEngine` (forwarded logs), `MCP` (server‑side messages)

---

## Tools (Phase 1: Read‑Only)

```csharp
[McpServerToolType]
public static class UnityReadTools
{
    [McpServerTool, Description("Status snapshot of Unity Explorer.")]
    public static Task<StatusDto> GetStatus(CancellationToken ct);

    [McpServerTool, Description("List scenes.")]
    public static Task<Page<SceneDto>> ListScenes(int? limit, int? offset, CancellationToken ct);

    [McpServerTool, Description("List objects in a scene or all scenes.")]
    public static Task<Page<ObjectCardDto>> ListObjects(
        string? sceneId, string? name, string? type, bool? activeOnly,
        int? limit, int? offset, CancellationToken ct);

    [McpServerTool, Description("Get object details by id.")]
    public static Task<ObjectDetailDto> GetObject(string id, CancellationToken ct);

    [McpServerTool, Description("Get components for object.")]
    public static Task<Page<ComponentCardDto>> GetComponents(string objectId, int? limit, int? offset, CancellationToken ct);

    [McpServerTool, Description("Search objects by name/type/path.")]
    public static Task<Page<SearchResultDto>> SearchObjects(
        string? query, string? name, string? type, string? path, bool? activeOnly,
        int? limit, int? offset, CancellationToken ct);

    [McpServerTool, Description("Pick object(s) under mouse (world/ui). For `mode=world` returns a single top‑most hit; for `mode=ui` may return multiple hits.")]
    public static Task<PickResultDto> MousePick(string mode = "world", CancellationToken ct);

    [McpServerTool, Description("Get active camera info.")]
    public static Task<CameraInfoDto> GetCameraInfo(CancellationToken ct);

    [McpServerTool, Description("Tail recent logs.")]
    public static Task<LogTailDto> TailLogs(int count = 200, CancellationToken ct);
}
```

Phase‑later write tools exist but are disabled by default; they require allowlist + confirmations.

---

## Resources via Attributes

```csharp
[McpServerResourceType]
public static class UnityResources
{
    [McpServerResource("unity://status")]
    public static Task<StatusDto> Status(CancellationToken ct) => UnityReadTools.GetStatus(ct);

    [McpServerResource("unity://scenes")]
    public static Task<Page<SceneDto>> Scenes(int? limit, int? offset, CancellationToken ct)
        => UnityReadTools.ListScenes(limit, offset, ct);

    [McpServerResource("unity://object/{id}")]
    public static Task<ObjectDetailDto> ObjectById(string id, CancellationToken ct)
        => UnityReadTools.GetObject(id, ct);
}
```

---

## Streaming & Progress

- Streams
  - `logs/stream`: `{ level, message, t }`
  - `selection/stream`: `{ added: [id], removed: [id], current: [id] }`
  - `scene/stream`: `{ event: "loaded|unloaded", scene: { id, name } }`

- Progress Notifications
  - Long‑running tools accept progress tokens and report incremental updates per SDK guidance.

---

## Error Semantics

All JSON‑RPC errors use the standard envelope:

- `error.code` — numeric code (JSON‑RPC built‑in codes for parse/transport errors, `-32603` or `-32000` range for server errors).
- `error.message` — short human‑readable message.
- `error.data` — optional object with at least:
  - `kind`: one of `NotReady`, `NotFound`, `InvalidArgument`, `PermissionDenied`, `RateLimited`, `Internal`.
  - `hint` (optional): short guidance such as `"resend with confirm=true"`.

Semantic meanings:

- `NotReady`  
  - Explorer not fully initialized yet (e.g. scenes not populated).  
  - Typically mapped to HTTP 503 with `error.data.kind = "NotReady"`.
- `NotFound`  
  - Missing object/scene/component or unknown URI/tool.  
  - Typically mapped to HTTP 404.
- `InvalidArgument`  
  - Parameter/type coercion failure, invalid IDs, bad enum values, etc.  
  - Typically mapped to HTTP 400.
- `PermissionDenied`  
  - Write tool invoked while writes are disabled, config disallows the action, or confirmation was not provided.  
  - Often paired with `hint = "resend with confirm=true"`.
- `RateLimited`  
  - Too many concurrent requests.  
  - Message should be: `"Cannot have more than X parallel requests. Please slow down."` with `error.data.kind = "RateLimited"`.
- `Internal`  
  - Unexpected server error; internal details may be logged but `error.data` stays minimal (e.g. `{ kind: "Internal" }`).

Tool‑level failures that still return a JSON‑RPC `result` use a consistent pattern:

- `result = { ok: false, error: { code, message, kind, hint? } }`
- The `kind` and optional `hint` mirror the JSON‑RPC `error.data` fields above.

---

## Transport & Hosting

- Server: `AddMcpServer().WithHttpTransport().WithTools<UnityReadTools>().WithResources<UnityResources>();` + `app.MapMcp()`.
- Bind: `127.0.0.1:0` (ephemeral). Publish discovery file `%TEMP%/unity-explorer-mcp.json` with `{ pid, baseUrl, port, modes }`.
- Client: `new HttpClientTransport(new() { Endpoint = baseUrl, Mode = AutoDetect })`.

---

## Threading & Safety

- All Unity calls marshal via `MainThread.Run(...)` to avoid non‑main‑thread access.
- Read‑only by default; write tools require `allowWrites=true` and per‑call confirmation prompt.
- Reflection writes limited by allowlist; exports restricted to configured root.

---

## Example Payloads

- `get_status()`
```json
{
  "version":"0.1.0",
  "unityVersion":"2021.3.34f1",
  "platform":"Windows",
  "runtime":"IL2CPP",
  "explorerVersion":"4.12.8",
  "ready":true,
  "scenesLoaded":2,
  "selection":["scn:Main:obj:12345"]
}
```

- `list_objects(sceneId, limit=2)`
```json
{
  "total": 1287,
  "items": [
    {"id":"scn:Main:obj:12345","name":"Player","path":"/Main/Player","tag":"Player","layer":0,"active":true,"componentCount":7},
    {"id":"scn:Main:obj:67890","name":"Camera","path":"/Main/Camera","tag":"MainCamera","layer":0,"active":true,"componentCount":3}
  ]
}
```

---

## DTO Sketch

```csharp
public record Page<T>(int Total, IReadOnlyList<T> Items);
public record StatusDto(string Version, string UnityVersion, string Platform, string Runtime,
    string ExplorerVersion, bool Ready, int ScenesLoaded, IReadOnlyList<string> Selection);
public record SceneDto(string Id, string Name, int Index, bool IsLoaded, int RootCount);
public record ObjectCardDto(string Id, string Name, string Path, string Tag, int Layer, bool Active, int ComponentCount);
public record ObjectDetailDto(string Id, string Name, string Path, bool Active, string Tag, int Layer,
    TransformDto Transform, IReadOnlyList<ComponentCardDto> Components);
public record ComponentCardDto(string Type, string? Summary);
public record TransformDto(Vector3Dto Pos, Vector3Dto Rot, Vector3Dto Scale);
public record Vector3Dto(float X, float Y, float Z);
public record SearchResultDto(string Id, string Name, string Type, string Path);
public record PickHit(string Id, string Name, string Path);
public record PickResultDto(string Mode, bool Hit, string? Id, IReadOnlyList<PickHit>? Items);
public record CameraInfoDto(bool IsFreecam, string Name, float Fov, Vector3Dto Pos, Vector3Dto Rot);
public record LogTailDto(IReadOnlyList<LogLine> Items);
public record LogLine(DateTimeOffset T, string Level, string Source, string Message);
```

---

Status: Concept draft v0.2 (updated for logs metadata, mouse‑inspect multi‑hit design, error envelope, and DTO sketch for time/log tooling)

