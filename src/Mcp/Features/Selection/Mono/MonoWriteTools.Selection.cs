#if MONO && !INTEROP
#nullable enable
using System;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object SelectObject(string objectId)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                SelectionDto? selection = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    try { InspectorManager.Inspect(go); } catch { }
                    _read.RecordSelection(objectId);
                });

                selection = _read.GetSelection();
                try
                {
                    var http = McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("selection", selection);
                }
                catch { }

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
