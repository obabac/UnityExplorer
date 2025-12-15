## Agent Instructions (UnityExplorer root)

This file is for agents working on this repository, especially when building on Linux but targeting Windows Unity titles.

### Build focus

- Primary build targets for MCP work are the CoreCLR IL2CPP configurations:
  - `Release_BIE_Cpp_CoreCLR`
  - `Release_BIE_Unity_Cpp_CoreCLR`
  - `Release_ML_Cpp_CoreCLR`
  - Optionally: `Release_STANDALONE_Cpp_CoreCLR`
- You do **not** need to build legacy Mono / `net35` targets unless a task explicitly requires them.

### Required tools on Linux

On a Linux development machine, assume the following tools should be present or installed by the human operator:

- `.NET SDK` 6.0 or newer (for `dotnet build`).
- `git` (for source control).
- For full release packaging and ILRepack usage:
  - `PowerShell` (pwsh) to run `build.ps1`.
  - `Mono` to run `lib/ILRepack.exe` as `mono lib/ILRepack.exe ...`.
- Common CLI tools: `zip`/`unzip` or equivalent (many distros ship these by default).

Agents must **not** assume they can install system packages themselves; instead, document the required commands and let the user run them. See the Linux build docs linked below.

### Linux build documentation

There are two Linux-oriented docs under the repo root:

- `docs/linux-build-basic.md` – building raw DLLs only (no packaging).
- `docs/linux-build-packaging.md` – reproducing the full release packaging with `build.ps1` and ILRepack.

When modifying build steps, update the relevant doc(s) and keep these instructions in sync.

### Recommended build + auto-deploy for Test-VM

- For day-to-day work, prefer the helper script  
  `build-ml-coreclr.ps1` in the `UnityExplorer` root.
- From `UnityExplorer/`, run:
  ```powershell
  pwsh ./build-ml-coreclr.ps1
  ```
- This will:
  - Build the `Release_ML_Cpp_CoreCLR` configuration, and
  - Automatically call `./tools/Update-Mod-Remote.ps1` to copy the
    freshly built mod files to the Windows Test-VM.

### Mono build/deploy gotcha (ML_Mono)

- `dotnet build src/UnityExplorer.csproj -c ML_Mono` writes `Release/UnityExplorer.MelonLoader.Mono/UnityExplorer.ML.Mono.dll`, but MelonLoader loads the mod from `Release/UnityExplorer.MelonLoader.Mono/Mods/UnityExplorer.ML.Mono.dll`.
- Keep these in sync before deploying to the Test-VM (run `pwsh ./build.ps1` which moves the DLL into `Mods/`, or copy the built DLL into `Release/UnityExplorer.MelonLoader.Mono/Mods/`).
- If you deploy a stale `Mods/` DLL, the Mono host can look like it “misses” tools (e.g., `Reparent`) even when the code exists.

### Checking MelonLoader logs on the Test-VM

- To verify that MelonLoader and UnityExplorer loaded correctly, tail the
  MelonLoader log from the repo by running (from `UnityExplorer/`):
  ```powershell
  pwsh ./tools/Get-ML-Log.ps1
  ```
- `Get-ML-Log.ps1` will:
  - Poll the Windows Test-VM over SSH until it sees UnityExplorer / MelonLoader
    startup markers in  
    `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\MelonLoader\Latest.log` (adjust if installed elsewhere),
  - Then print the full log contents (including information about loaded mods).
- You can adjust the wait time, e.g.:
  ```powershell
  pwsh ./tools/Get-ML-Log.ps1 -TimeoutSeconds 120 -PollIntervalSeconds 2
  ```
- For live debugging while the game is launching, you can stream the log:
  ```powershell
  pwsh ./tools/Get-ML-Log.ps1 -Stream
  ```
  This behaves like `tail -f`: it shows the last ~200 lines and then waits for
  new log entries until you stop it with `Ctrl+C`.

### Recommended workflow on the Test-VM (build → deploy → run → log)

- **Build + deploy** (from `UnityExplorer/`):
  ```powershell
  pwsh ./build-ml-coreclr.ps1
  ```
  This builds `Release_ML_Cpp_CoreCLR` and uses `tools/Update-Mod-Remote.ps1`
  to copy the mod files to the Test-VM.
- **Start/stop the game**:
  - Use the Windows MCP tools (PowerShell / UI automation) to kill any running  
    `SpaceShooter.exe` and then start it again, rather than doing this
    over SSH (which runs under different privileges and cannot reliably control
    the interactive session).
  - Agents working on MCP behavior should treat starting/stopping the game as
    their own responsibility and perform these steps whenever deploying a new
    UnityExplorer build or when tests depend on a fresh process.
  - For any behavior change, validate on the Test-VM in the same iteration (see `plans/unity-explorer-mcp-todo.md` → Pitfalls).
- **Inspect logs**:
  - Once the game is starting, use:
    ```powershell
    pwsh ./tools/Get-ML-Log.ps1 -Stream
    ```
    to tail the MelonLoader log in real time, or run it without `-Stream` to
    wait for UnityExplorer startup and then dump the log once.

### Test-VM: Space Shooter + UnityExplorer

- The Windows test VM runs **Space Shooter** from  
  `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\`.
- After building the `Release` MelonLoader IL2CPP CoreCLR target, you can push the
  mod files to the VM from the repo root with:
  - `cd UnityExplorer`
  - `pwsh ./tools/Update-Mod-Remote.ps1`
- This copies `./Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/*` to the VM
  into the game’s install folder (using SSH to `GPUVM@192.168.178.210`).
- To start the game on the VM, use PowerShell on Windows:
  ```powershell
  Start-Process "C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\SpaceShooter.exe"
  ```
  or launch it via the MCP Windows tools by executing the same `Start-Process`
  command through the PowerShell tool.
- MelonLoader log on the VM: `C:\codex-workspace\space-shooter-build\SpaceShooter_IL2CPP\MelonLoader\Latest.log`.

### MCP Quick‑Start (for agents)

- **Build + deploy MCP build:**
  - From repo root: `cd UnityExplorer`
  - `pwsh ./build-ml-coreclr.ps1`
- **Launch the game on the Test‑VM:**
  - Use the Windows MCP tools (PowerShell) to kill any running `Space Shooter.exe` (adjust name if different) and start it again.
- **Connect an MCP inspector:**
  - From your dev machine, run  
    `npx @modelcontextprotocol/inspector --transport http --server-url http://<TestVM-IP>:51477`
  - Use `initialize` → `notifications/initialized` → `list_tools` / `read_resource` / `stream_events`.
- **Guarded writes and config:**
  - By default `allowWrites` is `false`; do not enable this casually on shared machines.
  - To experiment with write tools on the Test‑VM, use the `SetConfig` tool to toggle `allowWrites` / `requireConfirm` (and `enableConsoleEval` / allowlists) and always reset them to safe values when you are done.
