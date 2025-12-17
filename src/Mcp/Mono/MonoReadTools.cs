#if MONO && !INTEROP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityExplorer.CSConsole;
using UnityExplorer.ObjectExplorer;
using UnityExplorer.UI.Panels;
using UniverseLib.Input;
using UniverseLib.Utility;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed class MonoReadTools
    {
        private const int MaxConsoleScriptBytes = 256 * 1024;

        private string? _fallbackSelectionActive;
        private readonly List<string> _fallbackSelectionItems = new List<string>();

        internal void RecordSelection(string objectId)
        {
            if (string.IsNullOrEmpty(objectId)) return;
            _fallbackSelectionActive = objectId;
            if (!_fallbackSelectionItems.Contains(objectId))
                _fallbackSelectionItems.Insert(0, objectId);
        }

        private sealed class TraversalEntry
        {
            public GameObject GameObject { get; }
            public string Path { get; }

            public TraversalEntry(GameObject go, string path)
            {
                GameObject = go;
                Path = path;
            }
        }

        private IEnumerable<TraversalEntry> Traverse(GameObject root)
        {
            var stack = new Stack<TraversalEntry>();
            stack.Push(new TraversalEntry(root, "/" + root.name));
            while (stack.Count > 0)
            {
                var entry = stack.Pop();
                yield return entry;
                var t = entry.GameObject.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                {
                    var c = t.GetChild(i);
                    stack.Push(new TraversalEntry(c.gameObject, entry.Path + "/" + c.name));
                }
            }
        }

        internal GameObject? FindByInstanceId(int instanceId)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                foreach (var root in s.GetRootGameObjects())
                {
                    if (root.GetInstanceID() == instanceId)
                        return root;
                    foreach (var entry in Traverse(root))
                    {
                        if (entry.GameObject.GetInstanceID() == instanceId)
                            return entry.GameObject;
                    }
                }
            }

            try
            {
                foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (go != null && go.GetInstanceID() == instanceId)
                        return go;
                }
            }
            catch { }

            return null;
        }

        private string BuildPath(Transform t)
        {
            var names = new List<string>();
            var cur = t;
            while (cur != null)
            {
                names.Add(cur.name);
                cur = cur.parent;
            }
            names.Reverse();
            return "/" + string.Join("/", names.ToArray());
        }

        private sealed class SelectionSnapshot
        {
            public string? ActiveId { get; set; }
            public List<string> Items { get; set; } = new List<string>();
        }

        private SelectionSnapshot CaptureSelection()
        {
            var snap = new SelectionSnapshot();
            try
            {
                if (InspectorManager.ActiveInspector?.Target is GameObject ago)
                    snap.ActiveId = "obj:" + ago.GetInstanceID();
                foreach (var ins in InspectorManager.Inspectors)
                {
                    if (ins.Target is GameObject go)
                    {
                        var id = "obj:" + go.GetInstanceID();
                        if (!snap.Items.Contains(id))
                            snap.Items.Add(id);
                    }
                }
            }
            catch { }

            if (snap.ActiveId != null)
                RecordSelection(snap.ActiveId);

            if (snap.Items.Count == 0 && _fallbackSelectionItems.Count > 0)
            {
                foreach (var id in _fallbackSelectionItems)
                {
                    if (!snap.Items.Contains(id))
                        snap.Items.Add(id);
                }
            }

            if (string.IsNullOrEmpty(snap.ActiveId) && !string.IsNullOrEmpty(_fallbackSelectionActive))
                snap.ActiveId = _fallbackSelectionActive;

            if (snap.ActiveId != null && !snap.Items.Contains(snap.ActiveId))
                snap.Items.Insert(0, snap.ActiveId);
            return snap;
        }

        public StatusDto GetStatus()
        {
            return MainThread.Run(() =>
            {
                var scenesLoaded = SceneManager.sceneCount;
                var platform = Application.platform.ToString();
                var runtime = Universe.Context.ToString();
                var selection = CaptureSelection().Items;
                return new StatusDto
                {
                    Version = "0.1.0",
                    UnityVersion = Application.unityVersion,
                    Platform = platform,
                    Runtime = runtime,
                    ExplorerVersion = ExplorerCore.VERSION,
                    Ready = scenesLoaded > 0,
                    ScenesLoaded = scenesLoaded,
                    Selection = selection
                };
            });
        }

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

        private static string SafeTag(GameObject go)
        {
            try { return go.tag; } catch { return string.Empty; }
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

        public Page<ComponentCardDto> GetComponents(string objectId, int? limit, int? offset)
        {
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");

            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                Component[] comps;
                try { comps = go.GetComponents<Component>(); }
                catch { comps = new Component[0]; }
                var total = comps.Length;
                var list = new List<ComponentCardDto>();
                for (int i = off; i < Math.Min(total, off + lim); i++)
                {
                    var c = comps[i];
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
                    list.Add(new ComponentCardDto { Type = typeName, Summary = summary });
                }
                return new Page<ComponentCardDto>(total, list);
            });
        }

        public VersionInfoDto GetVersion()
        {
            return MainThread.Run(() =>
            {
                var explorerVersion = ExplorerCore.VERSION;
                var mcpVersion = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var unityVersion = Application.unityVersion;
                var runtime = Universe.Context.ToString();
                return new VersionInfoDto
                {
                    ExplorerVersion = explorerVersion,
                    McpVersion = mcpVersion,
                    UnityVersion = unityVersion,
                    Runtime = runtime
                };
            });
        }

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

        public CameraInfoDto GetCameraInfo()
        {
            return MainThread.Run(() =>
            {
                var freecam = FreeCamPanel.inFreeCamMode;
                Camera cam = null;

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
                    return new CameraInfoDto
                    {
                        IsFreecam = freecam,
                        Name = "<none>",
                        Fov = 0f,
                        Pos = new Vector3Dto { X = 0f, Y = 0f, Z = 0f },
                        Rot = new Vector3Dto { X = 0f, Y = 0f, Z = 0f }
                    };

                var pos = cam.transform.position;
                var rot = cam.transform.eulerAngles;
                return new CameraInfoDto
                {
                    IsFreecam = freecam,
                    Name = cam.name,
                    Fov = cam.fieldOfView,
                    Pos = new Vector3Dto { X = pos.x, Y = pos.y, Z = pos.z },
                    Rot = new Vector3Dto { X = rot.x, Y = rot.y, Z = rot.z }
                };
            });
        }

        public PickResultDto MousePick(string? mode = "world", float? x = null, float? y = null, bool normalized = false)
        {
            return MainThread.Run(() =>
            {
                var normalizedMode = string.IsNullOrEmpty(mode) ? "world" : mode.ToLowerInvariant();
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
                        return new PickResultDto
                        {
                            Mode = "ui",
                            Hit = false,
                            Id = null,
                            Items = new List<PickHit>()
                        };

                    var pointer = new PointerEventData(eventSystem)
                    {
                        position = pos
                    };
                    var raycastResults = new List<RaycastResult>();
                    eventSystem.RaycastAll(pointer, raycastResults);
                    var items = new List<PickHit>(raycastResults.Count);
                    foreach (var rr in raycastResults)
                    {
                        var go = rr.gameObject;
                        if (go == null) continue;

                        var resolved = FindByInstanceId(go.GetInstanceID());
                        if (resolved == null) continue;

                        var id = "obj:" + resolved.GetInstanceID();
                        var path = BuildPath(resolved.transform);
                        items.Add(new PickHit { Id = id, Name = resolved.name, Path = path });
                    }

                    var primaryId = items.Count > 0 ? items[0].Id : null;
                    return new PickResultDto
                    {
                        Mode = "ui",
                        Hit = items.Count > 0,
                        Id = primaryId,
                        Items = items
                    };
                }

                var cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0)
                    cam = Camera.allCameras[0];
                if (cam == null)
                    return new PickResultDto
                    {
                        Mode = "world",
                        Hit = false,
                        Id = null,
                        Items = null
                    };

                var ray = cam.ScreenPointToRay(pos);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    var go = hit.collider != null ? hit.collider.gameObject : null;
                    var id = go != null ? "obj:" + go.GetInstanceID() : null;
                    return new PickResultDto
                    {
                        Mode = "world",
                        Hit = go != null,
                        Id = id,
                        Items = null
                    };
                }

                return new PickResultDto
                {
                    Mode = "world",
                    Hit = false,
                    Id = null,
                    Items = null
                };
            });
        }

        public LogTailDto TailLogs(int count = 200)
        {
            return LogBuffer.Tail(count);
        }

        public SelectionDto GetSelection()
        {
            return MainThread.Run(() =>
            {
                var snap = CaptureSelection();
                return new SelectionDto { ActiveId = snap.ActiveId, Items = snap.Items };
            });
        }

        public ConsoleScriptFileDto ReadConsoleScript(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "path is required");
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Only .cs files are allowed");

            return MainThread.Run(() =>
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
                    throw new MonoMcpHandlers.McpError(-32001, 503, "NotReady", "Not ready");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                if (!scriptsRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) && !scriptsRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                    scriptsRoot += Path.DirectorySeparatorChar;

                var candidate = Path.IsPathRooted(path) ? path : Path.Combine(scriptsRoot, path);
                var fullPath = Path.GetFullPath(candidate);
                if (!fullPath.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase))
                    throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "path must stay inside the Scripts folder");

                if (!File.Exists(fullPath))
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

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

                return new ConsoleScriptFileDto
                {
                    Name = Path.GetFileName(fullPath),
                    Path = fullPath,
                    Content = content,
                    SizeBytes = sizeBytes,
                    LastModifiedUtc = lastModifiedUtc,
                    Truncated = truncated
                };
            });
        }
    }
}
#endif
