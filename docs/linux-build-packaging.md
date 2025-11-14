# Linux Build Guide – Full Packaging (Release Zips)

This guide explains how to reproduce the upstream Windows-style release packages on Linux, including ILRepack and zipped artifacts.

> Goal: run `build.ps1` on Linux and produce release `.zip` files under `Release/`, suitable for direct distribution or installation on Windows.

## 1. Prerequisites

On the Linux machine, the **human operator** should install:

- `.NET SDK` 6.0 or newer  
  - Check with: `dotnet --version`
- `git`
- `PowerShell` (pwsh) – to run `build.ps1`
- `Mono` – to execute the Windows ILRepack binary (`lib/ILRepack.exe`)
- `zip` / `unzip` utilities (or equivalent); `build.ps1` uses PowerShell `compress-archive`, but having `zip` is generally useful.

Agents must **not** run system package managers directly; instead, they should document commands (e.g., `sudo apt-get install dotnet-sdk-6.0 powershell mono-complete` on Ubuntu) for the user to execute.

## 2. Clone and restore

```bash
git clone https://github.com/yukieiji/UnityExplorer.git
cd UnityExplorer/UnityExplorer

dotnet restore src/UnityExplorer.csproj
dotnet restore ../UniverseLib/src/UniverseLib.csproj
```

The second restore is usually optional because UniverseLib has minimal external dependencies, but it is good practice.

## 3. Running the PowerShell build script

The root `build.ps1` drives:

- `UniverseLib` builds (via its own `build.ps1`)
- UnityExplorer solution builds for multiple configurations
- ILRepack runs to merge support assemblies into single DLLs
- Cleanup and layout of `Mods` / `UserLibs` folders
- Zipping into release archives in `Release/`

On Linux, run it via PowerShell Core:

```bash
cd UnityExplorer
pwsh ./build.ps1
```

### 3.1 ILRepack on Linux

`build.ps1` assumes `lib/ILRepack.exe` is directly executable. On Linux, it is a Windows/.NET PE file, so use Mono to execute it.

If the script fails with an execution error on `lib/ILRepack.exe`, update the calls in `build.ps1` to:

```powershell
mono lib/ILRepack.exe /target:library ...
```

instead of:

```powershell
lib/ILRepack.exe /target:library ...
```

Make this change consistently for each ILRepack invocation block.

### 3.2 Path separators

`build.ps1` uses Windows-style backslashes (`\`) in paths. PowerShell Core on Linux generally accepts these, but if you encounter path resolution issues:

- Normalize paths to use `/` (forward slashes), or
- Use `Join-Path` and other PowerShell path utilities for robustness.

Keep edits minimal and focused on cross-platform compatibility.

## 4. Expected outputs

After a successful run, you should see release archives such as:

- `Release/UnityExplorer.BepInEx.IL2CPP.CoreCLR.zip`
- `Release/UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR.zip`
- `Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR.zip`
- Additional zips for non-CoreCLR and Mono targets if those builds are enabled in the script.

Each zip contains:

- A `Mods/` folder with the primary UnityExplorer DLL for that loader.
- A `UserLibs/` folder (for MelonLoader) or appropriate layout for BepInEx.
- Supporting libraries (UniverseLib, etc.) already arranged as expected by the loader.

These archives should be directly usable on **Windows** following the installation instructions in `README.md`.

## 5. Testing Linux-packaged releases on Windows

1. Copy a generated `.zip` from `Release/` to a Windows machine.
2. Extract it.
3. Follow the relevant section of `README.md` (BepInEx / MelonLoader / Standalone) to install.
4. Launch a Unity game and verify that:
   - UnityExplorer UI appears.
   - MCP server (for CoreCLR targets) starts and writes its discovery file.

If the release works as expected, your Linux-built packages are functionally equivalent to Windows-built ones.

## 6. Notes and maintenance tips for agents

- When adjusting `build.ps1`, keep changes cross-platform where possible:
  - Use `mono` for `ILRepack.exe` when not on Windows.
  - Prefer path helpers (`Join-Path`) over hard-coded separators.
- Do not remove or rename existing configurations without updating:
  - `src/UnityExplorer.csproj`
  - `src/UnityExplorer.sln`
  - Any CI workflows (if present outside this repo clone)
- If new build configurations are added in the future, extend this document with the extra build and packaging steps.

