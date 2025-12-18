#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("List objects in a scene (paged, shallow).")]
        public static async Task<Page<ObjectCardDto>> ListObjects(string? sceneId, string? name, string? type, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;

                IEnumerable<GameObject> AllRoots()
                {
                    if (!string.IsNullOrEmpty(sceneId))
                    {
                        if (sceneId == "scn:ddol")
                            return GetDontDestroyOnLoadRoots().Roots;
                        if (sceneId == "scn:hide")
                            return GetHideAndDontSaveRoots();
                        if (sceneId!.StartsWith("scn:") && int.TryParse(sceneId.Substring(4), out var idx) && idx >= 0 && idx < SceneManager.sceneCount)
                            return SceneManager.GetSceneAt(idx).GetRootGameObjects();
                        return Array.Empty<GameObject>();
                    }
                    var list = new List<GameObject>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                        list.AddRange(SceneManager.GetSceneAt(i).GetRootGameObjects());
                    return list;
                }

                foreach (var root in AllRoots())
                {
                    foreach (var (go, path) in UnityQuery.Traverse(root))
                    {
                        if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                        if (!string.IsNullOrEmpty(name) && !go.name.Contains(name!, StringComparison.OrdinalIgnoreCase)) { total++; continue; }
                        if (!string.IsNullOrEmpty(type) && go.GetComponent(type!) == null) { total++; continue; }

                        if (total >= off && results.Count < lim)
                        {
                            int compCount = 0;
                            try { var comps = go.GetComponents<UnityEngine.Component>(); compCount = comps != null ? comps.Length : 0; } catch { }
                            results.Add(new ObjectCardDto(
                                Id: $"obj:{go.GetInstanceID()}",
                                Name: go.name,
                                Path: path,
                                Tag: SafeTag(go),
                                Layer: go.layer,
                                Active: go.activeSelf,
                                ComponentCount: compCount
                            ));
                        }
                        total++;
                        if (results.Count >= lim) break;
                    }
                    if (results.Count >= lim) break;
                }

                return new Page<ObjectCardDto>(total, results);
            });
        }

        [McpServerTool, Description("Get object details by opaque id (obj:<instanceId>).")]
        public static async Task<ObjectCardDto> GetObject(string id, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("obj:"))
                throw new ArgumentException("Invalid id; expected 'obj:<instanceId>'", nameof(id));

            if (!int.TryParse(id.Substring(4), out var iid))
                throw new ArgumentException("Invalid instance id", nameof(id));

            return await MainThread.Run(() =>
            {
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                var path = UnityQuery.BuildPath(go.transform);
                int compCount = 0;
                try { var comps2 = go.GetComponents<UnityEngine.Component>(); compCount = comps2 != null ? comps2.Length : 0; } catch { }
                return new ObjectCardDto(
                    Id: $"obj:{go.GetInstanceID()}",
                    Name: go.name,
                    Path: path,
                    Tag: SafeTag(go),
                    Layer: go.layer,
                    Active: go.activeSelf,
                    ComponentCount: compCount
                );
            });
        }
    }
}
#endif
