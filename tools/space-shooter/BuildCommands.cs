using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceShooter.EditorTools
{
    public static class BuildCommands
    {
        private const string Il2CppOutput = "C:/codex-workspace/space-shooter-build/SpaceShooter_IL2CPP/SpaceShooter.exe";
        private const string MonoOutput = "C:/codex-workspace/space-shooter-build/SpaceShooter_Mono/SpaceShooter.exe";

        public static void BuildWindows64Il2Cpp()
        {
            BuildWindows64(ScriptingImplementation.IL2CPP, Il2CppOutput);
        }

        public static void BuildWindows64Mono()
        {
            BuildWindows64(ScriptingImplementation.Mono2x, MonoOutput);
        }

        private static void BuildWindows64(ScriptingImplementation backend, string locationPathName)
        {
            var scenes = ResolveScenes();

            var outputDirectory = Path.GetDirectoryName(locationPathName);
            if (string.IsNullOrEmpty(outputDirectory))
            {
                throw new InvalidOperationException($"Invalid output path: {locationPathName}");
            }

            Directory.CreateDirectory(outputDirectory);

            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, backend);

            if (backend == ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetIl2CppCompilerConfiguration(BuildTargetGroup.Standalone, Il2CppCompilerConfiguration.Release);
            }

            ConfigureLightingForHeadless();

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };

            Debug.Log($"[BuildCommands] Building Space Shooter ({backend}) → {locationPathName}");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"Space Shooter {backend} build failed: {summary.result} ({summary.totalErrors} errors, {summary.totalWarnings} warnings). See Unity log for details.");
            }

            Debug.Log($"[BuildCommands] Build succeeded: {locationPathName} ({summary.totalSize / (1024f * 1024f):F1} MB, duration {summary.totalTime:g})");
        }

        private static string[] ResolveScenes()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
                .Select(s => s.path)
                .ToArray();

            if (enabledScenes.Length > 0)
            {
                return enabledScenes;
            }

            string[] preferred =
            {
                "Assets/Scenes/Main.unity",
                "Assets/Scenes/SampleScene.unity"
            };

            foreach (var scenePath in preferred)
            {
                if (File.Exists(scenePath))
                {
                    Debug.Log($"[BuildCommands] Using fallback scene: {scenePath}");
                    return new[] { scenePath };
                }
            }

            var scenePaths = AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (scenePaths.Length > 0)
            {
                var selected = scenePaths[0];
                Debug.Log($"[BuildCommands] Using first discovered scene: {selected}");
                return new[] { selected };
            }

            throw new InvalidOperationException("No scenes found for build (no enabled build scenes and no fallback scenes in project).");
        }

        private static void ConfigureLightingForHeadless()
        {
            if (!Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                return;
            }

            Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;
            LightmapEditorSettings.lightmapper = LightmapEditorSettings.Lightmapper.ProgressiveCPU;

            var lightingSettings = Lightmapping.lightingSettings;
            if (lightingSettings != null)
            {
                lightingSettings.denoiserTypeDirect = LightingSettings.DenoiserType.None;
                lightingSettings.denoiserTypeIndirect = LightingSettings.DenoiserType.None;
                lightingSettings.denoiserTypeAO = LightingSettings.DenoiserType.None;
            }

            Debug.Log("[BuildCommands] Headless lighting configured (ProgressiveCPU + denoiser disabled).");
        }
    }
}
