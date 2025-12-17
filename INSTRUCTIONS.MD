# Next iteration instructions (Iteration 22)

Focus (do in this order)
1) Console scripts parity (TOP): implement file read/write/delete over MCP on **both** hosts (IL2CPP + Mono).
2) Add contract tests for the new console-script surface (round-trip + cleanup).
3) Test-VM validation in the same iteration (inspector CLI + smoke + contract tests) on both ports.

Rules / gates
- Inspector validation is CLI-only (no UI).
- Test-VM validation must happen in the same iteration as any behavior change.
- IL2CPP regression is required when shared query/DTO code changes: run inspector CLI + smoke + contract tests.
- Keep writes off by default; any write-smoke must reset config at the end.

Targets
- IL2CPP host: `http://192.168.178.210:51477/mcp`
- Mono host: `http://192.168.178.210:51478/mcp`
- Discovery files in repo: `ue-mcp-il2cpp-discovery.json`, `ue-mcp-mono-discovery.json`

Tasks

1) Console scripts (read)
- Implement `ReadConsoleScript(path)` tool on BOTH hosts.
- Implement resource `unity://console/script?path=...` on BOTH hosts.
- Contract must match `plans/mcp-interface-concept.md` (planned section).
- Path handling:
  - Accept `path` as a relative or absolute path.
  - Validate it stays inside `ConsoleController.ScriptsFolder`.
  - Require `.cs` extension.
  - Enforce a fixed max read size and set `Truncated=true` when needed.

2) Console scripts (write)
- Implement `WriteConsoleScript(path, content, confirm=false)` and `DeleteConsoleScript(path, confirm=false)` on BOTH hosts.
- Gating:
  - require `allowWrites=true`.
  - if `requireConfirm=true`, require `confirm=true`.
- Safety:
  - Same path validation rules as read.
  - Enforce max write size.

3) Contract tests
- Add a new test that does a safe script lifecycle:
  - Write `mcp-test-<guid>.cs` (simple `return 123;` or similar).
  - Read it back via `ReadConsoleScript` (or `read_resource` on `unity://console/script?...`).
  - Delete it.
  - Always cleanup in `finally`.
- Run on BOTH hosts (Mono + IL2CPP).

Validation (must do)
- Mono:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478/mcp`
  - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 5`
  - `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-mono-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release`
- IL2CPP regression:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477/mcp`
  - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 10`
  - `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release`

Docs
- Keep in sync if anything changes:
  - `plans/mcp-interface-concept.md` (move items from Planned → Implemented when done)
  - `plans/unity-explorer-mcp-plan.md`
  - `plans/unity-explorer-mcp-todo.md`
  - `README-mcp.md` (summary only; no duplication)

Commit
- Commits are allowed; commit your changes with a clear message.
