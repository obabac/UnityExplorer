# UnityExplorer Tests

Tests for the MCP surface live under this folder. Current suites:

- `dotnet/` — xUnit contract tests for the in‑process MCP server (`UnityExplorer.Mcp.ContractTests`). Use `tools/Run-McpMonoSmoke.ps1` for the Mono/net35 smoke flow.

Run the .NET suite:

```bash
cd UnityExplorer/tests/dotnet
dotnet test
```
