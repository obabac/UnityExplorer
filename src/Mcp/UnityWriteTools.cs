using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if INTEROP
using ModelContextProtocol.Server;
#endif

namespace UnityExplorer.Mcp
{
#if INTEROP
    [McpServerToolType]
    public static class UnityWriteTools
    {
        [McpServerTool, Description("Update MCP config settings and optionally restart the server.")]
        public static object SetConfig(bool? allowWrites = null, bool? requireConfirm = null, string? authToken = null, bool restart = false)
        {
            try
            {
                var cfg = McpConfig.Load();
                if (allowWrites.HasValue) cfg.AllowWrites = allowWrites.Value;
                if (requireConfirm.HasValue) cfg.RequireConfirm = requireConfirm.Value;
                if (authToken != null) cfg.AuthToken = authToken;
                McpConfig.Save(cfg);
                if (restart)
                {
                    Mcp.McpHost.Stop();
                    Mcp.McpHost.StartIfEnabled();
                }
                return new { ok = true };
            }
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }
        }
        [McpServerTool, Description("Set a GameObject's active state (guarded by config). Pass confirm=true to bypass confirmation when required.")]
        public static async Task<object> SetActive(string objectId, bool active, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return new { ok = false, error = "PermissionDenied: writes disabled" };
            if (cfg.RequireConfirm && !confirm)
                return new { ok = false, error = "ConfirmationRequired", hint = "resend with confirm=true" };

            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                return new { ok = false, error = "Invalid objectId; expected 'obj:<instanceId>'" };
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return new { ok = false, error = "Invalid instance id" };

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    go.SetActive(active);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = ex.Message };
            }
        }

        [McpServerTool, Description("Add a component by full type name to a GameObject (guarded by allowlist).")]
        public static async Task<object> AddComponent(string objectId, string type, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return new { ok = false, error = "PermissionDenied: writes disabled" };
            if (cfg.RequireConfirm && !confirm) return new { ok = false, error = "ConfirmationRequired", hint = "resend with confirm=true" };
            if (cfg.ComponentAllowlist == null || cfg.ComponentAllowlist.Length == 0)
                return new { ok = false, error = "No components are allowlisted" };
            if (Array.IndexOf(cfg.ComponentAllowlist, type) < 0)
                return new { ok = false, error = "Denied by allowlist" };
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                return new { ok = false, error = "Invalid objectId; expected 'obj:<instanceId>'" };
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return new { ok = false, error = "Invalid instance id" };

            try
            {
                Type? t = ReflectionUtility.GetTypeByName(type);
                if (t == null || !typeof(UnityEngine.Component).IsAssignableFrom(t))
                    return new { ok = false, error = "Type not found or not a Component" };

                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    RuntimeHelper.AddComponent<UnityEngine.Component>(go, t);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }
        }

        [McpServerTool, Description("Remove a component by full type name or index from a GameObject (allowlist enforced when by type).")]
        public static async Task<object> RemoveComponent(string objectId, string typeOrIndex, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return new { ok = false, error = "PermissionDenied: writes disabled" };
            if (cfg.RequireConfirm && !confirm) return new { ok = false, error = "ConfirmationRequired", hint = "resend with confirm=true" };
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                return new { ok = false, error = "Invalid objectId; expected 'obj:<instanceId>'" };
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return new { ok = false, error = "Invalid instance id" };

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
                            // enforce allowlist by type name if provided
                            if (cfg.ComponentAllowlist == null || Array.IndexOf(cfg.ComponentAllowlist, t.FullName) < 0)
                                throw new InvalidOperationException("Denied by allowlist");
                            // Avoid IL2CPP type parameter; match by FullName
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
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }
        }

        [McpServerTool, Description("Reparent a GameObject under a new parent (by object id).")]
        public static async Task<object> Reparent(string objectId, string newParentId, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return new { ok = false, error = "PermissionDenied: writes disabled" };
            if (cfg.RequireConfirm && !confirm) return new { ok = false, error = "ConfirmationRequired", hint = "resend with confirm=true" };
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : "", out var iid))
                return new { ok = false, error = "Invalid child id" };
            if (!int.TryParse(newParentId.StartsWith("obj:") ? newParentId.Substring(4) : "", out var pid))
                return new { ok = false, error = "Invalid parent id" };

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var child = UnityQuery.FindByInstanceId(iid);
                    var parent = UnityQuery.FindByInstanceId(pid);
                    if (child == null || parent == null) throw new InvalidOperationException("NotFound");
                    if (child == parent) throw new InvalidOperationException("Cannot parent to self");
                    child.transform.SetParent(parent.transform, true);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }
        }

        [McpServerTool, Description("Destroy a GameObject.")]
        public static async Task<object> DestroyObject(string objectId, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return new { ok = false, error = "PermissionDenied: writes disabled" };
            if (cfg.RequireConfirm && !confirm) return new { ok = false, error = "ConfirmationRequired", hint = "resend with confirm=true" };
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : "", out var iid))
                return new { ok = false, error = "Invalid id" };
            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Object.Destroy(go);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return new { ok = false, error = ex.Message }; }
        }
    }
#endif
}
