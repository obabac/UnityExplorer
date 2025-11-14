# Linux Build Guide – Basic (DLLs only)

This guide explains how to build UnityExplorer DLLs on Linux without reproducing the full Windows-style packaging. The resulting assemblies are intended to be used on **Windows** Unity titles (BepInEx, MelonLoader, or standalone injectors).

> Goal: produce functionally equivalent DLLs to a Windows build, using only `dotnet` on Linux.

## 1. Prerequisites

Install these on your Linux machine (human operator action):

- `.NET SDK` 6.0 or newer  
  - Verify with: `dotnet --version`
- `git` for source control

Optional but recommended:

- `zip` / `unzip` for ad-hoc packaging of build outputs

> Note: Agents should **not** run system package managers directly; instead, they should document the command for the user (for example: `sudo apt-get install dotnet-sdk-6.0` on Ubuntu).

## 2. Clone the repository

From a terminal:

```bash
git clone https://github.com/yukieiji/UnityExplorer.git
cd UnityExplorer/UnityExplorer
```

(Adjust the path if your checkout layout differs.)

## 3. Restore NuGet packages

`UnityExplorer.csproj` uses `src/nuget.config` to pull packages from NuGet.org and BepInEx's feed. Run:

```bash
cd UnityExplorer    # repo root that contains src/
dotnet restore src/UnityExplorer.csproj
```

This restores all managed dependencies needed for the builds below.

## 4. Build UniverseLib (once per configuration type)

UnityExplorer relies on the sibling `UniverseLib` project. You can either let the `PreBuild` target build it automatically, or build it explicitly (recommended on Linux):

```bash
# BepInEx IL2CPP Interop (CoreCLR)
dotnet build UniverseLib/src/UniverseLib.sln -c Release_IL2CPP_Interop_BIE

# MelonLoader IL2CPP Interop (CoreCLR)
dotnet build UniverseLib/src/UniverseLib.sln -c Release_IL2CPP_Interop_ML

# Mono targets (only if you need Mono / net35 builds)
dotnet build UniverseLib/src/UniverseLib.sln -c Release_Mono
```

These commands populate `UniverseLib/Release/*` as expected.

## 5. Build UnityExplorer DLLs (CoreCLR / IL2CPP)

From the `UnityExplorer` repo root:

```bash
cd UnityExplorer

# BepInEx IL2CPP CoreCLR
dotnet build src/UnityExplorer.sln -c Release_BIE_Cpp_CoreCLR

# BepInEx Unity IL2CPP CoreCLR (recommended for BepInEx + MCP)
dotnet build src/UnityExplorer.sln -c Release_BIE_Unity_Cpp_CoreCLR

# MelonLoader IL2CPP CoreCLR
dotnet build src/UnityExplorer.sln -c Release_ML_Cpp_CoreCLR
```

Each configuration produces output under `Release/`:

- `Release/UnityExplorer.BepInEx.IL2CPP.CoreCLR/`
- `Release/UnityExplorer.BepInEx.Unity.IL2CPP.CoreCLR/`
- `Release/UnityExplorer.MelonLoader.IL2CPP.CoreCLR/`

Inside each directory, the main DLL names match the configuration, for example:

- `UnityExplorer.BIE.IL2CPP.CoreCLR.dll`
- `UnityExplorer.BIE.IL2CPP.Unity.CoreCLR.dll` (naming example)
- `UnityExplorer.ML.IL2CPP.CoreCLR.dll`

The exact names are controlled by `<AssemblyName>` entries in `src/UnityExplorer.csproj`.

## 6. Using Linux-built DLLs on Windows

To test on Windows:

1. Copy the relevant `Release/UnityExplorer.*` directory to your Windows machine.
2. For **BepInEx**:
   - Place the plugin DLLs into `BepInEx/plugins/sinai-dev-UnityExplorer` (matching the README instructions).
3. For **MelonLoader**:
   - Copy the main DLL into `Mods/` and any required support DLLs into `UserLibs/`.
4. Launch the Unity game and verify that UnityExplorer loads and the MCP integration behaves as expected.

Because the code is pure managed C# and the build is driven by `dotnet`, a DLL compiled on Linux should behave identically on Windows, as long as you use the same configuration and reference assemblies.

## 7. Notes for agents

- Do **not** modify `src/UnityExplorer.csproj` paths unless absolutely necessary; the project expects reference assemblies under `../lib`.
- If the `PreBuild` `Exec` steps cause issues on Linux (e.g., backslash path separators), prefer building `UniverseLib` explicitly (step 4) rather than editing the project file.
- Keep this document up to date if new configurations or dependencies are added.

