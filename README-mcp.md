# Unity Explorer MCP (In‑Process) — Preview

This build hosts a Model Context Protocol (MCP) server inside the Unity Explorer DLL for CoreCLR (IL2CPP Interop) targets. The server exposes read‑only tools and resources over HTTP using the official C# SDK.

## Status

- Targets: CoreCLR builds only (`BIE_*_Cpp_CoreCLR`, `ML_Cpp_CoreCLR`, `STANDALONE_Cpp_CoreCLR`).
- Transport: lightweight streamable HTTP over a local TCP listener.
- Default mode: Read‑only (guarded writes must be explicitly enabled).

## Getting Started

1. Launch a Unity title with Unity Explorer (CoreCLR target).
2. The MCP server starts after Explorer initialization.
3. Discovery file is written to `%TEMP%/unity-explorer-mcp.json` with `{ pid, baseUrl, port, modeHints, startedAt }`.
4. Connect a client via the MCP C# SDK using `HttpClientTransport` (AutoDetect mode), or talk directly to the HTTP endpoints described below.

## Configuration

The config file is created at `{ExplorerFolder}/mcp.config.json` (Explorer folder is typically `sinai-dev-UnityExplorer/`). Example:

```json
{
  "enabled": true,
  "bindAddress": "0.0.0.0",
  "port": 51477,
  "transportPreference": "auto",
  "allowWrites": false,
  "requireConfirm": true,
  "enableConsoleEval": false,
  "componentAllowlist": null,
  "reflectionAllowlistMembers": null,
  "hookAllowlistSignatures": null,
  "exportRoot": null,
  "logLevel": "Information"
}
```

- `port: 0` uses an ephemeral port.

## HTTP Surface

The in‑process server exposes a minimal HTTP protocol over a loopback TCP listener:

- `POST /message` (JSON body)  
  - JSON‑RPC‑style envelope:
    - `{"jsonrpc":"2.0","id":1,"method":"list_tools","params":{}}`
    - `{"jsonrpc":"2.0","id":2,"method":"call_tool","params":{"name":"ListScenes","arguments":{...}}}`
    - `{"jsonrpc":"2.0","id":3,"method":"read_resource","params":{"uri":"unity://status"}}`
    - `{"jsonrpc":"2.0","id":4,"method":"stream_events","params":{}}`
  - Non‑streaming methods (`list_tools`, `call_tool`, `read_resource`) return a JSON‑RPC response in the HTTP body:
    - Success: `{"jsonrpc":"2.0","id":1,"result":{...}}`
    - Error: `{"jsonrpc":"2.0","id":1,"error":{"code":-32602,"message":"Invalid params",...}}`
  - `stream_events` keeps the HTTP response open and streams JSON‑RPC 2.0 payloads over a chunked HTTP body (one JSON object per line). The stream may contain:
    - Notifications: `{"jsonrpc":"2.0","method":"notification","params":{"event":"log|selection|scenes|scenes_diff|inspected_scene|tool_result|...","payload":{...}}}`
    - Standard results/errors echoed to subscribers for other requests: `{"jsonrpc":"2.0","id":...,"result":{...}}` and `{"jsonrpc":"2.0","id":...,"error":{"code":...,"message":"...", "data":{...}}}`
  - Tool calls are also echoed as streamed `tool_result` notifications on `stream_events`.
- `GET /read?uri=unity://...`  
  - Convenience endpoint for simple read operations.  
  - Returns JSON directly in the HTTP response.

### MCP Handshake

When connecting with a generic MCP client (including `@modelcontextprotocol/inspector`), the typical sequence is:

1. `initialize`  
   - Returns `protocolVersion`, `capabilities` (`tools`, `resources`, `experimental.streamEvents`), `serverInfo`, and a short `instructions` string.
2. `notifications/initialized`  
   - Optional client notification; Unity Explorer accepts this method and replies with `{ "ok": true }`.
3. `list_tools`, `read_resource`, `call_tool`, and `stream_events`  
   - Use `list_tools` to discover tools, `call_tool` for RPCs, `read_resource` for `unity://...` URIs, and `stream_events` for log/scene/selection/tool_result notifications.

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
- `unity://logs/tail` — recent log lines (`{ t, level, message, source, category? }`).
 - `unity://console/scripts` — C# console scripts in the Explorer `Scripts` folder.
 - `unity://hooks` — currently active method hooks.

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
- `MousePick` (mode `world` = top-most hit; mode `ui` = ordered `Items` + primary `Id`)
- `GetCameraInfo`
- `GetSelection`
- `TailLogs`
 - `GetVersion`

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
- Time-scale:
  - `GetTimeScale()` — read current `Time.timeScale` and lock state.
  - `SetTimeScale(value, lock?, confirm?)` — clamped (0..4), requires `allowWrites` + confirmation.
- Config:
  - `SetConfig(allowWrites?, requireConfirm?, enableConsoleEval?, componentAllowlist?, reflectionAllowlistMembers?, hookAllowlistSignatures?, restart?)`
  - `GetConfig()` — returns a sanitized view of the current config.
- Selection / hooks / console:
  - `SelectObject(objId)` — open a GameObject in the inspector.
  - `ConsoleEval(code, confirm)` — evaluate a small C# snippet (requires `enableConsoleEval: true`).
  - `HookAdd(typeFullName, method, confirm)` / `HookRemove(signature, confirm)` — guarded by `hookAllowlistSignatures`.

## Stream Events

All server‑side notifications are delivered over `stream_events` as JSON‑RPC 2.0 notifications:

```jsonc
{ "jsonrpc": "2.0", "method": "notification", "params": { "event": "<name>", "payload": { ... } } }
```

Event types:

- `log` — `{ level, message, t, source, category? }`
- `selection` — `{ activeId, items[] }`
- `scenes` — `{ loaded[], count }`
- `scenes_diff` — `{ added[], removed[] }`
- `inspected_scene` — `{ name, handle, isLoaded }`
- `tool_result` — `{ name, ok, result? | error? }`

## Allowlists

Two allowlists are configured in `mcp.config.json` and editable via the Options panel:

- Component allowlist: `componentAllowlist`  
  - Array of `FullTypeName` strings.  
  - `AddComponent` and `RemoveComponent` are restricted to these types (or indices).
- Reflection allowlist: `reflectionAllowlistMembers`  
  - Array of `"Type.Member"` strings, e.g., `"UnityEngine.Light.intensity"`.  
  - `SetMember` / `CallMethod` require entries here.

Empty allowlists disable the corresponding write features (for safety).

## C# stream_events example

For a minimal C# client that connects to the in‑process MCP server and prints streamed events over HTTP:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main()
    {
        // Load discovery file
        var path = Environment.GetEnvironmentVariable("UE_MCP_DISCOVERY");
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
        if (!File.Exists(path))
        {
            Console.Error.WriteLine("Discovery file not found; is Unity Explorer MCP running?");
            return;
        }

        using var fs = File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);
        var root = doc.RootElement;
        var baseUrlStr = root.GetProperty("baseUrl").GetString();
        var baseUri = new Uri(baseUrlStr!, UriKind.Absolute);

        using var http = new HttpClient { BaseAddress = baseUri };

        var payload = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "stream_events",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/message") { Content = content };
        req.Headers.Accept.Clear();
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            Console.WriteLine(line);
        }
    }
}
```

This matches the `stream_events` behavior and will print JSON‑RPC `notification`, `result`, and `error` objects as one JSON object per line.

## Troubleshooting

- No discovery file:
  - Check `%TEMP%\unity-explorer-mcp.json` exists (or `UE_MCP_DISCOVERY` override).
  - PowerShell:
    - `Get-ChildItem $env:TEMP\unity-explorer-mcp.json`
    - `Get-Content  $env:TEMP\unity-explorer-mcp.json | Write-Host`
- Wrong base URL or port:
  - Verify `baseUrl` and `port` values in the discovery file match what you expect.
 - No events on stream_events:
  - Confirm `modeHints` includes `"streamable-http"` in the discovery file.
  - Use the C# snippet above or `curl -N -H "Content-Type: application/json" -d '{\"jsonrpc\":\"2.0\",\"id\":\"1\",\"method\":\"stream_events\"}' http://<host>:<port>/message`.
  - Trigger activity in the Unity game (logs, selection changes, scene changes, or tool calls) and verify JSON lines appear.

## Inspector Quick‑Start (`@modelcontextprotocol/inspector`)

1. Start a CoreCLR Unity title with Unity Explorer and ensure the MCP server is enabled (`mcp.config.json: { "enabled": true }`).
2. From your dev machine, run:

   ```bash
   npx @modelcontextprotocol/inspector --transport http --server-url http://<TestVM-IP>:51477
   ```

3. In the inspector UI:
   - Call `initialize` and then `notifications/initialized`.
   - Use “List Tools” to discover `GetStatus`, `ListScenes`, `SearchObjects`, etc.
   - Use “Read Resource” with URIs such as `unity://status`, `unity://scenes`, `unity://scene/0/objects?limit=10`, `unity://selection`, `unity://logs/tail?count=100`.
   - Open `stream_events` to watch `log`, `selection`, `scenes`, and `tool_result` notifications while you interact with the game.

## Notes

- Errors follow JSON-RPC envelope with `error.data.kind` (`InvalidArgument|NotFound|PermissionDenied|RateLimited|Internal|NotReady`) and optional `hint`. Tool failures return `{ ok:false, error:{ kind, message, hint? } }`.
- Non‑CoreCLR targets (Mono, Unhollower) do not host the MCP server.
- All Unity API calls are marshalled to the main thread.
