#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("List loaded scenes.")]
        public static async Task<Page<SceneDto>> ListScenes(int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 50);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var scenes = new List<SceneDto>();
                var total = SceneManager.sceneCount;
                for (int i = 0; i < total; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    scenes.Add(new SceneDto(
                        Id: $"scn:{i}",
                        Name: s.name,
                        Index: i,
                        IsLoaded: s.isLoaded,
                        RootCount: s.rootCount
                    ));
                }
                var items = scenes.Skip(off).Take(lim).ToArray();
                return new Page<SceneDto>(total, items);
            });
        }
    }
}
#endif
