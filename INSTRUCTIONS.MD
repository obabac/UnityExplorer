# Iteration Instructions — Inspector CLI smoke + regression

Goal: add an inspector CLI smoke script and guard inspector-compatible payloads on both MCP hosts.

## 1) Add an inspector CLI smoke script
- Non-interactive CLI that runs `initialize` → `notifications/initialized` → `tools/list` → `resources/list` → `resources/read unity://status` → `tools/call GetStatus`.
- Accept `-BaseUrl` or auto-discovery; support both IL2CPP (`:51477`) and Mono (`:51478`). Exit non-zero on validation errors.

## 2) Guard new surfaces
- Keep `call_tool` responses in the `text + json` form and ensure `resources/list` stays in sync with `read_resource` handlers. Add regression coverage (test or scripted check).
- Update docs if shapes change: `plans/mcp-interface-concept.md`, `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `README-mcp.md` if user-facing behaviour moves.

## 3) Validate IL2CPP host (http://192.168.178.210:51477)
- Build + deploy: `pwsh ./build-ml-coreclr.ps1`
- Restart SpaceShooter on the Test-VM and wait for MCP ready.
- Run: `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20`
- Run: `UE_MCP_DISCOVERY=/tmp/ue-mcp-il2cpp-discovery.json pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release`
- Run inspector CLI (Linux): `./inspector --method tools/list`, `./inspector --method resources/list`, `./inspector --method resources/read --uri unity://status`, `./inspector --method tools/call --tool-name GetStatus`.

## 4) Validate Mono host (http://192.168.178.210:51478)
- Build + deploy Mono mod (`dotnet build src/UnityExplorer.csproj -c ML_Mono` + copy `Release/UnityExplorer.MelonLoader.Mono/` contents into `SpaceShooter_Mono/Mods` + `UserLibs`). Restart the game.
- Run: `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478 -LogCount 10 -StreamLines 3`
- Run inspector CLI: `npx @modelcontextprotocol/inspector --cli --transport http http://192.168.178.210:51478 --method tools/list` and `--method resources/read --uri unity://status`.

## 5) Commit
- Commit when both hosts are green and docs/plans are updated. Suggested message: `chore: add inspector cli smoke and content regression`
- Keep writes disabled by default; stay within MCP scope.
