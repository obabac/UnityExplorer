# Iteration Instructions — Restore IL2CPP host + smoke scripts

Goal: get both MCP hosts (IL2CPP 51477, Mono 51478) green under inspector CLI + smoke scripts; IL2CPP currently returns connection refused.

Tasks:
1) Bring the IL2CPP Space Shooter host back up (or redeploy the CoreCLR build) on the Test-VM; restart the game if needed and confirm `Latest.log` shows MCP running.
2) Run inspector CLI smokes:
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478 [-AuthToken ...]`
3) Run existing smokes:
   - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 10`
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 5 -StreamLines 2`
4) If IL2CPP still fails, grab MelonLoader logs via `pwsh ./tools/Get-ML-Log.ps1` (use `-Stream` for live tail), note findings, and update plan/todo/README with any changes.

Notes:
- New inspector CLI smoke script lives at `tools/Run-McpInspectorCli.ps1` (runs tools/list, resources/list, read status, call GetStatus; optional Authorization header) and is documented in `README-mcp.md`.
- Keep `.plans/unity-explorer-mcp-plan.md` and `.plans/unity-explorer-mcp-todo.md` aligned with code/tests; update `plans/mcp-interface-concept.md` if DTO shapes change.
- Keep writes disabled by default; avoid refactors outside MCP scope.
