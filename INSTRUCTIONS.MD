# Iteration 9/10 — Inspector UI stream notifications + Mono UI connect

Rule
- Keep Space Shooter running in an interactive GPU session (avoid headless launches that hit Microsoft Basic Render Driver → graphics device null).
- Leave `allowWrites=false` and `requireConfirm=true` when done; reset any config you touch.
- Test-VM validation is a gate: for any behavior change, validate on the Test-VM in the same iteration.

Hosts
- IL2CPP/CoreCLR: http://192.168.178.210:51477
- Mono:          http://192.168.178.210:51478

Baselines (run before/after fixes)
- Inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `-BaseUrl http://192.168.178.210:51478`.
- Contract suite: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`.
- Mono smoke (writes enabled): `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`.

Tasks
0) Ensure both games are running on NVIDIA (interactive session). Tail Latest.log/Player.log for Basic Render Driver; restart via interactive session if needed.
1) Run the inspector UI (browser, not just --cli) on the Test‑VM for BOTH hosts using the no-typing PowerShell snippet in `plans/space-shooter-test-plan.md`.
   - Validate: `list_tools` renders with schemas; `call_tool` GetStatus + TailLogs works; `read_resource` `unity://status` + `unity://scenes` works; `stream_events` shows at least one notification (tool_result/log/selection).
   - If server notifications stay empty, investigate stream_events wiring in inspector vs MCP (proxy vs direct) and fix DTOs/tests as needed.
2) Fix any UI/schema/resource issues surfaced by the inspector UI; update `plans/mcp-interface-concept.md` + DTOs/tests; re-run baselines.
3) Mark the Inspector UI checkbox in `plans/unity-explorer-mcp-todo.md` when UI is clean on both hosts.
4) Leave allowWrites=false/requireConfirm=true; keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
5) Sync docs/plans and commit.

Housekeeping
- Prefer `pwsh ./tools/Get-ML-Log.ps1 -Stream` to watch startup; stop the game before copying mods with `tools/Update-Mod-Remote.ps1`.
- Use SpawnTestUi blocks for writes; keep writes gated by confirm.
