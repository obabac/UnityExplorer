# Iteration Instructions (Inspector schema + IL2CPP regression)

Goal: keep both MCP hosts green and validate inspector schema rendering.

Current hosts:
- IL2CPP/CoreCLR: `http://192.168.178.210:51477` (SpaceShooter_IL2CPP)
- Mono: `http://192.168.178.210:51478` (SpaceShooter_Mono, read-only)

1) Run IL2CPP smoke + contract tests to confirm no regressions:
   - `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20`
   - `pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release -BaseUrl http://192.168.178.210:51477`
   Record pass/fail.
2) Run Mono smoke sanity:
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3`
   Confirm `tool_result` is observed on stream_events.
3) Do one inspector session (prefer IL2CPP host) to verify per-argument `inputSchema` renders without errors; note any schema/validation issues.
4) Sync docs/plans if anything changes (plans/*, README-mcp.md); update `plans/mcp-interface-concept.md` only if DTO/stream shapes shift.
5) Commit all changes.
