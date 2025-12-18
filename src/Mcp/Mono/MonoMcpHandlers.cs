#if MONO && !INTEROP
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityExplorer.CSConsole;
using UnityExplorer.Hooks;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        internal sealed class McpError : Exception
        {
            public int Code { get; }
            public int HttpStatus { get; }
            public string Kind { get; }
            public string? Hint { get; }
            public string? Detail { get; }

            public McpError(int code, int httpStatus, string kind, string message, string? hint = null, string? detail = null)
                : base(message)
            {
                Code = code;
                HttpStatus = httpStatus;
                Kind = kind;
                Hint = hint;
                Detail = detail;
            }
        }

        private readonly MonoReadTools _tools = new MonoReadTools();
        private readonly MonoWriteTools _write;

        public MonoMcpHandlers()
        {
            _write = new MonoWriteTools(_tools);
        }

        public object BuildInitializeResult()
        {
            var protocolVersion = "2024-11-05";
            var serverInfo = new
            {
                name = "UnityExplorer.Mcp.Mono",
                version = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0"
            };
            var capabilities = new
            {
                tools = new { listChanged = true },
                resources = new { listChanged = true },
                experimental = new { streamEvents = new { } }

            };
            var instructions = "Unity Explorer MCP (Mono) exposes status, scenes, objects, selection, logs, camera, mouse pick, and component inspector (list/read members) over streamable-http. Guarded writes (SetActive, SetMember, ConsoleEval, AddComponent, RemoveComponent, HookAdd, HookRemove, Reparent, DestroyObject, SelectObject, SetTimeScale, SpawnTestUi, DestroyTestUi) are available when allowWrites=true (requireConfirm recommended; use SpawnTestUi blocks as safe targets and keep the component/hook allowlists configured). stream_events provides log/scene/selection/tool_result notifications.";
            return new { protocolVersion, capabilities, serverInfo, instructions };
        }

        public object[] ListTools()
        {
            var list = new List<object>();
            AddTools_Config(list);
            AddTools_Status(list);
            AddTools_Scenes(list);
            AddTools_Objects(list);
            AddTools_Components(list);
            AddTools_Version(list);
            AddTools_Search(list);
            AddTools_Camera(list);
            AddTools_MousePick(list);
            AddTools_Logs(list);
            AddTools_Selection(list);
            AddTools_ConsoleScripts(list);
            AddTools_ConsoleEval(list);
            AddTools_Hooks(list);
            AddTools_TimeScale(list);
            AddTools_TestUi(list);
            return list.ToArray();
        }

        public object[] ListResources()
        {
            static object Resource(string uri, string name, string description)
                => new { uri, name, description, mimeType = "application/json" };

            return new object[]
            {
                Resource("unity://status", "Status", "Status snapshot resource."),
                Resource("unity://scenes", "Scenes", "List scenes resource."),
                Resource("unity://scene/{sceneId}/objects", "Scene objects", "List objects under a scene (paged)."),
                Resource("unity://object/{id}", "Object detail", "Object details by id."),
                Resource("unity://object/{id}/components", "Object components", "Components for object id (paged)."),
                Resource("unity://object/{id}/component/members", "Component members", "List component members for a component (paged)."),
                Resource("unity://object/{id}/component/member", "Component member", "Read a component member value (safe, bounded)."),
                Resource("unity://object/{id}/children", "Object children", "Direct children for object id (paged)."),
                Resource("unity://search", "Search objects", "Search objects across scenes."),
                Resource("unity://camera/active", "Active camera", "Active camera info."),
                Resource("unity://selection", "Selection", "Current selection / inspected tabs."),
                Resource("unity://logs/tail", "Log tail", "Tail recent MCP log buffer."),
                Resource("unity://console/scripts", "Console scripts", "List C# console scripts (from the Scripts folder)."),
                Resource("unity://console/script?path={path}", "Console script", "Read a single C# console script by path (validated; .cs only)."),
                Resource("unity://hooks", "Hooks", "List active method hooks."),
            };
        }

        public object CallTool(string name, JObject? args)
        {
            var key = (name ?? string.Empty).ToLowerInvariant();
            try { LogBuffer.Add("debug", "call_tool:" + key, "mcp"); } catch { }

            if (TryCallTool_Config(key, args, out var result)) return result;
            if (TryCallTool_Status(key, args, out result)) return result;
            if (TryCallTool_Scenes(key, args, out result)) return result;
            if (TryCallTool_Objects(key, args, out result)) return result;
            if (TryCallTool_Components(key, args, out result)) return result;
            if (TryCallTool_Version(key, args, out result)) return result;
            if (TryCallTool_Search(key, args, out result)) return result;
            if (TryCallTool_Camera(key, args, out result)) return result;
            if (TryCallTool_MousePick(key, args, out result)) return result;
            if (TryCallTool_Logs(key, args, out result)) return result;
            if (TryCallTool_Selection(key, args, out result)) return result;
            if (TryCallTool_ConsoleScripts(key, args, out result)) return result;
            if (TryCallTool_ConsoleEval(key, args, out result)) return result;
            if (TryCallTool_Hooks(key, args, out result)) return result;
            if (TryCallTool_TimeScale(key, args, out result)) return result;
            if (TryCallTool_TestUi(key, args, out result)) return result;

            throw new McpError(-32004, 404, "NotFound", "Tool not found: " + name);
        }

        public object ReadResource(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                throw new McpError(-32602, 400, "InvalidArgument", "uri required");

            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
                throw new McpError(-32602, 400, "InvalidArgument", "invalid uri");

            var path = u.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(u.Host))
                path = string.IsNullOrEmpty(path) ? u.Host : $"{u.Host}/{path}";
            var query = ParseQuery(u.Query);

            if (path.Equals("status", StringComparison.OrdinalIgnoreCase)) return _tools.GetStatus();
            if (path.Equals("scenes", StringComparison.OrdinalIgnoreCase)) return _tools.ListScenes(TryInt(query, "limit"), TryInt(query, "offset"));
            if (path.StartsWith("scene/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/objects", StringComparison.OrdinalIgnoreCase))
            {
                var sceneId = path.Substring(6, path.Length - 6 - "/objects".Length);
                return _tools.ListObjects(sceneId, TryString(query, "name"), TryString(query, "type"), TryBool(query, "activeOnly"), TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/children", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/children".Length);
                return _tools.ListChildren(id, TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/components".Length);
                return _tools.GetComponents(id, TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/component/members", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/component/members".Length);
                var compType = TryString(query, "type") ?? TryString(query, "componentType");
                if (IsNullOrWhiteSpace(compType))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'type' is required.");
                return _tools.ListComponentMembers(id, compType, TryBool(query, "includeMethods") ?? false, TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/component/member", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/component/member".Length);
                var compType = TryString(query, "type") ?? TryString(query, "componentType");
                var name = TryString(query, "name");
                if (IsNullOrWhiteSpace(compType) || IsNullOrWhiteSpace(name))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'type' and 'name' are required.");
                return _tools.ReadComponentMember(id, compType, name);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/components", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/children", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/component/members", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/component/member", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length);
                return _tools.GetObject(id);
            }
            if (path.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                return _tools.SearchObjects(TryString(query, "query"), TryString(query, "name"), TryString(query, "type"), TryString(query, "path"), TryBool(query, "activeOnly"), TryInt(query, "limit"), TryInt(query, "offset"));
            }
            if (path.Equals("camera/active", StringComparison.OrdinalIgnoreCase)) return _tools.GetCameraInfo();
            if (path.Equals("selection", StringComparison.OrdinalIgnoreCase)) return _tools.GetSelection();
            if (path.Equals("logs/tail", StringComparison.OrdinalIgnoreCase)) return _tools.TailLogs(TryInt(query, "count") ?? 200);
            if (path.Equals("console/scripts", StringComparison.OrdinalIgnoreCase))
            {
                int lim = Math.Max(1, TryInt(query, "limit") ?? 100);
                int off = Math.Max(0, TryInt(query, "offset") ?? 0);
                return MainThread.Run(() =>
                {
                    var scriptsFolder = ConsoleController.ScriptsFolder;
                    if (!Directory.Exists(scriptsFolder))
                        return new Page<ConsoleScriptDto>(0, new List<ConsoleScriptDto>());

                    var files = Directory.GetFiles(scriptsFolder, "*.cs");
                    var total = files.Length;
                    var list = new List<ConsoleScriptDto>(lim);
                    foreach (var file in files.Skip(off).Take(lim))
                    {
                        list.Add(new ConsoleScriptDto { Name = Path.GetFileName(file), Path = file });
                    }
                    return new Page<ConsoleScriptDto>(total, list);
                });
            }
            if (path.Equals("console/script", StringComparison.OrdinalIgnoreCase))
            {
                var p = TryString(query, "path");
                if (string.IsNullOrEmpty(p) || p.Trim().Length == 0)
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'path' is required.");
                return _tools.ReadConsoleScript(p);
            }
            if (path.Equals("hooks", StringComparison.OrdinalIgnoreCase))
            {
                int lim = Math.Max(1, TryInt(query, "limit") ?? 100);
                int off = Math.Max(0, TryInt(query, "offset") ?? 0);
                return MainThread.Run(() =>
                {
                    var list = new List<HookDto>(lim);
                    int total = HookList.currentHooks.Count;
                    int index = 0;
                    foreach (System.Collections.DictionaryEntry entry in HookList.currentHooks)
                    {
                        if (index++ < off) continue;
                        if (list.Count >= lim) break;
                        if (entry.Value is HookInstance hook)
                        {
                            list.Add(new HookDto { Signature = hook.TargetMethod.FullDescription(), Enabled = hook.Enabled });
                        }
                    }
                    return new Page<HookDto>(total, list);
                });
            }

            throw new McpError(-32004, 404, "NotFound", "resource not supported");
        }

        internal static Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query.Substring(1);
            var pairs = query.Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split(new[] { '=' }, 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                dict[k] = v;
            }
            return dict;
        }

        private static object Schema(Dictionary<string, object> props, string[]? required = null)
        {
            return new
            {
                type = "object",
                properties = props,
                required = required != null && required.Length > 0 ? required : new string[0],
                additionalProperties = false
            };
        }

        private static object String() => new { type = "string" };
        private static object Integer(object? defaultValue = null) => defaultValue == null ? new { type = "integer" } : new { type = "integer", @default = defaultValue };
        private static object Number(object? defaultValue = null) => defaultValue == null ? new { type = "number" } : new { type = "number", @default = defaultValue };
        private static object Bool(object? defaultValue = null) => defaultValue == null ? new { type = "boolean" } : new { type = "boolean", @default = defaultValue };

        private static int? GetInt(JObject? args, string name)
            => args != null && args[name] != null && int.TryParse(args[name]!.ToString(), out var v) ? v : (int?)null;
        private static float? GetFloat(JObject? args, string name)
            => args != null && args[name] != null && float.TryParse(args[name]!.ToString(), out var v) ? v : (float?)null;
        private static bool? GetBool(JObject? args, string name)
            => args != null && args[name] != null && bool.TryParse(args[name]!.ToString(), out var v) ? v : (bool?)null;
        private static string? GetString(JObject? args, string name)
            => args != null && args[name] != null ? args[name]!.ToString() : null;
        private static string[]? GetStringArray(JObject? args, string name)
            => args != null && args[name] is JArray arr ? arr.Select(v => v.ToString()).ToArray() : null;
        private static string RequireString(JObject? args, string name, string message)
        {
            var s = GetString(args, name);
            if (string.IsNullOrEmpty(s)) throw new McpError(-32602, 400, "InvalidArgument", message);
            return s!;
        }

        private static int? TryInt(Dictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : (int?)null;
        private static bool? TryBool(Dictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && bool.TryParse(s, out var v) ? v : (bool?)null;
        private static string? TryString(Dictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && !IsNullOrWhiteSpace(s) ? s : null;

        private static bool IsNullOrWhiteSpace(string? value)
        {
            return string.IsNullOrEmpty(value) || value.Trim().Length == 0;
        }
    }
}
#endif
