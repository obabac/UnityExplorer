#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public Page<ObjectCardDto> SearchObjects(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var entry in Traverse(root))
                        {
                            var go = entry.GameObject;
                            var p = entry.Path;
                            if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                            var nm = go.name ?? string.Empty;
                            var match = true;
                            if (!string.IsNullOrEmpty(query))
                                match &= nm.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0 || p.IndexOf(query!, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!string.IsNullOrEmpty(name))
                                match &= nm.IndexOf(name!, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!string.IsNullOrEmpty(type))
                                match &= go.GetComponent(type!) != null;
                            if (!string.IsNullOrEmpty(path))
                                match &= p.IndexOf(path!, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!match) { total++; continue; }

                            if (total >= off && results.Count < lim)
                            {
                                int compCount = 0;
                                try { var comps3 = go.GetComponents<Component>(); compCount = comps3 != null ? comps3.Length : 0; } catch { }
                                results.Add(new ObjectCardDto
                                {
                                    Id = "obj:" + go.GetInstanceID(),
                                    Name = nm,
                                    Path = p,
                                    Tag = SafeTag(go),
                                    Layer = go.layer,
                                    Active = go.activeSelf,
                                    ComponentCount = compCount
                                });
                            }
                            total++;
                            if (results.Count >= lim) break;
                        }
                        if (results.Count >= lim) break;
                    }
                    if (results.Count >= lim) break;
                }

                return new Page<ObjectCardDto>(total, results);
            });
        }
    }
}
#endif
