using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UniverseLib.Input;

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
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                var path = BuildPath(go.transform);
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
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                UnityEngine.Component[] comps;
                try { comps = go.GetComponents<UnityEngine.Component>(); }
                catch { comps = Array.Empty<UnityEngine.Component>(); }
                var total = comps.Length;
                var slice = comps.Skip(off).Take(lim);
                var list = new List<ComponentCardDto>();
                foreach (var c in slice)
                {
                    string typeName = c != null ? c.GetType().FullName : "<null>";
                    string summary = c != null ? c.ToString() : "<null>";
                    list.Add(new ComponentCardDto(typeName, summary));
                }
                var items = list.ToArray();
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
                            try { var comps3 = go.GetComponents<UnityEngine.Component>(); compCount = comps3 != null ? comps3.Length : 0; } catch { }
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

        [McpServerTool, Description("Get active camera information (name, FOV, position, rotation).")]
        public static async Task<CameraInfoDto> GetCameraInfo(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                Camera cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
                if (cam == null)
                    return new CameraInfoDto(false, "<none>", 0f, new Vector3Dto(0, 0, 0), new Vector3Dto(0, 0, 0));
                var pos = cam.transform.position; var rot = cam.transform.eulerAngles;
                return new CameraInfoDto(false, cam.name, cam.fieldOfView,
                    new Vector3Dto(pos.x, pos.y, pos.z),
                    new Vector3Dto(rot.x, rot.y, rot.z));
            });
        }

        [McpServerTool, Description("Raycast at current mouse position to pick a world object.")]
        public static async Task<PickResultDto> MousePick(string mode = "world", CancellationToken ct = default)
        {
            return await MainThread.Run(() =>
            {
                if (!string.Equals(mode, "world", StringComparison.OrdinalIgnoreCase))
                    return new PickResultDto(null, mode, false);

                Camera cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
                if (cam == null) return new PickResultDto(null, mode, false);
                Vector3 mousePos = InputManager.MousePosition;
                var ray = cam.ScreenPointToRay(mousePos);
                if (Physics.Raycast(ray, out var hit))
                {
                    var col = hit.collider;
                    if (col == null) return new PickResultDto(null, mode, false);
                    var go = col.gameObject;
                    return new PickResultDto(go != null ? $"obj:{go.GetInstanceID()}" : null, mode, go != null);
                }
                return new PickResultDto(null, mode, false);
            });
        }

        [McpServerTool, Description("Tail recent logs from the in-process buffer.")]
        public static Task<LogTailDto> TailLogs(int count = 200, CancellationToken ct = default)
            => Task.FromResult(LogBuffer.Tail(count));

        [McpServerTool, Description("Return current selection/inspected tabs (best effort).")]
        public static async Task<SelectionDto> GetSelection(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                string? active = null;
                var items = new List<string>();
                try
                {
                    if (InspectorManager.ActiveInspector?.Target is GameObject ago)
                        active = $"obj:{ago.GetInstanceID()}";
                    foreach (var ins in InspectorManager.Inspectors)
                    {
                        if (ins.Target is GameObject go)
                            items.Add($"obj:{go.GetInstanceID()}");
                    }
                }
                catch { }
                return new SelectionDto(active, items);
            });
        }
    }
#endif
}
