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
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var s = SceneManager.GetSceneAt(i);
                    scenes.Add(new SceneDto { Id = "scn:" + i, Name = s.name, Index = i, IsLoaded = s.isLoaded, RootCount = s.rootCount });
                }

                var ddol = GetDontDestroyOnLoadRoots();
                scenes.Add(new SceneDto { Id = "scn:ddol", Name = "DontDestroyOnLoad", Index = -1, IsLoaded = ddol.Scene.IsValid() && ddol.Scene.isLoaded, RootCount = ddol.Roots.Count });

                var hideRoots = GetHideAndDontSaveRoots();
                scenes.Add(new SceneDto { Id = "scn:hide", Name = "HideAndDontSave", Index = -2, IsLoaded = false, RootCount = hideRoots.Count });

                var items = scenes.Skip(off).Take(lim).ToList();
                return new Page<SceneDto>(scenes.Count, items);
            });
        }
    }
}
#endif
