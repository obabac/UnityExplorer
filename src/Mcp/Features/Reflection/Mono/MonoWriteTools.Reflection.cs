#if MONO && !INTEROP
#nullable enable
using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object SetMember(string objectId, string componentType, string member, string jsonValue, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsAllowed(componentType, member)) return ToolError("PermissionDenied", "Denied by allowlist");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component comp;
                    if (!TryGetComponent(go, componentType, out comp) || comp == null)
                        throw new InvalidOperationException("Component not found");

                    var t = comp.GetType();
                    var fi = t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null)
                    {
                        var val = DeserializeTo(jsonValue, fi.FieldType);
                        fi.SetValue(comp, val);
                        return;
                    }

                    var pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi == null || !pi.CanWrite)
                        throw new InvalidOperationException("Member not writable");
                    var valProp = DeserializeTo(jsonValue, pi.PropertyType);
                    pi.SetValue(comp, valProp, null);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object CallMethod(string objectId, string componentType, string method, string argsJson = "[]", bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsAllowed(componentType, method)) return ToolError("PermissionDenied", "Denied by allowlist");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                object? resultObj = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component comp;
                    if (!TryGetComponent(go, componentType, out comp) || comp == null)
                        throw new InvalidOperationException("Component not found");

                    var t = comp.GetType();
                    JArray arr;
                    try
                    {
                        arr = string.IsNullOrEmpty(argsJson) ? new JArray() : JArray.Parse(argsJson);
                    }
                    catch
                    {
                        throw new InvalidOperationException("InvalidArgument");
                    }

                    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo? pick = null;
                    object?[]? callArgs = null;
                    foreach (var mi in methods)
                    {
                        if (!string.Equals(mi.Name, method, StringComparison.Ordinal)) continue;
                        var ps = mi.GetParameters();
                        if (arr.Count != ps.Length) continue;
                        var tmp = new object?[ps.Length];
                        var ok = true;
                        for (int i = 0; i < ps.Length; i++)
                        {
                            try
                            {
                                tmp[i] = arr[i].ToObject(ps[i].ParameterType);
                            }
                            catch
                            {
                                ok = false;
                                break;
                            }
                        }
                        if (!ok) continue;
                        pick = mi; callArgs = tmp; break;
                    }

                    if (pick == null)
                        throw new InvalidOperationException("Method overload not found");

                    resultObj = pick.Invoke(comp, callArgs);
                });

                return new { ok = true, result = resultObj != null ? resultObj.ToString() : null };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
