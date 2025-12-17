#if MONO && !INTEROP
#nullable enable
using System;
using UnityExplorer.CSConsole;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object ConsoleEval(string code, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrEmpty(code) || code.Trim().Length == 0)
                return new { ok = true, result = string.Empty };

            try
            {
                string? result = null;
                MainThread.Run(() =>
                {
                    try
                    {
                        var evaluator = new ConsoleScriptEvaluator();
                        evaluator.Initialize();
                        var compiled = evaluator.Compile(code);
                        if (compiled != null)
                        {
                            object? ret = null;
                            compiled.Invoke(ref ret);
                            result = ret?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        result = "Error: " + ex.Message;
                    }
                });

                return new { ok = true, result };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
