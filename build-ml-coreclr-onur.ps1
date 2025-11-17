<# AGENTS STOP HERE - this is not a tool for you #>

$GameDir = "/mnt/d/SteamLibrary/steamapps/common/Soulstone Survivors"

<#
    Builds only the MelonLoader IL2CPP CoreCLR configuration and copies the
    resulting DLLs into the specified game directory.

    It overwrites any existing files at the destination.

    Usage (from this folder):

        pwsh ./build-ml-coreclr.ps1 -GameDir "C:\Path\To\Game"

    Expected layout under $GameDir:

        $GameDir\Mods\
        $GameDir\UserLibs\
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "=== Building UnityExplorer (Release_ML_Cpp_CoreCLR) ==="
dotnet build src/UnityExplorer.sln -c Release_ML_Cpp_CoreCLR

$releaseRoot = Join-Path $PSScriptRoot "Release\UnityExplorer.MelonLoader.IL2CPP.CoreCLR"
$modDll     = Join-Path $releaseRoot "Mods\UnityExplorer.ML.IL2CPP.CoreCLR.dll"
$userLibDll = Join-Path $releaseRoot "UserLibs\UniverseLib.ML.IL2CPP.Interop.dll"
${mcsSource} = Join-Path $PSScriptRoot "lib\net6\mcs.dll"

if (-not (Test-Path $modDll)) {
    throw "Built mod DLL not found at '$modDll'. Did the build succeed?"
}
if (-not (Test-Path $userLibDll)) {
    throw "Built UniverseLib DLL not found at '$userLibDll'. Did the build succeed?"
}
if (-not (Test-Path ${mcsSource})) {
    throw "mcs.dll not found at '${mcsSource}'."
}

Write-Host "=== SHA256 of UnityExplorer.ML.IL2CPP.CoreCLR.dll ==="
try {
    $hash = Get-FileHash -Path $modDll -Algorithm SHA256
    Write-Host "$($hash.Hash)  $($hash.Path)"
}
catch {
    Write-Warning "Get-FileHash failed: $($_.Exception.Message)"
}

$modsTarget     = Join-Path $GameDir "Mods"
$userLibsTarget = Join-Path $GameDir "UserLibs"

Write-Host "=== Copying files ==="
Write-Host "Source Mods DLL:      $modDll"
Write-Host "Source UserLibs DLL:  $userLibDll"
Write-Host "Source mcs DLL:       ${mcsSource}"
Write-Host "Target Mods folder:   $modsTarget"
Write-Host "Target UserLibs folder: $userLibsTarget"

New-Item -ItemType Directory -Path $modsTarget -Force | Out-Null
New-Item -ItemType Directory -Path $userLibsTarget -Force | Out-Null

Copy-Item -Path $modDll -Destination (Join-Path $modsTarget "UnityExplorer.ML.IL2CPP.CoreCLR.dll") -Force
Copy-Item -Path $userLibDll -Destination (Join-Path $userLibsTarget "UniverseLib.ML.IL2CPP.Interop.dll") -Force
Copy-Item -Path ${mcsSource} -Destination (Join-Path $userLibsTarget "mcs.dll") -Force

Write-Host "Done. You can now launch the game and it will use the freshly built CoreCLR MelonLoader UnityExplorer."