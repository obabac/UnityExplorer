# Iteration Instructions — Mono guarded writes + IL2CPP host

Goals:
- Validate new Mono guarded write tools (SetConfig/SetActive/SelectObject/GetTimeScale/SetTimeScale) on the Test-VM.
- Bring the IL2CPP host back online and rerun inspector/smoke checks.
- Keep plans/todo/concept docs in sync with any shape or host changes.

Steps:
1) Mono writes smoke: `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3 -EnableWriteSmoke` (expect SetTimeScale ok + config reset). Record results.
2) Restart/redeploy IL2CPP host (`http://192.168.178.210:51477`), then run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` and `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20`.
3) If time remains, start on the dropdown Il2Cpp cast crash so `Mods/UeMcpHeadless.dll` can be removed (priority).

Notes:
- Tests: `dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj`; `dotnet build src/UnityExplorer.csproj -c ML_Mono`.
- Keep `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` aligned with any code/host changes.
- Commit changes when done.
