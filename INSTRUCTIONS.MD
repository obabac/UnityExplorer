# Iteration — Contract sweep + Mono parity follow-up

Context
- `tools/Start-McpInspectorUi.ps1` launches the inspector UI with prefilled `transport=streamable-http&serverUrl=...` (no typing). Ran on the Test-VM for IL2CPP (:51477) and Mono (:51478); CLI smokes pass on both hosts.
- ML_Mono adds a post-build copy into `Release/.../Mods/`; `sha256sum Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll Release/UnityExplorer.MelonLoader.Mono/Mods/UnityExplorer.ML.Mono.dll` matches after `dotnet build -c ML_Mono`. Deployed to `SpaceShooter_Mono`; `pwsh ./tools/Run-McpMonoSmoke.ps1 -EnableWriteSmoke` now passes end-to-end.
- Contract suite stays green (54 passed, 1 skipped). Games are stopped on the Test-VM; allowWrites/requireConfirm should stay false/true when you finish.

Rules
- Validate on the Test-VM in the same iteration for any behavior change; keep `allowWrites=false` and `requireConfirm=true` when done.
- Keep `Mods\\UeMcpHeadless.dll` disabled; launch games in an interactive GPU session.
- Prefer PowerShell one-liners; keep plans (`plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`) in sync.

Tasks (suggested)
1) Re-run contract tests on IL2CPP host using the discovery file: `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`; record results in the plan/todo.
2) Tackle one Mono parity gap (e.g., console/scripts/hooks coverage or stream coverage) without regressing IL2CPP; document any shape changes in `plans/mcp-interface-concept.md` and add tests.
3) Use `tools/Start-McpInspectorUi.ps1` on the host you touched and confirm UI can list tools/resources, call `GetStatus`, read `unity://status`/`unity://scenes`, and show at least one `tool_result` notification; note findings in the plan/todo.

Hosts
- IL2CPP: http://192.168.178.210:51477
- Mono:   http://192.168.178.210:51478
