# Iteration Instructions (2025-12-09)

- Changes made: stream_events now uses WaitForStreamDisconnectAsync with infinite timeouts to remove clients on disconnect; added JsonRpcContractTests.StreamEvents_Allows_Reconnect_After_Client_Close; strengthened DtoSchemaContractTests to read Dto.cs and ensure the UnityExplorer.Mcp namespace; plan/todo already reflect the stream disconnect cleanup work.
- Tests: `dotnet test tests/dotnet/UnityExplorer.Mcp.ContractTests/UnityExplorer.Mcp.ContractTests.csproj` (passes; 44 passed, 1 skipped when MCP host not running).
- Next steps:
  1) Add hook lifecycle contract tests in the dedicated hook test scene to cover HookAdd/HookRemove beyond permission errors.
  2) Flesh out structured error tests (NotReady/NotFound/PermissionDenied/rate limit) and concurrency behaviour to match inspector expectations; update concept doc if shapes change.
  3) Run inspector/Space Shooter validation: initialize → list_tools/read_resource → stream_events, plus stream_events notification coverage on a live host; ensure docs/README-mcp stay in sync.
- Notes: keep plans and docs in sync with DTOs/tests; avoid game-specific assumptions; allowWrites stays false by default on shared hosts.
