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
    }
#endif
}
