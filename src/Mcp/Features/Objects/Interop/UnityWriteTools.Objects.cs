#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Set a GameObject's active state (guarded by config). Pass confirm=true to bypass confirmation when required.")]
        public static async Task<object> SetActive(string objectId, bool active, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

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
                    go.SetActive(active);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Reparent a GameObject under a new parent (by object id).")]
        public static async Task<object> Reparent(string objectId, string newParentId, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid child id");
            if (!int.TryParse(newParentId.StartsWith("obj:") ? newParentId.Substring(4) : string.Empty, out var pid))
                return ToolError("InvalidArgument", "Invalid parent id");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var child = UnityQuery.FindByInstanceId(iid);
                    var parent = UnityQuery.FindByInstanceId(pid);
                    if (child == null || parent == null) throw new InvalidOperationException("NotFound");
                    if (child == parent) throw new InvalidOperationException("InvalidArgument");
                    child.transform.SetParent(parent.transform, true);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        [McpServerTool, Description("Destroy a GameObject.")]
        public static async Task<object> DestroyObject(string objectId, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");
            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Object.Destroy(go);
                    if (_testUiRoot == go)
                    {
                        _testUiRoot = null;
                        _testUiLeft = null;
                        _testUiRight = null;
                    }
                    else
                    {
                        if (_testUiLeft == go) _testUiLeft = null;
                        if (_testUiRight == go) _testUiRight = null;
                    }
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }
    }
}
#endif
