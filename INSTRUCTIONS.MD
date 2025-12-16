# Next iteration instructions (Iteration 18)

Focus
- Fix the Mono contract failure: `Write_Tools_Respond_With_Expected_Errors_By_Default` fails because `AddComponent`/`RemoveComponent` are missing (404). Bring Mono parity or adjust expectations while keeping safety gating.

Repro
- Mono host: http://192.168.178.210:51478 (Space Shooter Mono).
- Discovery: `ue-mcp-mono-discovery.json` in repo.
- Smoke: `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 5` (writes off by default).
- Contract: `UE_MCP_DISCOVERY=/home/onur/p-unity-explorer-mcp/ue-mcp-mono-discovery.json dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj -c Release` (current: 54 passed / 1 failed AddComponent 404 / 1 skipped).

Tasks
- Implement or guard Mono `AddComponent`/`RemoveComponent` so `call_tool` returns structured PermissionDenied (allowWrites=false) instead of 404; keep `allowWrites` + `requireConfirm` enforcement.
- Update `list_tools` schema and docs (`plans/mcp-interface-concept.md`, `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`) to match the Mono surface.
- Re-run Mono contract suite + inspector CLI + `Run-McpMonoSmoke`; record results and reset writes (allowWrites=false, requireConfirm=true).

Safety
- Keep writes off unless explicitly needed; if enabled for tests, reset to `allowWrites=false`, `requireConfirm=true` afterward.
- If Mono build looks stale, rebuild with `dotnet build src/UnityExplorer.csproj -c ML_Mono` and redeploy to `C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/Mods/` (stop the game before copying).
