# UnityExplorer Tests

Tests for the MCP surface live under this folder. Current suites:

- `dotnet/` — xUnit contract tests for the in‑process MCP server (`UnityExplorer.Mcp.ContractTests`). For Mono/net35 validation, follow the checklist in `README-mcp.md` and then run `tools/Run-McpMonoSmoke.ps1`. Test‑VM Space Shooter env details live in `plans/space-shooter-test-plan.md`.

Run the .NET suite:

```bash
cd UnityExplorer/tests/dotnet
dotnet test
```
