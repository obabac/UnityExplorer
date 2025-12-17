#if MONO && !INTEROP
#nullable enable
using System;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object AddComponent(string objectId, string type, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (cfg.ComponentAllowlist == null || cfg.ComponentAllowlist.Length == 0)
                return ToolError("PermissionDenied", "No components are allowlisted");
            if (Array.IndexOf(cfg.ComponentAllowlist, type) < 0)
                return ToolError("PermissionDenied", "Denied by allowlist");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                var t = UniverseLib.ReflectionUtility.GetTypeByName(type);
                if (t == null || !typeof(UnityEngine.Component).IsAssignableFrom(t))
                    return ToolError("InvalidArgument", "Type not found or not a Component");

                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    go.AddComponent(t);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object RemoveComponent(string objectId, string typeOrIndex, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component target = null;
                    if (int.TryParse(typeOrIndex, out var idx))
                    {
                        var comps = go.GetComponents<UnityEngine.Component>();
                        if (idx >= 0 && idx < comps.Length) target = comps[idx];
                    }
                    else
                    {
                        var t = UniverseLib.ReflectionUtility.GetTypeByName(typeOrIndex);
                        if (t != null)
                        {
                            if (cfg.ComponentAllowlist == null || Array.IndexOf(cfg.ComponentAllowlist, t.FullName) < 0)
                                throw new InvalidOperationException("Denied by allowlist");
                            var comps = go.GetComponents<UnityEngine.Component>();
                            foreach (var c in comps)
                            {
                                if (c != null && c.GetType().FullName == t.FullName)
                                {
                                    target = c;
                                    break;
                                }
                            }
                        }
                    }

                    if (target == null) throw new InvalidOperationException("Component not found");
                    UnityEngine.Object.Destroy(target);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
