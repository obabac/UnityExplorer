#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

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
            return false;
        }

        private static object? DeserializeTo(string json, Type type)
        {
            try { return System.Text.Json.JsonSerializer.Deserialize(json, type); }
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
