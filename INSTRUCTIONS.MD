# Iteration Instructions (Mono smoke validation + parity)

High-level goal: validate the new Mono MCP smoke/CI entry and keep CoreCLR green while closing remaining Mono parity gaps.

## 1) Mono smoke validation
- Run `pwsh ./tools/Run-McpMonoSmoke.ps1 -BaseUrl http://127.0.0.1:51477 -LogCount 10 -StreamLines 3` against a Mono host when available; record results (pass/fail + logs) in the plans/README.
- The smoke flow must exercise initialize → notifications/initialized → list_tools → call_tool (`GetStatus`, `TailLogs`, `MousePick`) → read_resource (`unity://status|scenes|logs/tail`) → `stream_events` (expect `tool_result`).
- If timeouts or stream gaps appear, tune the script (timeouts/lines) and update docs accordingly.

## 2) Mono parity follow-up
- Address remaining Mono gaps vs CoreCLR (selection stability, camera parity, guarded writes) per `.plans/unity-explorer-mcp-todo.md` section 11; update DTOs/tests/docs together when shapes change.
- Keep `MousePick` world/ui behaviour aligned (multi-hit for UI) and document any Mono-specific limits.

## 3) Inspector + IL2CPP sanity
- Keep CoreCLR contract tests green: `pwsh ./tools/Run-McpContractTests.ps1 -Configuration Release` (skips when no host).
- When touching shared MCP code, sanity-check inspector/stream_events against the Test-VM (`http://192.168.178.210:51477`) if available; note any schema or error envelope regressions.

## 4) Docs & plans
- Keep these in sync with behaviour/tests: `plans/unity-explorer-mcp-plan.md`, `plans/unity-explorer-mcp-todo.md`, `plans/mcp-interface-concept.md`, `README-mcp.md`, `tests/dotnet/README.md`.
- Document Mono smoke outcomes and any parity deltas in the plan/todo; avoid duplicating shapes across docs (concept doc is the source of truth).

## 5) Working style
- Prefer PowerShell helpers/one-liners; avoid touching `.codex/*` artifacts.
- Ask only on true blockers; make reasonable assumptions and document them.
- Update this file at the end of the iteration with fresh instructions; default to leaving changes unstaged unless explicitly asked to commit.
