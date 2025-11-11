using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

#if INTEROP
using ModelContextProtocol.Server;
#endif

namespace UnityExplorer.Mcp
{
#if INTEROP
    [McpServerToolType]
    public static class UnityReadTools
    {
        [McpServerTool, Description("Status snapshot of Unity Explorer and Unity runtime.")]
        public static async Task<StatusDto> GetStatus(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var scenesLoaded = SceneManager.sceneCount;
                var platform = Application.platform.ToString();
                var runtime = Universe.Context.ToString();
                var selection = Array.Empty<string>(); // placeholder until selection wired
                return new StatusDto(
                    Version: "0.1.0",
                    UnityVersion: Application.unityVersion,
                    Platform: platform,
                    Runtime: runtime,
                    ExplorerVersion: ExplorerCore.VERSION,
                    Ready: true,
                    ScenesLoaded: scenesLoaded,
                    Selection: selection
                );
            });
        }

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
                    if (!string.IsNullOrEmpty(sceneId) && sceneId!.StartsWith("scn:"))
                    {
                        if (int.TryParse(sceneId.Substring(4), out var idx) && idx >= 0 && idx < SceneManager.sceneCount)
                            return SceneManager.GetSceneAt(idx).GetRootGameObjects();
                        return Array.Empty<GameObject>();
                    }
                    // all scenes
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
                        // simple type filter: by component type name
                        if (!string.IsNullOrEmpty(type) && go.GetComponent(type!) == null) { total++; continue; }

                        if (total >= off && results.Count < lim)
                        {
                            int compCount = 0;
                            try { compCount = go.GetComponents<Component>()?.Length ?? 0; } catch { }
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

        private static string SafeTag(GameObject go)
        {
            try { return go.tag; } catch { return string.Empty; }
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
                var go = UnityQuery.FindByInstanceId(iid) ?? throw new InvalidOperationException("NotFound");
                var path = BuildPath(go.transform);
                int compCount = 0;
                try { compCount = go.GetComponents<Component>()?.Length ?? 0; } catch { }
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

        [McpServerTool, Description("Get component cards for object id (paged).")]
        public static async Task<Page<ComponentCardDto>> GetComponents(string objectId, int? limit, int? offset, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                throw new ArgumentException("Invalid objectId; expected 'obj:<instanceId>'", nameof(objectId));

            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new ArgumentException("Invalid instance id", nameof(objectId));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var go = UnityQuery.FindByInstanceId(iid) ?? throw new InvalidOperationException("NotFound");
                Component[] comps;
                try { comps = go.GetComponents<Component>(); }
                catch { comps = Array.Empty<Component>(); }
                var total = comps.Length;
                var items = comps.Skip(off).Take(lim)
                    .Select(c => new ComponentCardDto(c?.GetType().FullName ?? "<null>", c?.ToString()))
                    .ToArray();
                return new Page<ComponentCardDto>(total, items);
            });
        }

        private static string BuildPath(Transform t)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names);
        }

        [McpServerTool, Description("Search objects across scenes by name/type/path.")]
        public static async Task<Page<ObjectCardDto>> SearchObjects(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;
                foreach (var root in UnityQuery.EnumerateAllRootGameObjects())
                {
                    foreach (var (go, p) in UnityQuery.Traverse(root))
                    {
                        if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                        var nm = go.name ?? string.Empty;
                        var match = true;
                        if (!string.IsNullOrEmpty(query))
                            match &= nm.Contains(query!, StringComparison.OrdinalIgnoreCase) || p.Contains(query!, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(name))
                            match &= nm.Contains(name!, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(type))
                            match &= go.GetComponent(type!) != null;
                        if (!string.IsNullOrEmpty(path))
                            match &= p.Contains(path!, StringComparison.OrdinalIgnoreCase);
                        if (!match) { total++; continue; }

                        if (total >= off && results.Count < lim)
                        {
                            int compCount = 0;
                            try { compCount = go.GetComponents<Component>()?.Length ?? 0; } catch { }
                            results.Add(new ObjectCardDto(
                                Id: $"obj:{go.GetInstanceID()}",
                                Name: nm,
                                Path: p,
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
    }
#endif
}
