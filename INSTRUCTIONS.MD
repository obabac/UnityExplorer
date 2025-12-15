# Iteration 3/10 — Mono read parity + inspector UI schema

Goal
- Close Mono read parity gaps (camera/freecam + MousePick UI multi-hit) and align DTOs/docs/tests with what the hosts actually return.
- Re-check inspector UI/CLI schema on both hosts after schema tweaks; keep writes disabled after work.

Rules
- Any behavior change ⇒ validate on the Test-VM in the SAME iteration.
- Inspector is a gate: run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` (IL2CPP) and `-BaseUrl http://192.168.178.210:51478` (Mono) after changes.
- Keep `allowWrites=false` and `requireConfirm=true` when you finish. Leave `Mods\\UeMcpHeadless.dll` disabled; launch games in an interactive GPU session.
- Keep docs in sync: `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and `plans/mcp-interface-concept.md` if shapes change.
- Use Space Shooter on the Test-VM; avoid game-specific assumptions.

Test-VM hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

Tasks
1) Mono read parity: inspect/fix `GetCameraInfo` + `MousePick` (UI multi-hit ordering/primary Id) on Mono to match IL2CPP; update DTOs/docs/tests accordingly and extend Mono smoke if needed.
2) Inspector UX: run the inspector UI (`tools/Start-McpInspectorUi.ps1`) or CLI on both hosts; fix any `inputSchema` or resource metadata issues and record findings in `plans/unity-explorer-mcp-todo.md`.
3) Safety gates: rerun `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke`, `pwsh ./tools/Run-McpInspectorCli.ps1` on both hosts, and `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`; ensure configs end at safe defaults.

Finish
- Update `plans/unity-explorer-mcp-plan.md` snapshot + `plans/unity-explorer-mcp-todo.md` status (and `plans/mcp-interface-concept.md` if shapes changed).
- Write next iteration instructions into `INSTRUCTIONS.MD` (Iteration 4/10) based on the roadmap, then commit.
