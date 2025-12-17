#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityExplorer.CSConsole;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Evaluate a small C# snippet in the UnityExplorer console context (guarded by config).")]
        public static async Task<object> ConsoleEval(string code, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval)
                return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrWhiteSpace(code))
                return new { ok = true, result = string.Empty };

            try
            {
                string? result = null;
                await MainThread.RunAsync(async () =>
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
                        result = $"Error: {ex.Message}";
                    }
                    await Task.CompletedTask;
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
