# Iteration 2/10 — Validation-first MCP work

Goals
- Keep IL2CPP/CoreCLR (http://192.168.178.210:51477) and Mono (http://192.168.178.210:51478) MCP hosts healthy; `[MCP] error ...` must appear in `unity://logs/tail` on both after errors.
- Advance roadmap items (Mono parity, inspector UX/tests) per `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md`.

Required checks (run when touching MCP behavior)
1) Inspector CLI (both hosts):
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`
2) Mono guarded write smoke:
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`
3) Error logging validation:
   - Post bad `read_resource` to both hosts and confirm `[MCP] error ...` in `unity://logs/tail?count=50`.
     - Example: `pwsh -NoProfile -Command "Invoke-RestMethod -Method Post -Uri http://192.168.178.210:51477/message -ContentType application/json -Body '{\"jsonrpc\":\"2.0\",\"id\":\"bad\",\"method\":\"read_resource\",\"params\":{\"uri\":\"unity://no-such-resource\"}}' | ConvertTo-Json -Depth 6"` (swap port for Mono).
   - Reset config afterwards: `allowWrites=false`, `requireConfirm=true`.

Housekeeping
- Space Shooter on Test-VM only; restart the corresponding game after deploy (`pwsh ./tools/Update-Mod-Remote.ps1 ...` + PowerShell `Start-Process`).
- Keep `Mods\\UeMcpHeadless.dll` disabled; allowWrites=false at rest.
- Update `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` when DTOs/errors change.
- Commit small, keep docs/tests/DTOs in sync.
