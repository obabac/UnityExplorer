# Iteration 9/10 — Run Inspector UI flows via restored win-dev control plane

Context
- win-dev control plane is back: scheduled tasks `McpProxy8082` (desktop-commander) and `McpProxy8083` (mcp-control) wrap `mcp-proxy` on ports 8082/8083. Health check: `curl -I http://192.168.178.210:808{2,3}/mcp` → 200. Requests need `Accept: application/json, text/event-stream`, the `mcp-session-id` header from `initialize`, and method names `tools/list` + `tools/call` (e.g., `get_config`, `get_screen_size`). Logs: `C:\codex-workspace\logs\mcp-proxy-808{2,3}.log`.
- UnityExplorer MCP hosts stay up: IL2CPP `http://192.168.178.210:51477`, Mono `http://192.168.178.210:51478`; contract tests 55 passed / 1 skipped. Mono MousePick world still returns `Items=[]` (IL2CPP null).

Goal
1) Validate Inspector UI (browser) end-to-end on IL2CPP + Mono using the restored win-dev UI/command endpoints.
2) Capture evidence (logs and/or screenshot under `C:\codex-workspace\`) and sync plan/todo/test-plan with results.
3) If time remains, outline the fix/test plan for Mono MousePick parity.

Tasks
A) Healthcheck win-dev control plane: confirm tasks running (`Get-ScheduledTask -TaskName McpProxy8082/8083` or `Start-ScheduledTask` if needed); `curl -I` on 8082/8083; run sample `tools/list` + `tools/call get_config/get_screen_size` with the `mcp-session-id` header.
B) Inspector UI validation:
   - Launch inspector UI on the Test-VM with `Start-McpInspectorUi.ps1` for IL2CPP (`-BaseUrl http://192.168.178.210:51477`) and Mono (`-BaseUrl http://192.168.178.210:51478`, `-Port 6278` if needed).
   - Drive the UI via win-dev-vm-ui (streamable HTTP on 8083): ensure initialize → notifications/initialized → tools/resources render → call `GetStatus`/`TailLogs` → read `unity://status`/`unity://scenes` → open stream and see at least one `tool_result` notification (no schema errors).
   - Save evidence (log lines + screenshot) under `C:\codex-workspace\` and note paths in docs.
C) Update docs: refresh `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` with validation results and any control-plane changes; update `plans/mcp-interface-concept.md` only if payload shapes change.
D) If time: sketch the Mono MousePick `Items=null` parity fix + contract test plan.

Finish
- Keep plan/todo in sync with code and validation results.
- Commit changes.
