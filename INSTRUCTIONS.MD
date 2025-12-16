# Next iteration instructions (Iteration 16)

Focus
- Fix Mono MousePick world parity so `Items` matches IL2CPP (null/omitted when no hit) and add test coverage.

Repro
- Use the Mono host on the Test-VM: `http://192.168.178.210:51478` (Space Shooter).
- Call `MousePick` in `world` mode (inspector CLI or curl) with no hit; IL2CPP returns `Items=null`, Mono currently returns `[]`.

Tasks
- Align the Mono MousePick DTO/serialization with the IL2CPP shape (null when no hits).
- Update `plans/mcp-interface-concept.md` with the expected shape and extend tests/smokes to lock it in.
- Validate on the Test-VM: build/deploy Mono, run `pwsh ./tools/Run-McpInspectorCli.ps1 -BaseUrl http://192.168.178.210:51478` and `pwsh ./tools/Run-McpMonoSmoke.ps1` (enable writes only if required). Re-run the IL2CPP contract suite to ensure no regressions.

Safety
- Keep writes off by default; reset any write toggles after testing.
