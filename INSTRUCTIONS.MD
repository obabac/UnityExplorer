# Iteration Instructions (IL2CPP SelectObject/log crash fixed)

Goal: keep the IL2CPP SpaceShooter MCP host green after the headless-safe SelectObject + JSON-RPC log fixes; keep docs/tests/DTOs aligned.

State:
- Target host: http://192.168.178.210:51477 (SpaceShooter_IL2CPP). Discovery file used for tests: /tmp/ue-mcp-il2cpp-discovery.json.
- Build/deploy: `pwsh ./build-ml-coreclr.ps1` from repo root (copies to the Test-VM automatically). Stop SpaceShooter before copying if it is running, then restart it and poll `call_tool GetStatus` until ready.
- Config defaults restored: allowWrites=false, requireConfirm=true, enableConsoleEval=false. SelectObject records selection without touching the inspector UI when headless. JSON-RPC errors log `[MCP] error ...` via LogBuffer only (no ExplorerCore.LogWarning on background threads).

Validation to rerun after changes:
- Smoke: `pwsh ./tools/Invoke-McpSmoke.ps1 -BaseUrl http://192.168.178.210:51477 -LogCount 20`.
- Contracts (Release): `UE_MCP_DISCOVERY=/tmp/ue-mcp-il2cpp-discovery.json pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release` (currently all green; 1 skipped).

Notes:
- Keep plans (`.plans/unity-explorer-mcp-plan.md`, `.plans/unity-explorer-mcp-todo.md`) and `plans/mcp-interface-concept.md` in sync with any shape/behavior changes.
- Reset configs after experiments (writes stay disabled by default). Mono host not touched this iteration; last known smoke green at http://192.168.178.210:51478.
