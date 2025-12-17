Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "=== Building UnityExplorer (Release_ML_Cpp_CoreCLR) ==="
dotnet build src/UnityExplorer.sln -c Release_ML_Cpp_CoreCLR

$releaseRoot = Join-Path $PSScriptRoot "Release\UnityExplorer.MelonLoader.IL2CPP.CoreCLR"
$modDll     = Join-Path $releaseRoot "Mods\UnityExplorer.ML.IL2CPP.CoreCLR.dll"
$userLibDll = Join-Path $releaseRoot "UserLibs\UniverseLib.ML.IL2CPP.Interop.dll"
${mcsSource} = Join-Path $PSScriptRoot "lib\net6\mcs.dll"

if (-not (Test-Path ${mcsSource})) {
    throw "mcs.dll not found at '${mcsSource}'."
}

Write-Host "=== Synchronizing release layout (Mods/UserLibs) ==="

# Ensure the folder structure exists (some builds only produce the root DLLs).
$modsDir = Join-Path $releaseRoot "Mods"
if (-not (Test-Path $modsDir)) {
    New-Item -ItemType Directory -Path $modsDir -Force | Out-Null
}
$userLibsDir = Join-Path $releaseRoot "UserLibs"
if (-not (Test-Path $userLibsDir)) {
    New-Item -ItemType Directory -Path $userLibsDir -Force | Out-Null
}

# Ensure the Mods folder contains the freshly built DLL from the release root.
$modDllRoot = Join-Path $releaseRoot "UnityExplorer.ML.IL2CPP.CoreCLR.dll"
if (Test-Path $modDllRoot) {
    Write-Host "Copying mod DLL into Mods: $modDll"
    Copy-Item -Path $modDllRoot -Destination $modDll -Force
}
else {
    Write-Warning "Expected root mod DLL not found at '$modDllRoot'."
}

# Ensure the UniverseLib DLL in UserLibs matches the one in the release root (if present).
$userLibRoot = Join-Path $releaseRoot "UniverseLib.ML.IL2CPP.Interop.dll"
if (Test-Path $userLibRoot) {
    Write-Host "Copying UniverseLib DLL into UserLibs: $userLibDll"
    Copy-Item -Path $userLibRoot -Destination $userLibDll -Force
}
else {
    Write-Warning "Expected root UniverseLib DLL not found at '$userLibRoot'."
}

if (-not (Test-Path $modDll)) {
    throw "Built mod DLL not found at '$modDll'. Did the build succeed?"
}
if (-not (Test-Path $userLibDll)) {
    throw "Built UniverseLib DLL not found at '$userLibDll'. Did the build succeed?"
}

Write-Host "=== SHA256 of UnityExplorer.ML.IL2CPP.CoreCLR.dll ==="
try {
    $hash = Get-FileHash -Path $modDll -Algorithm SHA256
    Write-Host "$($hash.Hash)  $($hash.Path)"
}
catch {
    Write-Warning "Get-FileHash failed: $($_.Exception.Message)"
}

Write-Host "=== Preparing release layout (UserLibs) ==="

# Ensure mcs.dll is present in the Release UserLibs folder so it is deployed
# to the game's UserLibs directory on the Test-VM.
$mcsTarget = Join-Path $userLibsDir "mcs.dll"
Write-Host "Copying mcs.dll into Release UserLibs: $mcsTarget"
Copy-Item -Path ${mcsSource} -Destination $mcsTarget -Force

Write-Host "=== Copying files to Test-VM ==="

./tools/Update-Mod-Remote.ps1

Write-Host "Done. You can now launch the game and it will use the freshly built CoreCLR MelonLoader UnityExplorer."
