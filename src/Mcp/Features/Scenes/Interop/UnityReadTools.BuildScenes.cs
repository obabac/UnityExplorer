#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityExplorer.ObjectExplorer;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("List scenes from build settings (paged).")]
        public static async Task<Page<BuildSceneDto>> ListBuildScenes(int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 50);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var paths = SceneHandler.AllSceneNames ?? new List<string>();
                var total = paths.Count;
                var items = new List<BuildSceneDto>(Math.Min(lim, Math.Max(0, total - off)));

                for (int i = off; i < total && items.Count < lim; i++)
                {
                    var path = paths[i] ?? string.Empty;
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (string.IsNullOrEmpty(name))
                        name = path;
                    items.Add(new BuildSceneDto(i, name ?? string.Empty, path));
                }

                return new Page<BuildSceneDto>(total, items.ToArray());
            });
        }
    }
}
#endif
