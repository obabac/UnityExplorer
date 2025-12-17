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
        [McpServerTool, Description("Select a GameObject in the UnityExplorer inspector (read-only impact).")]
        public static async Task<object> SelectObject(string objectId, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");

            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            SelectionDto? selection = null;
            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var go = UnityQuery.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");

                    UnityReadTools.RecordSelection(objectId);

                    await Task.CompletedTask;
                });

                selection = await UnityReadTools.GetSelection(ct).ConfigureAwait(false);
                try
                {
                    var http = McpSimpleHttp.Current;
                    if (http != null && selection != null)
                        await http.BroadcastNotificationAsync("selection", selection, ct).ConfigureAwait(false);
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
