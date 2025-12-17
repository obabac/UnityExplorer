#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public Page<ObjectCardDto> ListObjects(string? sceneId, string? name, string? type, bool? activeOnly, int? limit, int? offset)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            return MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;

                IEnumerable<GameObject> AllRoots()
                {
                    if (!string.IsNullOrEmpty(sceneId) && sceneId!.StartsWith("scn:"))
                    {
                        if (int.TryParse(sceneId.Substring(4), out var idx) && idx >= 0 && idx < SceneManager.sceneCount)
                            return SceneManager.GetSceneAt(idx).GetRootGameObjects();
                        return new GameObject[0];
                    }
                    var list = new List<GameObject>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        list.AddRange(SceneManager.GetSceneAt(i).GetRootGameObjects());
                    return list;
                }

                foreach (var root in AllRoots())
                {
                    foreach (var entry in Traverse(root))
                    {
                        var go = entry.GameObject;
                        var path = entry.Path;
                        if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                        if (!string.IsNullOrEmpty(name) && (go.name == null || go.name.IndexOf(name!, StringComparison.OrdinalIgnoreCase) < 0)) { total++; continue; }
                        if (!string.IsNullOrEmpty(type) && go.GetComponent(type!) == null) { total++; continue; }

                        if (total >= off && results.Count < lim)
                        {
                            int compCount = 0;
                            try { var comps = go.GetComponents<Component>(); compCount = comps != null ? comps.Length : 0; } catch { }
                            results.Add(new ObjectCardDto
                            {
                                Id = "obj:" + go.GetInstanceID(),
                                Name = go.name,
                                Path = path,
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

                return new Page<ObjectCardDto>(total, results);
            });
        }

        public ObjectCardDto GetObject(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Trim().Length == 0 || !id.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid id; expected 'obj:<instanceId>'");

            if (!int.TryParse(id.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                var path = BuildPath(go.transform);
                int compCount = 0;
                try { var comps2 = go.GetComponents<Component>(); compCount = comps2 != null ? comps2.Length : 0; } catch { }
                return new ObjectCardDto
                {
                    Id = "obj:" + go.GetInstanceID(),
                    Name = go.name,
                    Path = path,
                    Tag = SafeTag(go),
                    Layer = go.layer,
                    Active = go.activeSelf,
                    ComponentCount = compCount
                };
            });
        }
    }
}
#endif
