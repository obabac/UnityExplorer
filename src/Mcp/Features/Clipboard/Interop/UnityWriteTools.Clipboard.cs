#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Set clipboard text (guarded by allowWrites + confirm).")]
        public static async Task<object> SetClipboardText(string text, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    ClipboardPanel.Copy(text ?? string.Empty);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Set clipboard to a GameObject by object id (guarded).")]
        public static async Task<object> SetClipboardObject(string objectId, bool confirm = false, CancellationToken ct = default)
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
                    ClipboardPanel.Copy(go);
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Clear the UnityExplorer clipboard (guarded).")]
        public static async Task<object> ClearClipboard(bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    ClipboardPanel.ClearClipboard();
                    await Task.CompletedTask;
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
