# Iteration 8/10 — Inspector UI validation + IL2CPP/Mono stability

Rule
- Keep Space Shooter running in an interactive GPU session (avoid headless launches that hit Microsoft Basic Render Driver → graphics device null).
- Leave `allowWrites=false` and `requireConfirm=true` when done; reset any config you touch.
- Keep `.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` in sync with actual DTOs/tests.

Hosts
- IL2CPP/CoreCLR: http://192.168.178.210:51477
- Mono:          http://192.168.178.210:51478

Baselines
- Inspector CLI: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `-BaseUrl http://192.168.178.210:51478` (tools/list, resources/list/read status, tools/call GetStatus).
- Contract suite: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release` after changes; watch for Basic Render Driver in Player.log/Latest.log.
- Mono smoke (writes enabled): `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke`.

Tasks
0) Make sure the game runs on NVIDIA (interactive session); tail Latest.log/Player.log for Basic Render Driver, restart via interactive session if needed.
1) Run the inspector **UI** (browser, not just --cli) on both hosts using the PowerShell snippet in AGENTS/INSTRUCTIONS. Validate: list_tools renders schemas, call_tool GetStatus + TailLogs works, read_resource unity://status + unity://scenes works, stream_events shows at least one notification (tool_result/log/selection). Capture any schema/input issues and update `plans/mcp-interface-concept.md` / DTOs / tests. Mark the Inspector UI checkbox in `plans/unity-explorer-mcp-todo.md` when done.
2) If shapes change, keep `plans/mcp-interface-concept.md` as the single source of truth and adjust code/tests/README accordingly. Re-run contract suite + inspector CLI after fixes.
3) Leave allowWrites/requireConfirm in the safe state; keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
4) Sync docs/plans and commit.

Housekeeping
- Prefer `pwsh ./tools/Get-ML-Log.ps1 -Stream` to watch startup; stop the game before copying mods with `tools/Update-Mod-Remote.ps1`.
- Use SpawnTestUi blocks for writes; keep writes gated by confirm.
