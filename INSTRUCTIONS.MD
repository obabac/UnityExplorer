# Next iteration instructions (Iteration 11)

Focus
- Get Inspector UI rendering visibly via win-dev UI for IL2CPP + Mono. Start via `Start-McpInspectorUi.ps1` (IL2CPP base URL :51477, Mono :51478 -Port 6278). Seed `mcp-proxy` session after restart with `initialize` (headers: `Accept: application/json, text/event-stream`, include `clientInfo`, e.g., `protocolVersion=2024-11-05`, `capabilities={}`) so `win-dev-vm-ui` alias tools work.
- Validate `win-dev-vm-ui/State-Tool` and `win-dev-vm-ui/Powershell-Tool` again post-restart; keep log refs `C:\codex-workspace\logs\mcp-proxy-808{2,3}.log`.
- Run Inspector UI minimal flow (initialize → notifications/initialized → list_tools → call GetStatus/TailLogs → read unity://status → stream_events tool_result) on both hosts; capture visible screenshots under `C:\codex-workspace\` and log paths.
- If UI still blank, debug (other Edge window, zoom, devtools, alternate browser, headless screenshot already at `C:\codex-workspace\inspector-il2cpp-ui.png`).
- Keep plans/docs in sync (plan/todo snapshot, space-shooter test plan evidence) after validation.

Context
- Hosts up: IL2CPP http://192.168.178.210:51477, Mono http://192.168.178.210:51478 (`/message` 200).
- Contract tests: IL2CPP 55 pass / 1 skip; Mono MousePick parity gap persists (Items=[] vs null).
- Headless IL2CPP Inspector UI capture: `C:\codex-workspace\inspector-il2cpp-ui.png`.

Safety
- Use PowerShell for Windows commands; avoid Playwright installs. No destructive git resets.
