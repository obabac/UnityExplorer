# Iteration 10/10 — Inspector UI flows after mcp-proxy session fix

Context
- Test-VM `mcp-proxy` patched to create a transport even when `mcp-session-id` is present on the first `initialize`; it binds to that session id.
- After a proxy restart, seed the client session once with `initialize` so the harness tools work:
  - Watch `C:\codex-workspace\logs\mcp-proxy-808{2,3}.log` to grab the session id emitted by the client (8082 = win-dev-vm-command, 8083 = win-dev-vm-ui).
  - Send `initialize` with that `mcp-session-id` header and `protocolVersion=2024-11-05`. After seeding, `win-dev-vm-command/get_config` and `win-dev-vm-ui/get_screen_size` succeed.
  - Current seeded sessions (if still running): 8082 `25932817-8547-47ec-9c83-64b52d71e9f7`, 8083 `0f363e73-95f0-4a96-b480-a83e5fe582ff`.
- UnityExplorer MCP hosts are up: IL2CPP `http://192.168.178.210:51477`, Mono `http://192.168.178.210:51478`; Mono MousePick world still returns `Items=[]` vs IL2CPP `null`.

Goals
1) Run Inspector UI (browser) flows on IL2CPP + Mono via the win-dev proxies and capture evidence.
2) Keep plan/todo/docs in sync with the proxy seeding note and UI validation.
3) Leave the harness usable (proxies running, sessions seeded, logs recorded).

Tasks
A) Control plane health
- If proxies were restarted, tail the logs to capture the client session ids and seed with one `initialize` (curl with `mcp-session-id` and `protocolVersion: 2024-11-05`).
- Verify `win-dev-vm-command/get_config` (8082) and `win-dev-vm-ui/get_screen_size` (8083) return 200 via the harness.

B) Inspector UI validation
- IL2CPP: `pwsh C:\codex-workspace\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51477 -LogPath C:\codex-workspace\mcp-inspector-il2cpp-ui.log`
- Mono: `pwsh C:\codex-workspace\Start-McpInspectorUi.ps1 -BaseUrl http://192.168.178.210:51478 -LogPath C:\codex-workspace\mcp-inspector-mono-ui.log -Port 6278`
- Drive with `win-dev-vm-ui`: initialize → notifications/initialized → list tools/resources → call `GetStatus` + `TailLogs` → read `unity://status`/`unity://scenes` → open `stream_events` and observe a `tool_result`. Save a screenshot under `C:\codex-workspace\` for each host.

C) Documentation
- Update `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/space-shooter-test-plan.md` with proxy seeding and Inspector UI results (logs + screenshot paths).

Finish
- Keep plan/todo in sync and commit all changes.
