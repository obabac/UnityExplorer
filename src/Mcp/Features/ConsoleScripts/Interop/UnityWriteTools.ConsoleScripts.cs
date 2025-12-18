#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityExplorer.CSConsole;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        private const int MaxConsoleScriptBytes = 256 * 1024;
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static string StripLeadingBom(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s[0] == '\uFEFF' ? s.Substring(1) : s;
        }

        [McpServerTool, Description("Write a C# console script file (guarded; validated to stay within the Scripts folder; fixed max bytes; .cs only). Pass confirm=true to bypass confirmation when required.")]
        public static object WriteConsoleScript(string path, string content, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var fullPath = ResolveConsoleScriptPath(path);
                content = StripLeadingBom(content ?? string.Empty);
                var byteCount = Utf8NoBom.GetByteCount(content);
                if (byteCount > MaxConsoleScriptBytes)
                    return ToolError("InvalidArgument", $"Content too large; max {MaxConsoleScriptBytes} bytes");

                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(fullPath, content, Utf8NoBom);
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Delete a C# console script file (guarded; validated to stay within the Scripts folder; .cs only). Pass confirm=true to bypass confirmation when required.")]
        public static object DeleteConsoleScript(string path, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var fullPath = ResolveConsoleScriptPath(path);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("NotFound");
                File.Delete(fullPath);
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Run a C# console script file (guarded; requires enableConsoleEval). Pass confirm=true to bypass confirmation when required.")]
        public static async Task<object> RunConsoleScript(string path, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval)
                return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var fullPath = ResolveConsoleScriptPath(path);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("NotFound");

                var content = ReadConsoleScriptContent(fullPath);

                string? result = null;
                await MainThread.RunAsync(async () =>
                {
                    try
                    {
                        var evaluator = new ConsoleScriptEvaluator();
                        evaluator.Initialize();
                        var compiled = evaluator.Compile(content);
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

        [McpServerTool, Description("Enable or disable the startup script (startup.cs ↔ startup.disabled.cs). Pass confirm=true to bypass confirmation when required.")]
        public static object SetStartupScriptEnabled(bool enabled, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrWhiteSpace(scriptsFolder))
                    throw new InvalidOperationException("NotReady");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");

                Directory.CreateDirectory(scriptsRoot);

                if (enabled)
                {
                    if (File.Exists(disabledPath))
                    {
                        if (File.Exists(activePath))
                            File.Delete(activePath);
                        File.Move(disabledPath, activePath);
                    }
                }
                else
                {
                    if (File.Exists(activePath))
                    {
                        if (File.Exists(disabledPath))
                            File.Delete(disabledPath);
                        File.Move(activePath, disabledPath);
                    }
                }

                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Write the startup script (startup.cs) with the provided content (guarded; max 256KB). Pass confirm=true to bypass confirmation when required.")]
        public static object WriteStartupScript(string content, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrWhiteSpace(scriptsFolder))
                    throw new InvalidOperationException("NotReady");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");

                Directory.CreateDirectory(scriptsRoot);

                content = StripLeadingBom(content ?? string.Empty);
                var byteCount = Utf8NoBom.GetByteCount(content);
                if (byteCount > MaxConsoleScriptBytes)
                    return ToolError("InvalidArgument", $"Content too large; max {MaxConsoleScriptBytes} bytes");

                File.WriteAllText(activePath, content, Utf8NoBom);
                if (File.Exists(disabledPath))
                    File.Delete(disabledPath);

                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Run the startup script (startup.cs or startup.disabled.cs if present) (guarded; requires enableConsoleEval). Pass confirm=true to bypass confirmation when required.")]
        public static async Task<object> RunStartupScript(bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval)
                return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites)
                return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm)
                return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrWhiteSpace(scriptsFolder))
                    throw new InvalidOperationException("NotReady");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");

                var targetPath = File.Exists(activePath) ? activePath : (File.Exists(disabledPath) ? disabledPath : activePath);
                if (!File.Exists(targetPath))
                    throw new InvalidOperationException("NotFound");

                var content = ReadConsoleScriptContent(targetPath);

                string? result = null;
                await MainThread.RunAsync(async () =>
                {
                    try
                    {
                        var evaluator = new ConsoleScriptEvaluator();
                        evaluator.Initialize();
                        var compiled = evaluator.Compile(content);
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

                return new { ok = true, result, path = targetPath };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private static string ReadConsoleScriptContent(string path)
        {
            var info = new FileInfo(path);
            var sizeBytes = info.Length;
            if (sizeBytes > MaxConsoleScriptBytes)
                throw new ArgumentException($"Content too large; max {MaxConsoleScriptBytes} bytes");

            string content;
            using (var fs = File.OpenRead(path))
            {
                var toRead = (int)Math.Min(sizeBytes, MaxConsoleScriptBytes);
                var bytes = new byte[toRead];
                var read = fs.Read(bytes, 0, toRead);
                content = Encoding.UTF8.GetString(bytes, 0, read);
            }

            if (!string.IsNullOrEmpty(content) && content[0] == '\uFEFF')
                content = content.Substring(1);

            return content;
        }

        private static string ResolveConsoleScriptPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("path is required", nameof(path));
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only .cs files are allowed", nameof(path));

            var scriptsFolder = ConsoleController.ScriptsFolder;
            if (string.IsNullOrWhiteSpace(scriptsFolder))
                throw new InvalidOperationException("NotReady");

            var scriptsRoot = Path.GetFullPath(scriptsFolder);
            if (!scriptsRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) && !scriptsRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                scriptsRoot += Path.DirectorySeparatorChar;

            var candidate = Path.IsPathRooted(path) ? path : Path.Combine(scriptsRoot, path);
            var full = Path.GetFullPath(candidate);
            if (!full.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("path must stay inside the Scripts folder", nameof(path));

            return full;
        }
    }
}
#endif
