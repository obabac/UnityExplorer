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
                for (int i = 0; i < SceneManager.sceneCount; i++)
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

                var (ddolScene, ddolRoots) = GetDontDestroyOnLoadRoots();
                scenes.Add(new SceneDto(
                    Id: "scn:ddol",
                    Name: "DontDestroyOnLoad",
                    Index: -1,
                    IsLoaded: ddolScene.IsValid() && ddolScene.isLoaded,
                    RootCount: ddolRoots.Count
                ));

                var hideRoots = GetHideAndDontSaveRoots();
                scenes.Add(new SceneDto(
                    Id: "scn:hide",
                    Name: "HideAndDontSave",
                    Index: -2,
                    IsLoaded: false,
                    RootCount: hideRoots.Count
                ));

                var items = scenes.Skip(off).Take(lim).ToArray();
                return new Page<SceneDto>(scenes.Count, items);
            });
        }
    }
}
#endif
