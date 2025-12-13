# Iteration Instructions — MCP error logging validation (SpaceShooter IL2CPP + Mono)

Goal
- Verify MCP error logging runs on the Unity main thread and Mono guarded writes stay safe on the Test-VM hosts.
- Keep IL2CPP/CoreCLR host healthy while validating Mono writes.

Do (in order)
1) Bring up both Space Shooter hosts on the Test-VM and watch for MCP bind lines:
   - IL2CPP: `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`
   - Mono:   `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_Mono\\SpaceShooter.exe"`
   - Tail logs: `pwsh ./tools/Get-ML-Log.ps1 -Stream` (look for binds on 51477 / 51478).

2) Inspector CLI gate (both hosts):
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`

3) Mono guarded write smoke + logging check:
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`
   - Confirm the run required `confirm=true` and reset config to `allowWrites=false` afterward.
   - Trigger a failing request to confirm `[MCP]` errors log without off-thread warnings (e.g., `read_resource` for `unity://no-such-resource` or `call_tool` with bad args) and verify it shows up in `logs/tail` or ML log.

4) If any step fails:
   - Fix the server error envelope/logging or guarded write paths (confirmation semantics, config reset) and keep CoreCLR/Mono parity.
   - Update `plans/mcp-interface-concept.md` if shapes/errors change; keep `README-mcp.md` as summary.
   - Sync `plans/unity-explorer-mcp-todo.md` checkboxes to match what was validated.

5) Housekeeping
   - Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
   - Keep `allowWrites=false` by default; do not leave writes enabled on shared hosts.
   - Ensure `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md` reflect current status.
   - `.codex/exec-logs/*` stays gitignored; keep commits small and scoped.

Notes
- Space Shooter is the primary Test-VM title; avoid game-specific assumptions beyond it.
- Use Space Shooter build + tail log commands above; inspector validation remains a gate for schema changes.
