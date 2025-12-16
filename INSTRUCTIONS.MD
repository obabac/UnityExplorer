# Next iteration instructions (Iteration 19)

Focus
- Close Mono parity gaps beyond MousePick: verify console/scripts/hook surfaces match IL2CPP and keep UI pick ids resolvable.
- Keep allowWrites gated; prefer read-only validation unless explicitly testing writes.

Repro
- Mono host: http://192.168.178.210:51478 (Space Shooter Mono).
- Discovery: `ue-mcp-mono-discovery.json` in repo.
- Smoke: `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 5` (writes off by default).
- Contract: `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-mono-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release` (current: 55 passed / 0 failed / 1 skipped).
- Inspector: `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478` (CLI only).

Tasks
- Audit Mono `list_tools`/`list_resources` vs `plans/mcp-interface-concept.md`; align descriptions/schemas if any drift from IL2CPP (keep UI MousePick ids resolvable).
- Review Mono parity for console/scripts/hook resources: ensure shapes match the concept doc and update docs/tests if gaps remain (avoid new APIs).
- Keep docs in sync (`plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`) when shapes or behaviors change; note any remaining Mono deltas.
- After any writes/testing, reset config to `allowWrites=false`, `requireConfirm=true`.

Safety
- Keep writes off unless explicitly needed; if toggled, set `allowWrites=false` and `requireConfirm=true` afterward.
- Stop SpaceShooter before redeploying Mono builds (`dotnet build src/UnityExplorer.csproj -c ML_Mono` copies to `Mods/`); deploy to `C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/Mods/` and restart the game.
