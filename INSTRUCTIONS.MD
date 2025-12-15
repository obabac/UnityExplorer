# Iteration 2/10 — Mono parity + stream notification test

Goal
- Push Mono MCP toward CoreCLR parity by adding one guarded write with allowlist/confirm parity (e.g., SetMember or equivalent) and extend Mono smoke coverage.
- Add a contract test for a non-tool stream notification (log or selection) while keeping IL2CPP contract suite green.

Rules
- Any behavior change => validate on the Test-VM in the SAME iteration.
- Inspector is a gate: run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51477` (IL2CPP) and `-BaseUrl http://192.168.178.210:51478` (Mono) after changes.
- Keep `allowWrites=false` and `requireConfirm=true` when you finish.
- Keep `Mods\\UeMcpHeadless.dll` disabled; launch games in an interactive GPU session.
- Keep docs in sync: `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, and (if shapes change) `plans/mcp-interface-concept.md`.

Test-VM hosts
- IL2CPP/CoreCLR: `http://192.168.178.210:51477`
- Mono:          `http://192.168.178.210:51478`

Tasks
1) Implement the next safe Mono write toward parity (e.g., `SetMember` with allowlists) with CoreCLR error envelope + `allowWrites`/`requireConfirm` gating; update DTOs/docs/tests as needed.
2) Extend `tools/Run-McpMonoSmoke.ps1` to cover the new write path; rerun `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -EnableWriteSmoke` and ensure config resets to safe defaults.
3) Add a contract test for at least one non-tool `stream_events` notification (selection or log); rerun `UE_MCP_DISCOVERY=ue-mcp-il2cpp-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests -c Release`.
4) Run inspector CLI on both hosts after changes. If anything fails, fix it and rerun the same gate(s) in this iteration.

Finish
- Update `plans/unity-explorer-mcp-plan.md` snapshot + `plans/unity-explorer-mcp-todo.md` status (and `plans/mcp-interface-concept.md` if shapes changed).
- Write next iteration instructions into `INSTRUCTIONS.MD` (Iteration 3/10) based on the roadmap, then commit.
