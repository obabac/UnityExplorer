# Iteration 6/10 — Stabilize IL2CPP contract tests

Rule
- Focus on the IL2CPP contract suite crash (UnityPlayer.dll 0x80000003 / "Graphics device is null"). Fix before adding new behavior.
- Run baseline gates (inspector CLI both hosts, Mono smoke with writes, bad read_resource) before and after changes.

Hosts
- IL2CPP/CoreCLR: http://192.168.178.210:51477
- Mono:          http://192.168.178.210:51478

Current state
- Selection stream + MousePick UI contracts are in place; docs/tests updated. AssemblyInfo.cs now disables xUnit parallelization.
- Mono inspector + Run-McpMonoSmoke pass; IL2CPP inspector CLI passes. Full IL2CPP contract suite still crashes the game after the first few tests; individual tests pass when run alone.

Tasks
0) Run baseline gates on both hosts.
1) Investigate the IL2CPP crash: check Latest.log, UnityExplorer logs under Mods/sinai-dev-UnityExplorer/Logs, and Player.log. Try to find a minimal repro (possibly stream_events/selection/SetConfig paths). Stop Mono if needed and focus on IL2CPP.
2) Fix the crash, rebuild/deploy IL2CPP (`pwsh ./build-ml-coreclr.ps1`), restart the game, and rerun full `dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release` with `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json`.
3) Keep allowWrites=false when done; leave the game running if stable.
4) Sync docs (`plans/...`) and this file, then commit.

Housekeeping
- Keep `Mods\\UeMcpHeadless.dll` disabled on the Test-VM.
- Use SpawnTestUi blocks for any writes; prefer PowerShell helpers in `tools/*.ps1`.
