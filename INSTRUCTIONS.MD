# Iteration 7/10 — Inspector UI validation + IL2CPP stability

Rule
- Keep Space Shooter running in an interactive GPU session (avoid headless ssh launches that use Microsoft Basic Render Driver and trigger "Graphics device is null").
- Run baseline gates before/after changes: inspector CLI on both hosts, Mono smoke with writes, bad read_resource, IL2CPP contract suite with UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json.
- Leave allowWrites=false and requireConfirm=true when done.

Hosts
- IL2CPP/CoreCLR: http://192.168.178.210:51477
- Mono:          http://192.168.178.210:51478

Current state
- IL2CPP contract suite ran clean twice after restarting Space Shooter in an interactive GPU session; Player-prev.log from the crash showed Microsoft Basic Render Driver -> Graphics device is null.
- Inspector CLI and Mono smoke (write flow) pass on both hosts; allowWrites reset to false on both.
- Bad read_resource returns structured NotFound and `[MCP] error ...` shows in logs.

Tasks
0) Ensure game is running with NVIDIA GPU (interactive session). Tail Latest.log/Player.log for any Basic Render Driver entry; restart via interactive session if needed.
1) Run inspector UI (not just --cli) against both hosts; fix any schema/input issues surfaced by the UI. Document deltas in plans/mcp-interface-concept.md if shapes change.
2) Keep IL2CPP contract suite green: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release` after changes; monitor for graphics device warnings.
3) Leave allowWrites=false/requireConfirm=true; keep game running if stable.
4) Sync docs (`plans/...`, README/INSTRUCTIONS) and commit.

Housekeeping
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
- Use SpawnTestUi blocks for any writes; prefer PowerShell helpers in `tools/*.ps1`.
