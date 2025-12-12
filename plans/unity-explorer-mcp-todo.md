# Unity Explorer MCP – High‑Level TODOs (Streamable HTTP Era)

Date: 2025‑12‑12  
Scope: Remaining work to get close to UnityExplorer feature parity over MCP, with a stable streamable‑http surface.

### Definition of Done (100%)
- All checkboxes in this file are checked (including tests and docs updates).
- `dotnet test UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests` passes.
- `@modelcontextprotocol/inspector` flow works end‑to‑end: `initialize` → `notifications/initialized` → `list_tools` → `call_tool` (at least status/logs) → `read_resource` (status/scenes/objects/search/selection/logs) → `stream_events` (receive non‑tool event).
- Smoke CLI (call‑mcp script) succeeds against a running game.
- Space Shooter host: all contract tests pass; documented write scenarios (`SetActive`, `SelectObject`, future time‑scale) succeed with `allowWrites+confirm`.
- Docs in sync: `plans/mcp-interface-concept.md`, `README-mcp.md`, DTO code, and tests all agree on shapes and errors.

Status (2025-12-12): Space Shooter IL2CPP host unchanged; last contract runs were green (45 passed, 1 skipped placeholder). ML_Mono includes the lightweight MCP host (Newtonsoft.Json + TcpListener) for initialize/list_tools/read_resource/call_tool on read-only surfaces (MousePick world/ui, GetVersion, discovery, log/selection/scene/tool_result stream events). `dotnet build src/UnityExplorer.csproj -c ML_Mono` still succeeds (nullable warnings in `McpSimpleHttp`). No Mono Space Shooter build exists on the Test-VM yet; Unity Editor 2021.3.45f1 lives at `C:\Program Files\Unity 2021.3.45f1`, and only `SpaceShooter_IL2CPP` is present under `C:\codex-workspace\space-shooter-build`. Inspector schema/UX validation remains pending; console scripts and hooks stay disabled on Mono until validated. Mono smoke wrapper `tools/Run-McpMonoSmoke.ps1` (initialize → list_tools → GetStatus/TailLogs/MousePick → read status/scenes/logs → stream_events tool_result) is available for CI/local Mono hosts.

---

## 0. Path to 100% MCP Inspector Validation

This section summarizes what still needs to be in place so that Unity Explorer MCP is fully exercised and “green” under `@modelcontextprotocol/inspector` plus the `UnityExplorer.Mcp.ContractTests` harness.

- Schema & payload validation:
  - [x] Confirm `unity://status`, `unity://scenes`, and `unity://scene/{id}/objects` payloads match `plans/mcp-interface-concept.md`, and adjust either DTOs or tests where they diverge.
  - [x] Ensure all DTOs used by inspector‑facing tools/resources are serializable without custom JSON options (no cycles, no Unity engine types leaking).
- Tool behaviour & write safety:
  - [x] Add a focused contract test for `SelectObject` that asserts selection state changes as expected (round‑trip with `unity://selection`).
  - [x] Add hook lifecycle tests in a dedicated “hook test” scene to validate `HookAdd` / `HookRemove` behaviour beyond permission errors (gated by `UE_MCP_HOOK_TEST_ENABLED=1` and `hookAllowlistSignatures` including a safe type such as `UnityEngine.GameObject`).
- Streaming & error robustness:
  - [x] Verify `stream_events` correctly cleans up on client disconnect (no unbounded dictionary growth) and add a test that repeatedly opens/closes streams (cleanup loop + reconnect test added).
  - [x] Ensure current concurrency behaviour matches `RateLimit_Does_Not_Crash_Server_When_Many_Concurrent_Requests` and, if a 429 limit is enabled, returns a structured JSON error payload.
  - [x] Add structured error JSON tests for common cases (`NotReady`, `NotFound`, `PermissionDenied`) that align server responses with inspector expectations.
- Inspector UX & dev‑experience:
  - [ ] Verify that all tools and resources render without schema or validation errors in `@modelcontextprotocol/inspector` during typical flows (initialize → list_tools/read_resource → stream_events); `list_tools` now ships per-argument schemas and needs a live inspector run to confirm.
  - [x] Extend `UnityExplorer/README-mcp.md` with the final tool/resource list and representative example payloads used by inspector.
  - [x] Add a small smoke CLI/PowerShell script that runs `initialize`, `list_tools`, `GetStatus`, and `TailLogs` against a running Unity Explorer MCP instance, mirroring the `inspector` script defaults (see `tools/Invoke-McpSmoke.ps1`).
  - [x] Add a CI note/script to run `dotnet test UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests` (and optionally a scripted inspector run) as part of the UnityExplorer build/validation pipeline.

---

### Recommended execution order
1) DTO/schema alignment + error envelope + rate limiting.
2) Read surface parity (logs metadata, camera/freecam, mouse inspect multi‑hit) and tests.
3) Guarded writes (SelectObject test, SetMember/ConsoleEval/Hooks) and new time‑scale tool.
4) Streams cleanup and rate‑limit tests.
5) Inspector/README-mcp/documentation polish.
6) Space Shooter validation (no game-specific assumptions).

### Pitfalls / reminders for agents
- Keep `plans/mcp-interface-concept.md`, DTO code, and contract tests in sync; update all three together when shapes change.
- Do not add game-specific assumptions; tests must pass on Space Shooter.
- Guarded writes: always enforce `allowWrites` + `RequireConfirm` and return structured `ok=false` errors instead of throwing.
- When adding new behaviour, include an example payload in the concept doc and a contract test.
- Error envelope: use `error.code/message/data.kind[/hint]`; tool `ok=false` errors mirror `kind/hint`. Rate-limit message: `"Cannot have more than X parallel requests. Please slow down."` (include X).
- Mouse UI multi-hit: UI mode should return a list of hits (`Items`) plus `primaryId`; follow-up via `GetObject`/`GetComponents` (or a UI detail tool) on the selected `Id`.
- Logs: include `source`; if `category` exists, include it or document its absence.
- Time-scale writes: single guarded tool; add tests and docs when implemented.
- Stop SpaceShooter on the Test-VM before copying mods (`Update-Mod-Remote.ps1`) to avoid SCP file-lock failures; restart the game after deployment.

## 1. Transport & Protocol Polish

- [x] Remove legacy SSE wording/naming in code (e.g. `McpSseState` → neutral name) while keeping behavior unchanged.
- [x] Add small sanity tests for:
  - [x] `stream_events` basic shape (already covered) plus long‑lived idle stream resilience.
  - [x] `notifications/initialized` being accepted (no error).
- [x] Document the expected MCP handshake (initialize → notifications/initialized → list_tools/read_resource/stream_events) in README‑mcp.

## 2. Read‑Only Surface Parity (Core Explorer Features)

- Status / Scenes / Objects (mostly done, needs validation polish):
  - [x] Confirm `unity://status`, `unity://scenes`, `unity://scene/{id}/objects` payloads align with the spec in `plans/mcp-interface-concept.md`.
  - [x] Add contract tests for `unity://object/{id}` and `unity://object/{id}/components` via `/read`.
- Search:
  - [x] Expose `unity://search?...` more fully (name/type/path/activeOnly) and add a contract test that exercises multiple filters.
- Selection:
  - [x] Ensure `GetSelection` mirrors Unity Explorer’s notion of “active inspector tab(s)” and write a test for success and error paths (`PermissionDenied`, `ConfirmationRequired`).
- [x] Add a simple `SelectObject(objectId)` tool that mirrors Explorer’s selection change (read‑only impact).
  - [x] Add a focused contract test that exercises `SelectObject` and validates that selection state changes as expected.
- [x] Design + stub reflection write helpers (`SetMember`) with allowlist and add tests that ensure:
  - [x] Denied when allowWrites=false.
  - [x] Denied when member not on allowlist.
  - [x] Succeeds for a whitelisted member in a controlled test scene.

## 5. Console & Hooks (Phase‑2 Parity)

- [x] Design a minimal `ConsoleEval` tool that:
  - [x] Takes small C# snippets and returns text/JSON result.
  - [x] Is disabled by default and gated by config + confirmation.
- [x] Expose Hook Manager basics:
  - [x] `unity://hooks` resource for listing active hooks.
  - [x] Tools for `HookAdd` / `HookRemove` guarded behind allowlist + confirmation.
- [x] Add tests that only run in a special “hook test” scene to verify basic lifecycle without depending on game content (gated by `UE_MCP_HOOK_TEST_ENABLED=1` with `hookAllowlistSignatures` containing a safe type such as `UnityEngine.GameObject`).

## 6. Config Hardening

- [x] Add MCP tool(s) around `McpConfig` (read/update) with appropriate permissions.
- [x] Auth removed; rely on bindAddress/port scoping plus allowWrites/confirm gating; docs updated.

## 7. Inspector & DX Polish

- [ ] Verify all tools and resources render cleanly in `@modelcontextprotocol/inspector` (no schema errors); per-argument `inputSchema` is now emitted from `list_tools` but still needs UI validation.
- [x] Add JSON‑RPC contract tests that exercise `tools/list`, `tools/call`, and `call_tool` for all read‑only tools (matching inspector CLI usage).
- [x] Add a small “how to connect with inspector” section to `README-mcp.md`, including:
  - [x] Example `initialize`, `list_tools`, `call_tool` flows.
  - [x] Example `read_resource` URIs for common Explorer tasks.

## 8. Documentation & AGENTS.md Sync

- [x] Extend `UnityExplorer/README-mcp.md` to reflect the final tool/resource list and example payloads.
- [x] Add `docs/unity-explorer-game-interaction.md` summarizing core UnityExplorer runtime capabilities (non-MCP).
- [x] Update `UnityExplorer/AGENTS.md` with:
  - [x] A short “MCP quick‑start” section (build + launch + inspector connect).
  - [x] Notes about guarded writes and how to safely enable them on the Test‑VM.

## 9. Additional Quality / Nice‑to‑Have TODOs

- [x] Add a tiny “smoke CLI” script (PowerShell/bash) that exercises `initialize`, `list_tools`, `GetStatus`, and `TailLogs` in one go (see `tools/Invoke-McpSmoke.ps1`; mirrors the Test-VM harness).
- [x] Add a contract test that calls `MousePick` and verifies the result shape (even if `Hit=false`).
- [x] Add a contract test that calls `TailLogs` via `call_tool` (not just `read_resource`).
- [x] Ensure all DTOs are serializable without extra JSON options (no cycles, no Unity types leaking through).
- [x] Add a simple rate‑limit in `McpSimpleHttp` (e.g., max ~32 concurrent requests) and a test that overload returns a clear error with message `"Cannot have more than X parallel requests. Please slow down."`. (Code: `UnityExplorer/src/Mcp/McpSimpleHttp.cs`; Tests: `UnityExplorer/tests/dotnet/UnityExplorer.Mcp.ContractTests/HttpContractTests.cs`.)
- [x] Standardize structured error data for common cases (`NotReady`, `NotFound`, `PermissionDenied`, `RateLimited`) using the existing JSON‑RPC error envelope (`error.code`, `error.message`, `error.data.kind`, optional `error.data.hint`) and assert this shape in tests (including tool `ok=false` payloads). (Code: `McpSimpleHttp`, `UnityWriteTools`; Tests: JSON‑RPC + tool contract tests.)
- [x] Verify `stream_events` gracefully handles client disconnects (no unbounded dictionary growth); add a test that opens and closes multiple streams.
- [x] Add logging hooks for MCP errors into the MelonLoader log (short prefix, e.g. `[MCP]`), with a test that triggers at least one intentional error and reads it back via `logs/tail`.
- [x] Add a small “version” resource or tool (e.g., `unity://status` already has version, but expose a dedicated `GetVersion` tool and test it).
- [x] Add a CI note/script to run the MCP contract tests as part of the normal UnityExplorer build pipeline (see `tools/Run-McpContractTests.ps1`).

## 10. Space Shooter IL2CPP – End‑to‑End Validation

- [x] Extend the Test‑VM PowerShell harness (`C:\codex-workspace\ue-mcp-headless\call-mcp.ps1` / `req.json`) to cover remaining read tools on Space Shooter (payloads now documented in `plans/space-shooter-test-plan.md`; update the VM script accordingly):
  - `SearchObjects` (by name and type),
  - `GetCameraInfo`,
  - `MousePick` (even if `Hit=false`), including coord-based UI picks (x/y/normalized) against `SpawnTestUi` blocks.
- [x] Add short snippets to `plans/space-shooter-test-plan.md` showing the above calls and expected JSON shapes so agents can quickly verify behavior (include `SpawnTestUi` + `MousePick` examples).
- [x] Add a minimal `stream_events` check against Space Shooter: open `stream_events`, call a tool (e.g. `GetStatus`), and confirm a `tool_result` notification is received; note any `logs` / `scenes` notifications (flow documented in `plans/space-shooter-test-plan.md`; needs live validation).
- [x] Run `UnityExplorer.Mcp.ContractTests` against the Space Shooter + MelonLoader host (document the exact steps and any required env vars / discovery overrides) and record whether all tests pass. (Release, BaseUrl `http://192.168.178.210:51477`: 45 passed, 1 skipped placeholder; host remained stable.)
- [x] Ensure no contract tests assume game‑specific content; adjust tests and docs so Space Shooter is the fully supported host for MCP contract validation (other titles are examples only).
- [x] Define 1–2 safe write scenarios on Space Shooter using `SetActive` / `SelectObject` with `AllowWrites=true` and `RequireConfirm=true`, and document them in `plans/space-shooter-test-plan.md` (also note `SetTimeScale` + `SpawnTestUi`/`MousePick` flow for UI validation).
- [ ] Track an upstream fix for the UnityExplorer dropdown Il2Cpp cast crash (UI `Dropdown` array cast) and remove the Test‑VM‑only `UeMcpHeadless.dll` workaround once a proper fix is merged and validated.

## 11. Mono / MelonLoader Support

- [x] Phase A — Build + host skeleton
  - [x] Fix ML_Mono build (currently `DefineConstants=MONO,ML`, `IncludeMcpPackages=false`, `net35`) so `build.ps1` can produce `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll` again; guard `ExplorerCore`/`OptionsPanel`/other MCP call sites when `INTEROP` is absent or introduce a stub McpHost that compiles on Mono. (Done: INTEROP-disabled stubs + MCP UI note; no listener/discovery on Mono.)
  - [x] Decide how `INTEROP` should apply to Mono (enable a minimal variant or keep it off with stubs) and ensure `McpConfig`/discovery file behavior is well-defined even if MCP is disabled. (Decision: keep INTEROP off; McpHost/Config return disabled defaults, Options panel shows disabled status.)
  - [x] Document the build command + expected output paths for Mono in the plan/todo and capture any remaining blocking errors in `build.log`. (Command: `dotnet build src/UnityExplorer.csproj -c ML_Mono` → `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll`; none blocking.)

- [ ] Phase B — Read-only MCP surface on Mono
  - [x] Select a Mono-friendly JSON/HTTP stack (e.g., `Newtonsoft.Json` + `HttpListener`/simplified TCP) that preserves the CoreCLR DTOs/error envelope; avoid heavy ASP.NET deps.
  - [x] Bring up minimal Mono MCP endpoints (`initialize`, `list_tools`, `read_resource` for status/scenes/objects/logs) and keep shapes identical to CoreCLR; document any deltas in `plans/mcp-interface-concept.md`.
  - [x] Implement real `stream_events` on Mono (matching CoreCLR streamable-http behaviour; log/selection/scene/tool_result notifications; cleanup on disconnect; identical error envelope).
  - [x] Add a Mono smoke harness (subset of contract tests) and doc how to run it.

- [ ] Phase C — Parity + tests
  - [ ] Expand Mono coverage toward CoreCLR parity (selection, MousePick, camera, guarded writes where safe) and log known gaps vs. IL2CPP/Test-VM. (MousePick world/ui and GetVersion now implemented; console/scripts + hooks resources remain disabled on Mono pending validation; guarded writes still pending.)
  - [x] Fix Mono notification broadcast compile issue (net35 has no Tasks): remove discards on void `BroadcastNotificationAsync` or reintroduce a Task-compatible wrapper.
  - [ ] Run Mono smoke/inspector against a real Mono host and record results. See `README-mcp.md` Mono Host Validation Checklist. (Blocked until a Mono game is available.)
  - [x] Add Mono-specific contract/CI entry (`tools/Run-McpMonoSmoke.ps1`); run against a Mono host when available and keep IL2CPP behavior unchanged.
