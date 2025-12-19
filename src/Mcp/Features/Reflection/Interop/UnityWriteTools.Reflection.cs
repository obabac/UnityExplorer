#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        private static bool IsAllowed(string typeFullName, string member)
        {
            var cfg = McpConfig.Load();
            if (cfg.ReflectionAllowlistMembers == null || cfg.ReflectionAllowlistMembers.Length == 0) return false;
            var key = $"{typeFullName}.{member}";
            foreach (var e in cfg.ReflectionAllowlistMembers)
            {
                if (string.Equals(e, key, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private static bool TryGetComponent(GameObject go, string typeFullName, out UnityEngine.Component? comp)
        {
            comp = null;
            var comps = go.GetComponents<UnityEngine.Component>();
            foreach (var c in comps)
            {
                if (c != null && string.Equals(c.GetType().FullName, typeFullName, StringComparison.Ordinal))
                { comp = c; return true; }
            }

            var requestedType = ReflectionUtility.GetTypeByName(typeFullName);
            if (requestedType == null) return false;

            var matches = comps
                .Where(c => c != null && requestedType.IsAssignableFrom(c.GetType()))
                .ToArray();

            if (matches.Length == 1)
            {
                comp = matches[0];
                return true;
            }

            if (matches.Length > 1)
                throw new InvalidOperationException("Ambiguous component type");

            return false;
        }

        private static bool TryGetNumber(JsonElement element, out float value)
        {
            value = 0f;
            if (element.ValueKind != JsonValueKind.Number) return false;
            if (element.TryGetDouble(out var d))
            {
                value = (float)d;
                return true;
            }
            return false;
        }

        private static bool TryGetPropertyNumber(JsonElement element, string name, out float value)
        {
            value = 0f;
            if (element.ValueKind != JsonValueKind.Object) return false;
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    return TryGetNumber(prop.Value, out value);
            }
            return false;
        }

        private static bool TryParseVector(JsonElement root, int length, out float[] values)
        {
            values = Array.Empty<float>();
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() == length)
            {
                var arr = new float[length];
                var idx = 0;
                foreach (var el in root.EnumerateArray())
                {
                    if (!TryGetNumber(el, out var v)) return false;
                    arr[idx++] = v;
                }
                values = arr;
                return true;
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                var names = length switch
                {
                    2 => new[] { "x", "y" },
                    3 => new[] { "x", "y", "z" },
                    4 => new[] { "x", "y", "z", "w" },
                    _ => Array.Empty<string>()
                };
                if (names.Length != length) return false;

                var arr = new float[length];
                for (int i = 0; i < length; i++)
                {
                    if (!TryGetPropertyNumber(root, names[i], out var v)) return false;
                    arr[i] = v;
                }
                values = arr;
                return true;
            }

            return false;
        }

        private static object? TryDeserializeUnityValue(JsonElement root, Type type)
        {
            if (type.IsEnum)
            {
                if (root.ValueKind == JsonValueKind.String)
                {
                    var name = root.GetString();
                    if (!string.IsNullOrWhiteSpace(name) && Enum.TryParse(type, name, ignoreCase: true, out var enumVal))
                        return enumVal;
                }
                else if (root.ValueKind == JsonValueKind.Number && root.TryGetInt64(out var enumInt))
                {
                    return Enum.ToObject(type, enumInt);
                }
            }

            if (type == typeof(Vector2) && TryParseVector(root, 2, out var v2))
                return new Vector2(v2[0], v2[1]);

            if (type == typeof(Vector3) && TryParseVector(root, 3, out var v3))
                return new Vector3(v3[0], v3[1], v3[2]);

            if (type == typeof(Vector4) && TryParseVector(root, 4, out var v4))
                return new Vector4(v4[0], v4[1], v4[2], v4[3]);

            if (type == typeof(Quaternion) && TryParseVector(root, 4, out var q))
                return new Quaternion(q[0], q[1], q[2], q[3]);

            if (type == typeof(Color) && root.ValueKind == JsonValueKind.Object)
            {
                if (TryGetPropertyNumber(root, "r", out var r) &&
                    TryGetPropertyNumber(root, "g", out var g) &&
                    TryGetPropertyNumber(root, "b", out var b))
                {
                    var a = TryGetPropertyNumber(root, "a", out var aVal) ? aVal : 1f;
                    return new Color(r, g, b, a);
                }
            }

            return null;
        }

        private static object? DeserializeTo(string json, Type type)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var special = TryDeserializeUnityValue(root, type);
                if (special != null) return special;
            }
            catch { }

            try { return JsonSerializer.Deserialize(json, type, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch { }
            try { return Convert.ChangeType(json, type); } catch { }
            return null;
        }

        [McpServerTool, Description("Set a field or property on a component (allowlist enforced): componentType, member, jsonValue")]
        public static async Task<object> SetMember(string objectId, string componentType, string member, string jsonValue, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsAllowed(componentType, member)) return ToolError("PermissionDenied", "Denied by allowlist");
            var idStr = objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty;
            if (!int.TryParse(idStr, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    if (!TryGetComponent(go, componentType, out var comp) || comp == null)
                        throw new InvalidOperationException("Component not found");
                    var t = comp.GetType();
                    var fi = t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null)
                    {
                        var val = DeserializeTo(jsonValue, fi.FieldType);
                        fi.SetValue(comp, val);
                    }
                    else
                    {
                        var pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (pi == null || !pi.CanWrite) throw new InvalidOperationException("Member not writable");
                        var val = DeserializeTo(jsonValue, pi.PropertyType);
                        pi.SetValue(comp, val);
                    }
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Call a method on a component (allowlist enforced): componentType, method, argsJson (array)")]
        public static async Task<object> CallMethod(string objectId, string componentType, string method, string argsJson = "[]", bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsAllowed(componentType, method)) return ToolError("PermissionDenied", "Denied by allowlist");
            var idStr2 = objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty;
            if (!int.TryParse(idStr2, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                object? resultObj = null;
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    if (!TryGetComponent(go, componentType, out var comp) || comp == null)
                        throw new InvalidOperationException("Component not found");
                    var t = comp.GetType();
                    using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrEmpty(argsJson) ? "[]" : argsJson);
                    var arr = doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array ? doc.RootElement : default;
                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo? pick = null;
                    object?[]? callArgs = null;
                    foreach (var mi in methods)
                    {
                        if (!string.Equals(mi.Name, method, StringComparison.Ordinal)) continue;
                        var ps = mi.GetParameters();
                        if (arr.ValueKind == default || arr.GetArrayLength() != ps.Length) continue;
                        var tmp = new object?[ps.Length];
                        int idx = 0;
                        foreach (var p in ps)
                        {
                            var el = arr[idx];
                            var val = System.Text.Json.JsonSerializer.Deserialize(el.GetRawText(), p.ParameterType);
                            tmp[idx] = val; idx++;
                        }
                        pick = mi; callArgs = tmp; break;
                    }
                    if (pick == null) throw new InvalidOperationException("Method overload not found");
                    resultObj = pick.Invoke(comp, callArgs);
                    await Task.CompletedTask;
                });
                return new { ok = true, result = resultObj?.ToString() };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }
    }
}
#endif
