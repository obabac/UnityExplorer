# Unity Explorer MCP (In‑Process) — Preview

This build hosts a Model Context Protocol (MCP) server inside the Unity Explorer DLL for CoreCLR (IL2CPP Interop) targets. The server exposes read‑only tools and resources over HTTP using the official C# SDK.

## Status

- Targets: CoreCLR builds only (`BIE_*_Cpp_CoreCLR`, `ML_Cpp_CoreCLR`, `STANDALONE_Cpp_CoreCLR`).
- Transport: lightweight HTTP + SSE over a local TCP listener (no ASP.NET Core dependency).
- Default mode: Read‑only (guarded writes must be explicitly enabled).

## Getting Started

1. Launch a Unity title with Unity Explorer (CoreCLR target).
2. The MCP server starts after Explorer initialization.
3. Discovery file is written to `%TEMP%/unity-explorer-mcp.json` with `{ pid, baseUrl, port, modeHints, startedAt }`.
4. Connect a client via the MCP C# SDK using `HttpClientTransport` (AutoDetect mode), or talk directly to the HTTP/SSE endpoints described below.

### Tiny CLI (local)

Build once:

```
dotnet build tools/mcpcli/McpCli.csproj -c Debug
```

While a Unity title with Explorer is running (CoreCLR target), use:

```
dotnet run --project tools/mcpcli -- status
dotnet run --project tools/mcpcli -- scenes
dotnet run --project tools/mcpcli -- objects scn:0
dotnet run --project tools/mcpcli -- search Player
dotnet run --project tools/mcpcli -- camera
dotnet run --project tools/mcpcli -- selection
dotnet run --project tools/mcpcli -- logs 100
dotnet run --project tools/mcpcli -- list-tools
dotnet run --project tools/mcpcli -- call GetStatus

# guarded write (requires sinai-dev-UnityExplorer/mcp.config.json: { "allowWrites": true })
dotnet run --project tools/mcpcli -- set-active obj:12345 true --confirm
```

## Configuration

The config file is created at `{ExplorerFolder}/mcp.config.json` (Explorer folder is typically `sinai-dev-UnityExplorer/`). Example:

```json
{
  "enabled": true,
  "bindAddress": "127.0.0.1",
  "port": 0,
  "transportPreference": "auto",
  "allowWrites": false,
  "requireConfirm": true,
  "exportRoot": null,
  "logLevel": "Information"
}
```

- `port: 0` uses an ephemeral port.

## HTTP / SSE Surface

The in‑process server exposes a minimal HTTP protocol over a loopback TCP listener:

- `GET /sse`  
  - Opens a Server‑Sent Events (SSE) stream.  
  - Events include logs, selection changes, scene updates, and tool results.
- `POST /message` (JSON body)  
  - JSON‑RPC‑style envelope with:
    - `{"kind":"list_tools"}`
    - `{"kind":"call_tool","name":"ListScenes","args":{...}}`
    - `{"kind":"read_resource","uri":"unity://status"}`
  - Returns `202 Accepted` immediately; the actual result is emitted to `/sse` as a `tool_result` event.
- `GET /read?uri=unity://...`  
  - Convenience endpoint for simple read operations.  
  - Returns JSON directly in the HTTP response.

### Auth

If `authToken` is set in `mcp.config.json`, all HTTP endpoints require:

- HTTP header: `Authorization: Bearer <token>`

If `authToken` is empty or missing, no auth is enforced (local use only is recommended).

## Resources

Resources are addressed using `unity://` URIs:

- `unity://status` — global status (Unity version, scenes, selection summary, etc.).
- `unity://scenes` — list of loaded scenes.
- `unity://scene/{sceneId}/objects` — objects in a scene (paged).
- `unity://object/{id}` — a single object plus key fields.
- `unity://object/{id}/components` — components on an object (paged).
- `unity://search?...` — search across objects (by name, type, path, activeOnly, etc.).
- `unity://camera/active` — active camera and basic info.
- `unity://selection` — current Unity selection.
- `unity://logs/tail` — recent log lines.

IDs:

- Scene IDs: `scn:<index>`
- Object IDs: `obj:<instanceId>`

Pagination:

- Many list endpoints accept `limit` and `offset` query parameters (integers).

## Tools

Read‑only tools (always available when the server is enabled):

- `GetStatus`
- `ListScenes`
- `ListObjects`
- `GetObject`
- `GetComponents`
- `SearchObjects`
- `MousePick`
- `GetCameraInfo`
- `GetSelection`
- `TailLogs`

Guarded write tools (require `allowWrites: true`; many also require confirm):

- Object state:
  - `SetActive(objId, bool)`
  - `SelectObject(objId)`
  - `SetName(objId, string)`
  - `SetTag(objId, string)`
  - `SetLayer(objId, int)`
- Hierarchy / components:
  - `Reparent(childId, parentId)`
  - `DestroyObject(objId)`
  - `AddComponent(objId, FullTypeName)` — requires component allowlist.
  - `RemoveComponent(objId, typeOrIndex)` — requires allowlist or index.
- Reflection:
  - `SetMember(objId, CompType, Member, jsonValue)` — guarded by reflection allowlist (`Type.Member`).
  - `CallMethod(objId, CompType, Method, jsonArrayArgs?)` — also allowlisted.
- Config:
  - `SetConfig(allowWrites?, requireConfirm?, authToken?, restart?)`

## SSE Events

`/sse` emits JSON events with a simple envelope:

- Event name in `event:` line (e.g., `event: log`).
- JSON payload in `data:` line.

Event types:

- `log` — `{ level, message, t }`
- `selection` — `{ activeId, items[] }`
- `scenes` — `{ loaded[], count }`
- `scenes_diff` — `{ added[], removed[] }`
- `inspected_scene` — `{ name, handle, isLoaded }`
- `tool_result` — `{ name, result }`

## Allowlists

Two allowlists are configured in `mcp.config.json` and editable via the Options panel:

- Component allowlist: `componentAllowlist`  
  - Array of `FullTypeName` strings.  
  - `AddComponent` and `RemoveComponent` are restricted to these types (or indices).
- Reflection allowlist: `reflectionAllowlistMembers`  
  - Array of `"Type.Member"` strings, e.g., `"UnityEngine.Light.intensity"`.  
  - `SetMember` / `CallMethod` require entries here.

Empty allowlists disable the corresponding write features (for safety).

## Notes

- Non‑CoreCLR targets (Mono, Unhollower) do not host the MCP server.
- All Unity API calls are marshalled to the main thread.
