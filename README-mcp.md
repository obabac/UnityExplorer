# Unity Explorer MCP (In‑Process) — Preview

This build hosts a Model Context Protocol (MCP) server inside the Unity Explorer DLL for CoreCLR (IL2CPP Interop) targets. The server exposes read‑only tools and resources over HTTP using the official C# SDK.

## Status

- Targets: CoreCLR builds only (`BIE_*_Cpp_CoreCLR`, `ML_Cpp_CoreCLR`, `STANDALONE_Cpp_CoreCLR`).
- Transport: HTTP (Streamable HTTP preferred, SSE fallback) via `ModelContextProtocol.AspNetCore`.
- Default mode: Read‑only.

## Getting Started

1. Launch a Unity title with Unity Explorer (CoreCLR target).
2. The MCP server starts after Explorer initialization.
3. Discovery file is written to `%TEMP%/unity-explorer-mcp.json` with `{ pid, baseUrl, port, modeHints, startedAt }`.
4. Connect a client via the MCP C# SDK using `HttpClientTransport` (AutoDetect mode).

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

## Surface (initial)

- Tools: `get_status`, `list_scenes`, `list_objects` (paged)
- Resources: `unity://status`, `unity://scenes`

More endpoints will be added incrementally per the plan.

## Notes

- Non‑CoreCLR targets (Mono, Unhollower) do not host the MCP server.
- All Unity API calls are marshalled to the main thread.
