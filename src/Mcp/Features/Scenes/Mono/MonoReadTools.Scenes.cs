#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public Page<SceneDto> ListScenes(int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            return MainThread.Run(() =>
            {
                var scenes = new List<SceneDto>();
                var total = SceneManager.sceneCount;
                for (int i = 0; i < total; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    scenes.Add(new SceneDto { Id = "scn:" + i, Name = s.name, Index = i, IsLoaded = s.isLoaded, RootCount = s.rootCount });
                }
                var items = scenes.Skip(off).Take(lim).ToList();
                return new Page<SceneDto>(total, items);
            });
        }
    }
}
#endif
