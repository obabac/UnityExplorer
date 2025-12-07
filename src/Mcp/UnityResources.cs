using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
#if INTEROP
using System.Threading.Tasks;
using HarmonyLib;
using UnityExplorer.CSConsole;
using UnityExplorer.Hooks;
#endif

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    [McpServerResourceType]
    public static class UnityResources
    {
        [McpServerResource, Description("Status snapshot resource.")]
        public static Task<StatusDto> Status(CancellationToken ct)
            => UnityReadTools.GetStatus(ct);

        [McpServerResource, Description("List scenes resource.")]
        public static Task<Page<SceneDto>> Scenes(int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.ListScenes(limit, offset, ct);

        [McpServerResource, Description("Object details by id.")]
        public static Task<ObjectCardDto> ObjectById(string id, CancellationToken ct)
            => UnityReadTools.GetObject(id, ct);

        [McpServerResource, Description("Components for object id (paged).")]
        public static Task<Page<ComponentCardDto>> ObjectComponents(string id, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.GetComponents(id, limit, offset, ct);

        [McpServerResource, Description("Tail recent MCP log buffer.")]
        public static Task<LogTailDto> LogsTail(int? count, CancellationToken ct)
            => Task.FromResult(LogBuffer.Tail(count ?? 200));

        [McpServerResource, Description("List objects under a scene (paged).")]
        public static Task<Page<ObjectCardDto>> SceneObjects(string sceneId, string? name, string? type, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.ListObjects(sceneId, name, type, activeOnly, limit, offset, ct);

        [McpServerResource, Description("Search objects across scenes.")]
        public static Task<Page<ObjectCardDto>> Search(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
            => UnityReadTools.SearchObjects(query, name, type, path, activeOnly, limit, offset, ct);

        [McpServerResource, Description("Active camera info.")]
        public static Task<CameraInfoDto> CameraActive(CancellationToken ct)
            => UnityReadTools.GetCameraInfo(ct);

        [McpServerResource, Description("Current selection / inspected tabs.")]
        public static Task<SelectionDto> Selection(CancellationToken ct)
            => UnityReadTools.GetSelection(ct);

        [McpServerResource, Description("List C# console scripts (from the Scripts folder).")]
        public static Task<Page<ConsoleScriptDto>> ConsoleScripts(int? limit, int? offset, CancellationToken ct)
        {
            int lim = System.Math.Max(1, limit ?? 100);
            int off = System.Math.Max(0, offset ?? 0);
            return MainThread.Run(() =>
            {
                var scriptsFolder = CSConsole.ConsoleController.ScriptsFolder;
                if (!Directory.Exists(scriptsFolder))
                    return new Page<ConsoleScriptDto>(0, System.Array.Empty<ConsoleScriptDto>());

                var files = Directory.GetFiles(scriptsFolder, "*.cs");
                var total = files.Length;
                var list = new List<ConsoleScriptDto>(lim);
                foreach (var path in files.Skip(off).Take(lim))
                {
                    var name = Path.GetFileName(path);
                    list.Add(new ConsoleScriptDto(name, path));
                }
                return new Page<ConsoleScriptDto>(total, list);
            });
        }

        [McpServerResource, Description("List active method hooks.")]
        public static Task<Page<HookDto>> Hooks(int? limit, int? offset, CancellationToken ct)
        {
            int lim = System.Math.Max(1, limit ?? 100);
            int off = System.Math.Max(0, offset ?? 0);
            return MainThread.Run(() =>
            {
                var list = new List<HookDto>(lim);
                int total = HookList.currentHooks.Count;
                int index = 0;
                foreach (DictionaryEntry entry in HookList.currentHooks)
                {
                    if (index++ < off) continue;
                    if (list.Count >= lim) break;
                    if (entry.Value is HookInstance hook)
                    {
                        var sig = hook.TargetMethod.FullDescription();
                        list.Add(new HookDto(sig, hook.Enabled));
                    }
                }
                return new Page<HookDto>(total, list);
            });
        }
    }
#endif
}
