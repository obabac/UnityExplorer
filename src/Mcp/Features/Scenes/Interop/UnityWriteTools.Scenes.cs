#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Load a scene by name (guarded by allowWrites + confirm).")]
        public static async Task<object> LoadScene(string name, string mode = "single", bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrWhiteSpace(name))
                return ToolError("InvalidArgument", "name is required");

            LoadSceneMode loadMode;
            if (string.IsNullOrEmpty(mode) || mode.Equals("single", StringComparison.OrdinalIgnoreCase))
                loadMode = LoadSceneMode.Single;
            else if (mode.Equals("additive", StringComparison.OrdinalIgnoreCase))
                loadMode = LoadSceneMode.Additive;
            else
                return ToolError("InvalidArgument", "mode must be \"single\" or \"additive\"");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    SceneManager.LoadScene(name, loadMode);
                    await Task.CompletedTask;
                });

                var normalizedMode = loadMode == LoadSceneMode.Additive ? "additive" : "single";
                return new { ok = true, name, mode = normalizedMode };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
