#if MONO && !INTEROP
#nullable enable
using System;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object SetActive(string objectId, bool active, bool confirm = false)
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
                    go.SetActive(active);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object Reparent(string objectId, string newParentId, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var childId))
                return ToolError("InvalidArgument", "Invalid child id");
            if (!int.TryParse(newParentId.StartsWith("obj:") ? newParentId.Substring(4) : string.Empty, out var parentId))
                return ToolError("InvalidArgument", "Invalid parent id");

            try
            {
                object? response = null;
                MainThread.Run(() =>
                {
                    var child = _read.FindByInstanceId(childId);
                    var parent = _read.FindByInstanceId(parentId);
                    if (child == null || parent == null) throw new InvalidOperationException("NotFound");
                    if (child == parent) throw new InvalidOperationException("InvalidArgument");
                    if (!IsTestUiObject(child) || !IsTestUiObject(parent))
                    {
                        response = ToolError("PermissionDenied", "Only test UI objects may be reparented (SpawnTestUi)");
                        return;
                    }

                    child.transform.SetParent(parent.transform, true);
                });
                if (response != null) return response;
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object DestroyObject(string objectId, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                object? response = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    if (!IsTestUiObject(go))
                    {
                        response = ToolError("PermissionDenied", "Only test UI objects may be destroyed (SpawnTestUi)");
                        return;
                    }

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
                });
                if (response != null) return response;
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
