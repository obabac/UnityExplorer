#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using UnityExplorer.ObjectExplorer;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public Page<BuildSceneDto> ListBuildScenes(int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
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
                    items.Add(new BuildSceneDto { Index = i, Name = name ?? string.Empty, Path = path });
                }

                return new Page<BuildSceneDto>(total, items);
            });
        }
    }
}
#endif
