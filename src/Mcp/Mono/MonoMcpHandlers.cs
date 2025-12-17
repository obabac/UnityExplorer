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
    internal sealed class MonoMcpHandlers
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
            var instructions = "Unity Explorer MCP (Mono) exposes status, scenes, objects, selection, logs, camera, and mouse pick over streamable-http. Guarded writes (SetActive, SetMember, ConsoleEval, AddComponent, RemoveComponent, HookAdd, HookRemove, Reparent, DestroyObject, SelectObject, SetTimeScale, SpawnTestUi, DestroyTestUi) are available when allowWrites=true (requireConfirm recommended; use SpawnTestUi blocks as safe targets and keep the component/hook allowlists configured). stream_events provides log/scene/selection/tool_result notifications.";
            return new { protocolVersion, capabilities, serverInfo, instructions };
        }

        public object[] ListTools()
        {
            var list = new List<object>();
            list.Add(new { name = "SetConfig", description = "Update MCP config settings and optionally restart the server.", inputSchema = Schema(new Dictionary<string, object> { { "allowWrites", Bool() }, { "requireConfirm", Bool() }, { "enableConsoleEval", Bool() }, { "componentAllowlist", new { type = "array", items = String() } }, { "reflectionAllowlistMembers", new { type = "array", items = String() } }, { "hookAllowlistSignatures", new { type = "array", items = String() } }, { "restart", Bool() } }) });
            list.Add(new { name = "GetConfig", description = "Read current MCP config (sanitized).", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "GetStatus", description = "Status snapshot of Unity Explorer.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "ListScenes", description = "List scenes (paged).", inputSchema = Schema(new Dictionary<string, object> { { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "ListObjects", description = "List objects in a scene or all scenes.", inputSchema = Schema(new Dictionary<string, object> { { "sceneId", String() }, { "name", String() }, { "type", String() }, { "activeOnly", Bool() }, { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "GetObject", description = "Get object details by id.", inputSchema = Schema(new Dictionary<string, object> { { "id", String() } }, new[] { "id" }) });
            list.Add(new { name = "GetComponents", description = "List component cards for an object.", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "objectId" }) });
            list.Add(new { name = "GetVersion", description = "Version info for Unity Explorer MCP.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SearchObjects", description = "Search objects by name/type/path.", inputSchema = Schema(new Dictionary<string, object> { { "query", String() }, { "name", String() }, { "type", String() }, { "path", String() }, { "activeOnly", Bool() }, { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "GetCameraInfo", description = "Get active camera info.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new
            {
                name = "MousePick",
                description = "Raycast at current mouse position to pick a world or UI object.",
                inputSchema = Schema(new Dictionary<string, object>
                {
                    { "mode", new { type = "string", @enum = new[] { "world", "ui" }, @default = "world" } },
                    { "x", Number() },
                    { "y", Number() },
                    { "normalized", Bool(false) }
                })
            });
            list.Add(new { name = "TailLogs", description = "Tail recent logs.", inputSchema = Schema(new Dictionary<string, object> { { "count", Integer(200) } }) });
            list.Add(new { name = "GetSelection", description = "Current selection / inspected tabs.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "ReadConsoleScript", description = "Read a C# console script file (validated to stay within the Scripts folder; fixed max bytes; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() } }, new[] { "path" }) });
            list.Add(new { name = "WriteConsoleScript", description = "Write a C# console script file (guarded; validated to stay within the Scripts folder; fixed max bytes; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "content", String() }, { "confirm", Bool(false) } }, new[] { "path", "content" }) });
            list.Add(new { name = "DeleteConsoleScript", description = "Delete a C# console script file (guarded; validated to stay within the Scripts folder; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "confirm", Bool(false) } }, new[] { "path" }) });
            list.Add(new { name = "SetActive", description = "Set GameObject active state (guarded by allowWrites/confirm).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "active", Bool() }, { "confirm", Bool(false) } }, new[] { "objectId", "active" }) });
            list.Add(new { name = "SetMember", description = "Set a field or property on a component (allowlist enforced).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "componentType", String() }, { "member", String() }, { "jsonValue", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "componentType", "member", "jsonValue" }) });
            list.Add(new { name = "ConsoleEval", description = "Evaluate a small C# snippet in the UnityExplorer console context (guarded by config).", inputSchema = Schema(new Dictionary<string, object> { { "code", String() }, { "confirm", Bool(false) } }, new[] { "code" }) });
            list.Add(new { name = "AddComponent", description = "Add a component by full type name to a GameObject (guarded by allowlist).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "type", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "type" }) });
            list.Add(new { name = "RemoveComponent", description = "Remove a component by full type name or index from a GameObject (allowlist enforced when by type).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "typeOrIndex", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "typeOrIndex" }) });
            list.Add(new { name = "HookListAllowedTypes", description = "List hook-allowed types (mirrors config hookAllowlistSignatures).", inputSchema = Schema(new Dictionary<string, object> { }) });
            list.Add(new { name = "HookListMethods", description = "List methods for a hook-allowed type (paged).", inputSchema = Schema(new Dictionary<string, object> { { "type", String() }, { "filter", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "type" }) });
            list.Add(new { name = "HookGetSource", description = "Get hook patch source by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() } }, new[] { "signature" }) });
            list.Add(new { name = "HookAdd", description = "Add a Harmony hook for the given type and method (guarded by hook allowlist).", inputSchema = Schema(new Dictionary<string, object> { { "type", String() }, { "method", String() }, { "confirm", Bool(false) } }, new[] { "type", "method" }) });
            list.Add(new { name = "HookSetEnabled", description = "Enable or disable a previously added Harmony hook by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "enabled", Bool() }, { "confirm", Bool(false) } }, new[] { "signature", "enabled" }) });
            list.Add(new { name = "HookSetSource", description = "Update the patch source for a previously added Harmony hook (requires enableConsoleEval).", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "source", String() }, { "confirm", Bool(false) } }, new[] { "signature", "source" }) });
            list.Add(new { name = "HookRemove", description = "Remove a previously added Harmony hook by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "confirm", Bool(false) } }, new[] { "signature" }) });
            list.Add(new { name = "Reparent", description = "Reparent a GameObject under a new parent (guarded; SpawnTestUi blocks recommended).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "newParentId", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "newParentId" }) });
            list.Add(new { name = "DestroyObject", description = "Destroy a GameObject (guarded; SpawnTestUi blocks recommended).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "confirm", Bool(false) } }, new[] { "objectId" }) });
            list.Add(new { name = "SelectObject", description = "Select a GameObject in the inspector (requires allowWrites).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() } }, new[] { "objectId" }) });
            list.Add(new { name = "GetTimeScale", description = "Get current time-scale (read-only).", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SetTimeScale", description = "Set Unity time-scale (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "value", Number() }, { "lock", Bool() }, { "confirm", Bool(false) } }, new[] { "value" }) });
            list.Add(new { name = "SpawnTestUi", description = "Spawn a simple UI canvas for MousePick UI validation (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }) });
            list.Add(new { name = "DestroyTestUi", description = "Destroy the test UI canvas (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }) });
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
            switch (key)
            {
                case "setconfig":
                    return _write.SetConfig(
                        GetBool(args, "allowWrites"),
                        GetBool(args, "requireConfirm"),
                        GetBool(args, "enableConsoleEval"),
                        GetStringArray(args, "componentAllowlist"),
                        GetStringArray(args, "reflectionAllowlistMembers"),
                        GetStringArray(args, "hookAllowlistSignatures"),
                        GetBool(args, "restart") ?? false);
                case "getconfig":
                    return _write.GetConfig();
                case "getstatus":
                    return _tools.GetStatus();
                case "listscenes":
                    return _tools.ListScenes(GetInt(args, "limit"), GetInt(args, "offset"));
                case "listobjects":
                    return _tools.ListObjects(GetString(args, "sceneId"), GetString(args, "name"), GetString(args, "type"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                case "getobject":
                    {
                        var id = RequireString(args, "id", "Invalid params: 'id' is required.");
                        return _tools.GetObject(id);
                    }
                case "getcomponents":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        return _tools.GetComponents(oid, GetInt(args, "limit"), GetInt(args, "offset"));
                    }
                case "getversion":
                    return _tools.GetVersion();
                case "searchobjects":
                    return _tools.SearchObjects(GetString(args, "query"), GetString(args, "name"), GetString(args, "type"), GetString(args, "path"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                case "getcamerainfo":
                    return _tools.GetCameraInfo();
                case "mousepick":
                    return _tools.MousePick(GetString(args, "mode"), GetFloat(args, "x"), GetFloat(args, "y"), GetBool(args, "normalized") ?? false);
                case "taillogs":
                    return _tools.TailLogs(GetInt(args, "count") ?? 200);
                case "getselection":
                    return _tools.GetSelection();
                case "readconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        return _tools.ReadConsoleScript(p);
                    }
                case "writeconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        var c = RequireString(args, "content", "Invalid params: 'content' is required.");
                        return _write.WriteConsoleScript(p, c, GetBool(args, "confirm") ?? false);
                    }
                case "deleteconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        return _write.DeleteConsoleScript(p, GetBool(args, "confirm") ?? false);
                    }
                case "setactive":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var active = GetBool(args, "active");
                        if (active == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'active' is required.");
                        return _write.SetActive(oid, active.Value, GetBool(args, "confirm") ?? false);
                    }
                case "setmember":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "componentType", "Invalid params: 'componentType' is required.");
                        var member = RequireString(args, "member", "Invalid params: 'member' is required.");
                        var jsonValue = RequireString(args, "jsonValue", "Invalid params: 'jsonValue' is required.");
                        return _write.SetMember(oid, type, member, jsonValue, GetBool(args, "confirm") ?? false);
                    }
                case "consoleeval":
                    {
                        var code = RequireString(args, "code", "Invalid params: 'code' is required.");
                        return _write.ConsoleEval(code, GetBool(args, "confirm") ?? false);
                    }
                case "addcomponent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        return _write.AddComponent(oid, type, GetBool(args, "confirm") ?? false);
                    }
                case "removecomponent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var typeOrIndex = RequireString(args, "typeOrIndex", "Invalid params: 'typeOrIndex' is required.");
                        return _write.RemoveComponent(oid, typeOrIndex, GetBool(args, "confirm") ?? false);
                    }
                case "hooklistallowedtypes":
                    return _tools.HookListAllowedTypes();
                case "hooklistmethods":
                    {
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        return _tools.HookListMethods(type, GetString(args, "filter"), GetInt(args, "limit"), GetInt(args, "offset"));
                    }
                case "hookgetsource":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        return _tools.HookGetSource(signature);
                    }
                case "hookadd":
                    {
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        var method = RequireString(args, "method", "Invalid params: 'method' is required.");
                        return _write.HookAdd(type, method, GetBool(args, "confirm") ?? false);
                    }
                case "hooksetenabled":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        var enabled = GetBool(args, "enabled");
                        if (enabled == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'enabled' is required.");
                        return _write.HookSetEnabled(signature, enabled.Value, GetBool(args, "confirm") ?? false);
                    }
                case "hooksetsource":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        var source = RequireString(args, "source", "Invalid params: 'source' is required.");
                        return _write.HookSetSource(signature, source, GetBool(args, "confirm") ?? false);
                    }
                case "hookremove":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        return _write.HookRemove(signature, GetBool(args, "confirm") ?? false);
                    }
                case "reparent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var pid = RequireString(args, "newParentId", "Invalid params: 'newParentId' is required.");
                        return _write.Reparent(oid, pid, GetBool(args, "confirm") ?? false);
                    }
                case "destroyobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        return _write.DestroyObject(oid, GetBool(args, "confirm") ?? false);
                    }
                case "selectobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        return _write.SelectObject(oid);
                    }
                case "gettimescale":
                    return _write.GetTimeScale();
                case "settimescale":
                    {
                        var val = GetFloat(args, "value");
                        if (val == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'value' is required.");
                        return _write.SetTimeScale(val.Value, GetBool(args, "lock"), GetBool(args, "confirm") ?? false);
                    }
                case "spawntestui":
                    return _write.SpawnTestUi(GetBool(args, "confirm") ?? false);
                case "destroytestui":
                    return _write.DestroyTestUi(GetBool(args, "confirm") ?? false);
                default:
                    throw new McpError(-32004, 404, "NotFound", "Tool not found: " + name);
            }
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
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && !path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length);
                return _tools.GetObject(id);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/components".Length);
                return _tools.GetComponents(id, TryInt(query, "limit"), TryInt(query, "offset"));
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
