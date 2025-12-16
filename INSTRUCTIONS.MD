# Next iteration instructions (Iteration 13)

Focus
- Validate `/mcp` alias and inspector CLI-only flows on both IL2CPP and Mono hosts.

Tasks
- Run inspector CLI one-liners against IL2CPP using `/mcp`:
  - `npx @modelcontextprotocol/inspector --cli --transport http http://192.168.178.210:51477/mcp --method tools/list`
  - `npx @modelcontextprotocol/inspector --cli --transport http http://192.168.178.210:51477/mcp --method tools/call --tool-name GetStatus`
- Run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477/mcp` and ensure it also works when `BaseUrl` omits `/mcp`.
- Run IL2CPP smoke: `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 10` (use the `/mcp` alias as needed).
- Run Mono inspector + smoke:
  - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478/mcp`
  - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 5 -StreamLines 2 -EnableWriteSmoke`
  - `npx @modelcontextprotocol/inspector --cli --transport http http://192.168.178.210:51478/mcp --method tools/list`
- Rerun contract tests with a running host using `UE_MCP_DISCOVERY=<path to active discovery> dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`; note any failures if the host is unavailable.
- After any write smoke, reset MCP config to safe defaults (`allowWrites=false`, `requireConfirm=true`).

Validation
- Confirm `/mcp` returns 200 for JSON-RPC and SSE (`Accept: text/event-stream`) on both hosts.
- Ensure `tools/Start-McpInspectorUi.ps1` prints the deprecation message and exits non-zero.
