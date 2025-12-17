# UnityExplorer MCP — .NET Tests

This folder contains the initial contract tests for the in‑process MCP server.

## Layout

- `UnityExplorer.Mcp.ContractTests/` — xUnit tests for discovery and basic contracts.
- `Directory.Build.props` — shared test settings (net8.0, nullable, implicit usings).

## Running

1. Ensure Unity Explorer MCP server is running inside a target game.
2. The server writes a discovery file at `%TEMP%/unity-explorer-mcp.json`.
3. Run:

```bash
cd UnityExplorer/tests/dotnet
dotnet test
```

If the server is not running, discovery tests will fail or be skipped.

## Optional gated write tests

Some contract tests perform guarded writes (they toggle `allowWrites` briefly and then reset config). They are disabled by default and must be enabled explicitly when running against a safe test host (Space Shooter).

- `UE_MCP_HOOK_TEST_ENABLED=1` — runs hook lifecycle + advanced hook tests (uses `hookAllowlistSignatures=["UnityEngine.GameObject"]`).
- `UE_MCP_CONSOLE_SCRIPT_TEST_ENABLED=1` — runs console script read/write/delete round-trip (writes a temporary `mcp-test-<guid>.cs` under `ConsoleController.ScriptsFolder` and deletes it).

## Mono smoke entry (no Test-VM)

For Mono/net35 hosts, first follow the Mono host validation checklist in `README-mcp.md`, then use the lightweight smoke script instead of the full contract suite:

```powershell
pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 10 -StreamLines 3
```

Flow: `initialize` → `notifications/initialized` → `list_tools` → `call_tool` (`GetStatus`, `TailLogs`, `MousePick`) → `read_resource` (`unity://status`, `unity://scenes`, `unity://logs/tail`) → `stream_events` (expects a `tool_result` notification). This is suitable for Mono CI jobs where the host runs locally.

## Next steps

- Add `ModelContextProtocol` client package and switch tests to real `HttpClientTransport` once the server exposes a full MCP surface.
- Add JSON snapshot tests for `unity://status`, `unity://scenes`, and object listings.
- Add richer streaming tests for logs/selection using Streamable HTTP (fallback SSE).

## Technical harness / CI usage

These tests are designed as a technical validation harness for the in‑process MCP server:

- When the MCP server is not running:
  - Discovery‑based tests either skip or treat the run as inconclusive.
- When the MCP server is running:
  - `DiscoveryTests` validate the discovery file shape (`pid`, `port`, `baseUrl`, `modeHints`, `streamable-http` hint).
  - `HttpContractTests`, `StatusContractTests`, and `ScenesContractTests` validate `/read` resources.
  - `JsonRpcContractTests` validate:
    - JSON‑RPC responses for `list_tools`, `call_tool`, and `read_resource` via `POST /message`.
    - `stream_events` HTTP streaming contract (chunked JSON, correct content type).
    - Emission and shape of `tool_result` notifications on `stream_events` after `call_tool`.

For CI, the expected pattern is:

1. Start a host process that includes Unity Explorer MCP (CoreCLR, in‑process) and ensure it writes the discovery file.
2. Run:

   ```powershell
   dotnet test UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Debug
   ```

3. Treat any failures as protocol/contract regressions in the MCP HTTP/streaming surface.
