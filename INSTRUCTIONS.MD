# Next iteration instructions (Iteration 17)

Focus
- Deploy the Mono MousePick Items=null fix and validate live on the Test-VM (Space Shooter Mono host).

Repro
- Test-VM Mono host: http://192.168.178.210:51478 (Space Shooter Mono).
- Call `MousePick` in `world` mode with no hit via inspector CLI or curl; expected JSON has `Items=null` (not `[]`).
- Use `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://192.168.178.210:51478` and `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478`; keep writes off unless explicitly needed.

Tasks
- Build/deploy ML_Mono (or reuse the latest deploy) and validate `MousePick` world Items=null on the Test-VM (inspector CLI + smoke output).
- Add or refresh `ue-mcp-mono-discovery.json` pointing at the Mono host and run `UnityExplorer.Mcp.ContractTests` against it; record any skips/failures.
- Keep `plans/mcp-interface-concept.md`, `plans/unity-explorer-mcp-plan.md`, and `plans/unity-explorer-mcp-todo.md` in sync if discovery steps or validation outcomes change.

Safety
- Keep writes off by default; if you enable them for smoke, reset `allowWrites=false` and `requireConfirm=true` afterward.
