#if INTEROP
using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal static class McpReflection
    {
        private static readonly NullabilityInfoContext Nullability = new();

        public static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
        {
            var dict = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return dict;
            if (query.StartsWith("?")) query = query.Substring(1);
            var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in pairs)
            {
                var kv = p.Split('=', 2);
                var k = Uri.UnescapeDataString(kv[0]);
                var v = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
                dict[k] = v;
            }
            return dict;
        }

        public static object[] ListTools()
        {
            var list = new System.Collections.Generic.List<object>();
            var toolTypes = new[] { typeof(UnityReadTools), typeof(UnityWriteTools) };
            foreach (var type in toolTypes)
            {
                foreach (var mi in type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!Attribute.IsDefined(mi, typeof(McpServerToolAttribute)))
                        continue;

                    var name = mi.Name;
                    var desc = mi.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
                    string? d = (desc.Length > 0) ? ((System.ComponentModel.DescriptionAttribute)desc[0]).Description : null;
                    var inputSchema = BuildInputSchema(mi);
                    list.Add(new { name, description = d, inputSchema });
                }
            }
            return list.ToArray();
        }

        public static object[] ListResources()
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

        private static JsonSchema BuildInputSchema(System.Reflection.MethodInfo mi)
        {
            var properties = new System.Collections.Generic.Dictionary<string, JsonSchemaProperty>(StringComparer.OrdinalIgnoreCase);
            var required = new System.Collections.Generic.List<string>();
            foreach (var p in mi.GetParameters())
            {
                if (p.ParameterType == typeof(System.Threading.CancellationToken))
                    continue;

                var schema = BuildParameterSchema(mi, p);
                if (schema != null)
                {
                    properties[p.Name!] = schema;
                    if (!IsOptionalParameter(p))
                        required.Add(p.Name!);
                }
            }

            return new JsonSchema
            {
                Type = "object",
                Properties = properties,
                Required = required.Count > 0 ? required.ToArray() : null,
                AdditionalProperties = false
            };
        }

        private static JsonSchemaProperty? BuildParameterSchema(System.Reflection.MethodInfo mi, System.Reflection.ParameterInfo p)
        {
            var paramType = p.ParameterType;
            var underlying = Nullable.GetUnderlyingType(paramType) ?? paramType;
            var isArray = underlying.IsArray;

            var typeName = isArray ? "array" : MapJsonType(underlying);
            object? items = null;
            if (isArray)
            {
                var elementType = underlying.GetElementType() ?? typeof(object);
                items = new { type = MapJsonType(elementType) };
            }

            string[]? enumValues = null;
            if (string.Equals(mi.Name, "MousePick", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(p.Name, "mode", StringComparison.OrdinalIgnoreCase))
            {
                enumValues = new[] { "world", "ui" };
            }

            object? defaultValue = null;
            if (p.HasDefaultValue)
                defaultValue = p.DefaultValue;

            return new JsonSchemaProperty
            {
                Type = typeName,
                Items = items,
                Enum = enumValues,
                Default = defaultValue
            };
        }

        private static bool IsOptionalParameter(System.Reflection.ParameterInfo p)
        {
            if (p.ParameterType == typeof(System.Threading.CancellationToken))
                return true;
            if (Nullable.GetUnderlyingType(p.ParameterType) != null)
                return true;
            if (p.HasDefaultValue)
                return true;
            if (!p.ParameterType.IsValueType)
            {
                var nullability = Nullability.Create(p);
                if (nullability.ReadState == NullabilityState.Nullable || nullability.WriteState == NullabilityState.Nullable)
                    return true;
            }
            return false;
        }

        private static string MapJsonType(Type type)
        {
            if (type.IsEnum) return "string";
            if (type == typeof(string)) return "string";
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type.IsArray) return "array";
            return "object";
        }

        private sealed class JsonSchema
        {
            [JsonPropertyName("type")]
            public string Type { get; init; } = "object";

            [JsonPropertyName("properties")]
            public System.Collections.Generic.Dictionary<string, JsonSchemaProperty> Properties { get; init; } = new(System.StringComparer.OrdinalIgnoreCase);

            [JsonPropertyName("required")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string[]? Required { get; init; }

            [JsonPropertyName("additionalProperties")]
            public bool AdditionalProperties { get; init; }
        }

        private sealed class JsonSchemaProperty
        {
            [JsonPropertyName("type")]
            public string Type { get; init; } = "string";

            [JsonPropertyName("items")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public object? Items { get; init; }

            [JsonPropertyName("enum")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string[]? Enum { get; init; }

            [JsonPropertyName("default")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public object? Default { get; init; }
        }

        public static async Task<object?> InvokeToolAsync(string name, JsonElement args)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("tool name required");
            var toolTypes = new[] { typeof(UnityReadTools), typeof(UnityWriteTools) };
            System.Reflection.MethodInfo? mi = null;
            foreach (var type in toolTypes)
            {
                mi = Array.Find(type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
                    m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase) &&
                         Attribute.IsDefined(m, typeof(McpServerToolAttribute)));
                if (mi != null) break;
            }
            if (mi == null) throw new InvalidOperationException($"Tool not found: {name}");

            var parameters = mi.GetParameters();
            var values = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                if (p.ParameterType == typeof(System.Threading.CancellationToken)) { values[i] = default(System.Threading.CancellationToken); continue; }
                object? val = null;
                if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(p.Name!, out var v))
                {
                    val = v.Deserialize(p.ParameterType);
                }
                else if (p.HasDefaultValue)
                {
                    val = p.DefaultValue;
                }
                values[i] = val;
            }
            var ret = mi.Invoke(null, values);
            if (ret is Task t)
            {
                await t.ConfigureAwait(false);
                var prop = t.GetType().GetProperty("Result");
                return prop != null ? prop.GetValue(t) : null;
            }
            return ret;
        }

        public static async Task<object?> ReadResourceAsync(string uri)
        {
            if (string.IsNullOrWhiteSpace(uri)) throw new ArgumentException("uri required");
            // simple router based on path segment
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) throw new ArgumentException("invalid uri");

            // Treat the host as the first path segment so that
            // both `unity://status` and `unity://scene/0/objects` map
            // to logical paths like `status` or `scene/0/objects`.
            var path = u.AbsolutePath.Trim('/');
            if (!string.IsNullOrEmpty(u.Host))
            {
                path = string.IsNullOrEmpty(path) ? u.Host : $"{u.Host}/{path}";
            }
            var query = ParseQuery(u.Query);

            if (path.Equals("status", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetStatus(default);
            if (path.Equals("scenes", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.ListScenes(TryInt(query, "limit"), TryInt(query, "offset"), default);
            if (path.StartsWith("scene/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/objects", StringComparison.OrdinalIgnoreCase))
            {
                var sceneId = path.Substring(6, path.Length - 6 - "/objects".Length);
                return await UnityReadTools.ListObjects(
                    sceneId,
                    TryString(query, "name"),
                    TryString(query, "type"),
                    TryBool(query, "activeOnly"),
                    TryInt(query, "limit"),
                    TryInt(query, "offset"),
                    default);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/children", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/children".Length);
                return await UnityReadTools.ListChildren(id, TryInt(query, "limit"), TryInt(query, "offset"), default);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/components", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/components".Length);
                return await UnityReadTools.GetComponents(id, TryInt(query, "limit"), TryInt(query, "offset"), default);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/component/members", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/component/members".Length);
                var compType = TryString(query, "type") ?? TryString(query, "componentType");
                if (string.IsNullOrWhiteSpace(compType))
                    throw new ArgumentException("Invalid params: 'type' is required.");
                return await UnityReadTools.ListComponentMembers(id, compType, TryBool(query, "includeMethods") ?? false, TryInt(query, "limit"), TryInt(query, "offset"), default);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/component/member", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length, path.Length - "object/".Length - "/component/member".Length);
                var compType = TryString(query, "type") ?? TryString(query, "componentType");
                var name = TryString(query, "name");
                if (string.IsNullOrWhiteSpace(compType) || string.IsNullOrWhiteSpace(name))
                    throw new ArgumentException("Invalid params: 'type' and 'name' are required.");
                return await UnityReadTools.ReadComponentMember(id, compType, name, default);
            }
            if (path.StartsWith("object/", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/components", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/children", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/component/members", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith("/component/member", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("object/".Length);
                return await UnityReadTools.GetObject(id, default);
            }
            if (path.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                return await UnityReadTools.SearchObjects(
                    TryString(query, "query"),
                    TryString(query, "name"),
                    TryString(query, "type"),
                    TryString(query, "path"),
                    TryBool(query, "activeOnly"),
                    TryInt(query, "limit"),
                    TryInt(query, "offset"),
                    default);
            }
            if (path.Equals("camera/active", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetCameraInfo(default);
            if (path.Equals("selection", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.GetSelection(default);
            if (path.Equals("logs/tail", StringComparison.OrdinalIgnoreCase))
                return await UnityReadTools.TailLogs(TryInt(query, "count") ?? 200, default);
            if (path.Equals("console/scripts", StringComparison.OrdinalIgnoreCase))
                return await UnityResources.ConsoleScripts(TryInt(query, "limit"), TryInt(query, "offset"), default);
            if (path.Equals("console/script", StringComparison.OrdinalIgnoreCase))
            {
                var p = TryString(query, "path");
                if (string.IsNullOrWhiteSpace(p))
                    throw new ArgumentException("path query is required", "path");
                return await UnityReadTools.ReadConsoleScript(p, default);
            }
            if (path.Equals("hooks", StringComparison.OrdinalIgnoreCase))
                return await UnityResources.Hooks(TryInt(query, "limit"), TryInt(query, "offset"), default);

            throw new NotSupportedException("resource not supported");
        }

        private static int? TryInt(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && int.TryParse(s, out var v) ? v : (int?)null;
        private static bool? TryBool(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && bool.TryParse(s, out var v) ? v : (bool?)null;
        private static string? TryString(System.Collections.Generic.IDictionary<string, string> q, string key)
            => q.TryGetValue(key, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    }
}
#endif
