#if MONO && !INTEROP
#nullable enable
using System;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object LoadScene(string name, string mode = "single", bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
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
                MainThread.Run(() =>
                {
                    SceneManager.LoadScene(name, loadMode);
                });

                var normalized = loadMode == LoadSceneMode.Additive ? "additive" : "single";
                return new { ok = true, name, mode = normalized };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
