#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityExplorer;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Add a component by full type name to a GameObject (guarded by allowlist).")]
        public static async Task<object> AddComponent(string objectId, string type, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (cfg.ComponentAllowlist == null || cfg.ComponentAllowlist.Length == 0)
                return ToolError("PermissionDenied", "No components are allowlisted");
            if (Array.IndexOf(cfg.ComponentAllowlist, type) < 0)
                return ToolError("PermissionDenied", "Denied by allowlist");
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                Type? t = ReflectionUtility.GetTypeByName(type);
                if (t == null || !typeof(UnityEngine.Component).IsAssignableFrom(t))
                    return ToolError("InvalidArgument", "Type not found or not a Component");

                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    RuntimeHelper.AddComponent<UnityEngine.Component>(go, t);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Remove a component by full type name or index from a GameObject (allowlist enforced when by type).")]
        public static async Task<object> RemoveComponent(string objectId, string typeOrIndex, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component? target = null;
                    if (int.TryParse(typeOrIndex, out var idx))
                    {
                        var comps = go.GetComponents<UnityEngine.Component>();
                        if (idx >= 0 && idx < comps.Length) target = comps[idx];
                    }
                    else
                    {
                        Type? t = ReflectionUtility.GetTypeByName(typeOrIndex);
                        if (t != null)
                        {
                            if (cfg.ComponentAllowlist == null || Array.IndexOf(cfg.ComponentAllowlist, t.FullName) < 0)
                                throw new InvalidOperationException("Denied by allowlist");
                            var comps = go.GetComponents<UnityEngine.Component>();
                            foreach (var c in comps)
                            {
                                if (c != null && c.GetType().FullName == t.FullName) { target = c; break; }
                            }
                        }
                    }
                    if (target == null) throw new InvalidOperationException("Component not found");
                    UnityEngine.Object.Destroy(target);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }
    }
}
#endif
