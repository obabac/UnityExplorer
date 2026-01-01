#if INTEROP
#nullable enable
using Mono.CSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityExplorer.CSConsole;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        private static readonly string[] DefaultUsings =
        {
            "System",
            "System.Linq",
            "System.Text",
            "System.Collections",
            "System.Collections.Generic",
            "System.Reflection",
            "UnityEngine",
            "UniverseLib",
#if CPP
#if INTEROP
            "Il2CppInterop.Runtime",
            "Il2CppInterop.Runtime.Attributes",
            "Il2CppInterop.Runtime.Injection",
            "Il2CppInterop.Runtime.InteropTypes.Arrays",
#else
            "UnhollowerBaseLib",
            "UnhollowerRuntimeLib",
#endif
#endif
        };

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
                object? evalResult = null;
                string? errorKind = null;
                string? errorMessage = null;
                await MainThread.RunAsync(async () =>
                {
                    try
                    {
                        var evaluator = new ConsoleScriptEvaluator();
                        evaluator.Initialize();
                        TryApplyDefaultUsings(evaluator, ref errorKind, ref errorMessage);
                        if (errorKind == null)
                            evalResult = EvalWithOptionalTrailingExpression(evaluator, code, ref errorKind, ref errorMessage);
                    }
                    catch (Exception ex)
                    {
                        errorKind = "Internal";
                        errorMessage = ex.ToString();
                    }
                    await Task.CompletedTask;
                });

                if (errorKind != null)
                    return ToolError(errorKind, errorMessage ?? "ConsoleEval failed");

                return new { ok = true, result = evalResult?.ToString() };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private static void TryApplyDefaultUsings(ConsoleScriptEvaluator evaluator, ref string? errorKind, ref string? errorMessage)
        {
            foreach (var use in DefaultUsings)
            {
                if (string.IsNullOrWhiteSpace(use)) continue;
                if (!TryCompileChunk(evaluator, $"using {use};", out _, out var compileError))
                {
                    errorKind = "InvalidArgument";
                    errorMessage = compileError;
                    return;
                }
            }
        }

        private static object? EvalWithOptionalTrailingExpression(ConsoleScriptEvaluator evaluator, string code, ref string? errorKind, ref string? errorMessage)
        {
            if (TrySplitTrailingExpression(code, out var preamble, out var expr))
            {
                if (!string.IsNullOrWhiteSpace(preamble))
                {
                    if (!TryCompileChunk(evaluator, preamble, out var preambleMethod, out var preambleError))
                    {
                        errorKind = "InvalidArgument";
                        errorMessage = preambleError;
                        return null;
                    }
                    if (preambleMethod != null)
                    {
                        object? ignored = null;
                        preambleMethod.Invoke(ref ignored);
                    }
                }

                if (!TryCompileChunk(evaluator, expr, out var exprMethod, out var exprError))
                {
                    errorKind = "InvalidArgument";
                    errorMessage = exprError;
                    return null;
                }

                if (exprMethod != null)
                {
                    object? ret = null;
                    exprMethod.Invoke(ref ret);
                    return ret;
                }

                return null;
            }

            if (!TryCompileChunk(evaluator, code, out var compiled, out var error))
            {
                errorKind = "InvalidArgument";
                errorMessage = error;
                return null;
            }

            if (compiled != null)
            {
                object? ret = null;
                compiled.Invoke(ref ret);
                return ret;
            }

            return null;
        }

        private static bool TryCompileChunk(ConsoleScriptEvaluator evaluator, string text, out CompiledMethod? repl, out string? compileError)
        {
            repl = null;
            compileError = null;

            try
            {
                repl = evaluator.Compile(text);
                if (repl != null)
                    return true;

                compileError = TryGetCompileError(evaluator);
                return compileError == null;
            }
            catch (Exception ex)
            {
                compileError = ex.ToString();
                return false;
            }
        }

        private static string? TryGetCompileError(ConsoleScriptEvaluator evaluator)
        {
            try
            {
                var output = evaluator.ToString();
                evaluator.ClearOutput();

                if (ScriptEvaluator._reportPrinter?.ErrorsCount > 0)
                {
                    if (string.IsNullOrEmpty(output))
                        return "Unable to compile the code.";

                    var lines = output.Split('\n');
                    if (lines.Length >= 2)
                        output = lines[lines.Length - 2];

                    return $"Unable to compile the code. Evaluator's last output was:\n{output}";
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool TrySplitTrailingExpression(string code, out string preamble, out string expression)
        {
            preamble = code ?? string.Empty;
            expression = string.Empty;

            if (string.IsNullOrWhiteSpace(code))
                return false;

            var normalized = code.Replace("\r\n", "\n");
            var lines = normalized.Split('\n');

            var last = lines.Length - 1;
            while (last >= 0 && string.IsNullOrWhiteSpace(lines[last]))
                last--;
            if (last < 0)
                return false;

            var lastLine = (lines[last] ?? string.Empty).Trim();
            if (lastLine.Length == 0)
                return false;

            if (lastLine.StartsWith("using ", StringComparison.Ordinal) ||
                lastLine.StartsWith("//", StringComparison.Ordinal))
                return false;

            var exprLine = string.Empty;
            if (lastLine.StartsWith("return ", StringComparison.Ordinal) && lastLine.EndsWith(";", StringComparison.Ordinal))
            {
                exprLine = lastLine.Substring("return ".Length).Trim();
                exprLine = exprLine.TrimEnd(';').Trim();
            }
            else
            {
                if (lastLine.EndsWith(";", StringComparison.Ordinal) ||
                    lastLine.EndsWith("{", StringComparison.Ordinal) ||
                    lastLine == "}")
                    return false;

                exprLine = lastLine;
            }

            if (string.IsNullOrWhiteSpace(exprLine))
                return false;

            var sb = new StringBuilder();
            for (var i = 0; i < last; i++)
            {
                sb.AppendLine(lines[i]);
            }

            preamble = sb.ToString();
            expression = exprLine;
            return true;
        }
    }
}
#endif
