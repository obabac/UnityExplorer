# Iteration Instructions — MCP log-main-thread validation

Goal
- Verify the new MCP error logging runs on the Unity main thread and shows `[MCP] error ...` in log tails on both IL2CPP and Mono hosts.
- Keep Mono guarded writes safe on the Test-VM; leave `allowWrites=false` by default.

Do (in order)
1) Bring up both Space Shooter hosts on the Test-VM and watch for MCP bind lines:
   - IL2CPP: `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_IL2CPP\\SpaceShooter.exe"`
   - Mono:   `Start-Process "C:\\codex-workspace\\space-shooter-build\\SpaceShooter_Mono\\SpaceShooter.exe"`
   - Tail logs: `pwsh ./tools/Get-ML-Log.ps1 -Stream` (look for binds on 51477 / 51478).

2) Inspector CLI gate (both hosts):
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477`
   - `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`

3) Error logging check (main-thread + prefix):
   - Trigger a failing request on each host (e.g., POST /message without `method` or `read_resource` for `unity://no-such-resource`).
   - Confirm `[MCP] error ...` appears in `unity://logs/tail` and the MelonLoader log without off-thread warnings.

4) Mono guarded write smoke + reset:
   - `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`
   - Ensure the run required `confirm=true` and reset config to `allowWrites=false` afterward.

5) If any step fails:
   - Fix the error envelope/logging (main-thread dispatch + `[MCP] error` prefix) or guarded write flows; keep CoreCLR/Mono parity.
   - Update `plans/mcp-interface-concept.md` if shapes/errors change; sync `plans/unity-explorer-mcp-todo.md` checkboxes with what was validated.

6) Housekeeping
   - Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
   - Keep `allowWrites=false` by default; do not leave writes enabled on shared hosts.
   - Ensure `plans/unity-explorer-mcp-plan.md` and `plans/unity-explorer-mcp-todo.md` reflect current status.

Notes
- Space Shooter is the primary Test-VM title; avoid game-specific assumptions beyond it.
- Use the commands above on the Test-VM; inspector validation remains a gate for schema changes.
