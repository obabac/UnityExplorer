using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
#if INTEROP
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityExplorer.Hooks;
using UnityExplorer.UI.Panels;
using UniverseLib.Input;
using UnityExplorer.CSConsole;
#endif

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    [McpServerToolType]
    public static class UnityReadTools
    {
        private const int MaxConsoleScriptBytes = 256 * 1024;

        private static string? _fallbackSelectionActive;
        private static readonly List<string> _fallbackSelectionItems = new();

        private static bool IsHookAllowedType(string typeFullName)
        {
            var cfg = McpConfig.Load();
            var allow = cfg.HookAllowlistSignatures;
            if (allow == null || allow.Length == 0) return false;
            foreach (var entry in allow)
            {
                if (string.Equals(entry, typeFullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal static void RecordSelection(string objectId)
        {
            _fallbackSelectionActive = objectId;
            if (!_fallbackSelectionItems.Contains(objectId))
                _fallbackSelectionItems.Insert(0, objectId);
        }

        [McpServerTool, Description("Status snapshot of Unity Explorer and Unity runtime.")]
        public static async Task<StatusDto> GetStatus(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var scenesLoaded = SceneManager.sceneCount;
                var platform = Application.platform.ToString();
                var runtime = Universe.Context.ToString();
                var ready = scenesLoaded > 0;
                var (_, selectionItems) = SnapshotSelection();
                return new StatusDto(
                    Version: "0.1.0",
                    UnityVersion: Application.unityVersion,
                    Platform: platform,
                    Runtime: runtime,
                    ExplorerVersion: ExplorerCore.VERSION,
                    Ready: ready,
                    ScenesLoaded: scenesLoaded,
                    Selection: selectionItems
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
                    string typeName;
                    string summary;
                    if (c == null)
                    {
                        typeName = "<null>";
                        summary = "<null>";
                    }
                    else
                    {
                        var t = c.GetType();
                        typeName = t != null ? (t.FullName ?? "<null>") : "<null>";
                        var s = c.ToString();
                        summary = string.IsNullOrEmpty(s) ? "<null>" : s;
                    }
                    list.Add(new ComponentCardDto(typeName, summary));
                }
                var items = list.ToArray();
                return new Page<ComponentCardDto>(total, items);
            });
        }

        [McpServerTool, Description("Version information for UnityExplorer MCP and Unity runtime.")]
        public static async Task<VersionInfoDto> GetVersion(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var explorerVersion = ExplorerCore.VERSION;
                var mcpVersion = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var unityVersion = Application.unityVersion;
                var runtime = Universe.Context.ToString();
                return new VersionInfoDto(explorerVersion, mcpVersion, unityVersion, runtime);
            });
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
                var freecam = FreeCamPanel.inFreeCamMode;
                Camera? cam = null;

                if (freecam)
                {
                    if (FreeCamPanel.ourCamera != null)
                    {
                        cam = FreeCamPanel.ourCamera;
                    }
                    else if (FreeCamPanel.lastMainCamera != null)
                    {
                        cam = FreeCamPanel.lastMainCamera;
                    }
                    else if (Camera.main != null)
                    {
                        cam = Camera.main;
                    }
                }

                if (cam == null && Camera.main != null)
                    cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0)
                    cam = Camera.allCameras[0];

                if (cam == null)
                    return new CameraInfoDto(freecam, "<none>", 0f, new Vector3Dto(0, 0, 0), new Vector3Dto(0, 0, 0));

                var pos = cam.transform.position;
                var rot = cam.transform.eulerAngles;
                return new CameraInfoDto(freecam, cam.name, cam.fieldOfView,
                    new Vector3Dto(pos.x, pos.y, pos.z),
                    new Vector3Dto(rot.x, rot.y, rot.z));
            });
        }

        [McpServerTool, Description("Raycast at current mouse position to pick a world or UI object.")]
        public static async Task<PickResultDto> MousePick(string mode = "world", float? x = null, float? y = null, bool normalized = false, CancellationToken ct = default)
        {
            return await MainThread.Run(() =>
            {
                var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "world" : mode.ToLowerInvariant();

                // Choose screen position: if x/y provided, override the live mouse. Optional normalized (0..1) uses Screen.width/height.
                var pos = InputManager.MousePosition;
                if (x.HasValue || y.HasValue)
                {
                    if (normalized)
                    {
                        var nx = Mathf.Clamp01(x ?? 0f);
                        var ny = Mathf.Clamp01(y ?? 0f);
                        pos = new Vector2(nx * Screen.width, ny * Screen.height);
                    }
                    else
                    {
                        pos = new Vector2(x ?? pos.x, y ?? pos.y);
                    }
                }

                if (normalizedMode == "ui")
                {
                    var eventSystem = EventSystem.current;
                    if (eventSystem == null)
                        return new PickResultDto("ui", false, null, Array.Empty<PickHit>());

                    var pointer = new PointerEventData(eventSystem)
                    {
                        position = pos
                    };
                    var raycastResults = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                    eventSystem.RaycastAll(pointer, raycastResults);
                    var items = new List<PickHit>(raycastResults.Count);
                    for (int i = 0; i < raycastResults.Count; i++)
                    {
                        var rr = raycastResults[i];
                        var go = rr.gameObject;
                        if (go == null) continue;

                        var resolved = UnityQuery.FindByInstanceId(go.GetInstanceID());
                        if (resolved == null) continue;

                        var id = $"obj:{resolved.GetInstanceID()}";
                        var path = UnityQuery.BuildPath(resolved.transform);
                        items.Add(new PickHit(id, resolved.name, path));
                    }

                    var primaryId = items.Count > 0 ? items[0].Id : null;
                    return new PickResultDto("ui", items.Count > 0, primaryId, items);
                }

                Camera cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
                if (cam == null) return new PickResultDto("world", false, null, null);
                var ray = cam.ScreenPointToRay(pos);
                if (Physics.Raycast(ray, out var hit))
                {
                    var col = hit.collider;
                    if (col == null) return new PickResultDto("world", false, null, null);
                    var go = col.gameObject;
                    var id = go != null ? $"obj:{go.GetInstanceID()}" : null;
                    return new PickResultDto("world", go != null, id, null);
                }
                return new PickResultDto("world", false, null, null);
            });
        }

        [McpServerTool, Description("Tail recent logs from the in-process buffer.")]
        public static Task<LogTailDto> TailLogs(int count = 200, CancellationToken ct = default)
            => Task.FromResult(LogBuffer.Tail(count));

        [McpServerTool, Description("Read a C# console script file (validated to stay within the Scripts folder; fixed max bytes; .cs only).")]
        public static Task<ConsoleScriptFileDto> ReadConsoleScript(string path, CancellationToken ct = default)
        {
            return MainThread.Run(() =>
            {
                var fullPath = ResolveConsoleScriptPath(path);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("NotFound");

                var info = new FileInfo(fullPath);
                var sizeBytes = info.Length;
                var lastModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

                string content;
                bool truncated;
                using (var fs = File.OpenRead(fullPath))
                {
                    var toRead = (int)Math.Min(sizeBytes, MaxConsoleScriptBytes + 1L);
                    var bytes = new byte[toRead];
                    var read = fs.Read(bytes, 0, toRead);
                    truncated = read > MaxConsoleScriptBytes;
                    var used = truncated ? MaxConsoleScriptBytes : read;
                    content = Encoding.UTF8.GetString(bytes, 0, used);
                }

                return new ConsoleScriptFileDto(
                    Name: Path.GetFileName(fullPath),
                    Path: fullPath,
                    Content: content,
                    SizeBytes: sizeBytes,
                    LastModifiedUtc: lastModifiedUtc,
                    Truncated: truncated);
            });
        }

        [McpServerTool, Description("Return current selection/inspected tabs (best effort).")]
        public static async Task<SelectionDto> GetSelection(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var (active, items) = SnapshotSelection();
                return new SelectionDto(active, items);
            });
        }

        private static (string? ActiveId, List<string> Items) SnapshotSelection()
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
                    {
                        var id = $"obj:{go.GetInstanceID()}";
                        if (!items.Contains(id))
                            items.Add(id);
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(active) && _fallbackSelectionActive != null)
                active = _fallbackSelectionActive;
            if (items.Count == 0 && _fallbackSelectionItems.Count > 0)
                items.AddRange(_fallbackSelectionItems);
            if (active != null && !items.Contains(active))
                items.Insert(0, active);
            return (active, items);
        }

        private static string ResolveConsoleScriptPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required", nameof(path));
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only .cs files are allowed", nameof(path));

            var scriptsFolder = ConsoleController.ScriptsFolder;
            if (string.IsNullOrWhiteSpace(scriptsFolder))
                throw new InvalidOperationException("NotReady");

            var scriptsRoot = Path.GetFullPath(scriptsFolder);
            if (!scriptsRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) && !scriptsRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                scriptsRoot += Path.DirectorySeparatorChar;

            var candidate = Path.IsPathRooted(path) ? path : Path.Combine(scriptsRoot, path);
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("path must stay inside the Scripts folder", nameof(path));

            return full;
        }

        [McpServerTool, Description("List hook-allowed types (mirrors config hookAllowlistSignatures).")]
        public static object HookListAllowedTypes()
        {
            var cfg = McpConfig.Load();
            return new { ok = true, items = cfg.HookAllowlistSignatures ?? Array.Empty<string>() };
        }

        [McpServerTool, Description("List methods for a hook-allowed type (paged).")]
        public static async Task<Page<HookMethodDto>> HookListMethods(string type, string? filter, int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(type))
                    throw new ArgumentException("type is required", nameof(type));
                if (!IsHookAllowedType(type))
                    throw new InvalidOperationException("PermissionDenied");

                var t = ReflectionUtility.GetTypeByName(type);
                if (t == null) throw new InvalidOperationException("Type not found");

                var methods = t.GetMethods(ReflectionUtility.FLAGS);
                IEnumerable<System.Reflection.MethodInfo> query = methods;
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    query = query.Where(m =>
                    {
                        if (m == null) return false;
                        var sig = m.FullDescription();
                        return m.Name.Contains(filter!, StringComparison.OrdinalIgnoreCase)
                            || sig.Contains(filter!, StringComparison.OrdinalIgnoreCase);
                    });
                }

                var ordered = query
                    .Select(m => new HookMethodDto(m.Name, m.FullDescription()))
                    .OrderBy(m => m.Signature, StringComparer.Ordinal)
                    .ToList();

                var total = ordered.Count;
                var items = ordered.Skip(off).Take(lim).ToArray();
                return new Page<HookMethodDto>(total, items);
            });
        }

        [McpServerTool, Description("Get hook patch source by signature.")]
        public static async Task<object> HookGetSource(string signature, CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                if (string.IsNullOrWhiteSpace(signature))
                    throw new ArgumentException("signature is required", nameof(signature));

                if (!HookList.currentHooks.Contains(signature))
                    throw new InvalidOperationException("Hook not found");

                var hook = (HookInstance)HookList.currentHooks[signature]!;
                return new { ok = true, signature, source = hook.PatchSourceCode ?? string.Empty };
            });
        }
    }
#endif
}
