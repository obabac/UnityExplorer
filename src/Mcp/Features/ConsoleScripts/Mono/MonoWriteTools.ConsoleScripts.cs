#if MONO && !INTEROP
#nullable enable
using System;
using System.IO;
using System.Text;
using UnityExplorer.CSConsole;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object WriteConsoleScript(string path, string content, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var fullPath = ResolveConsoleScriptPath(path);
                content = StripLeadingBom(content ?? string.Empty);
                var byteCount = Utf8NoBom.GetByteCount(content);
                if (byteCount > MaxConsoleScriptBytes)
                    throw new ArgumentException("Content too large; max " + MaxConsoleScriptBytes + " bytes");

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

        public object DeleteConsoleScript(string path, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

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

        public object RunConsoleScript(string path, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var fullPath = ResolveConsoleScriptPath(path);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("NotFound");

                var content = ReadConsoleScriptContent(fullPath);
                string? result = null;
                MainThread.Run(() =>
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
                            result = ret != null ? ret.ToString() : null;
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

        public object SetStartupScriptEnabled(bool enabled, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
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

        public object WriteStartupScript(string content, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
                    throw new InvalidOperationException("NotReady");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");

                Directory.CreateDirectory(scriptsRoot);

                content = StripLeadingBom(content ?? string.Empty);
                var byteCount = Utf8NoBom.GetByteCount(content);
                if (byteCount > MaxConsoleScriptBytes)
                    throw new ArgumentException("Content too large; max " + MaxConsoleScriptBytes + " bytes");

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

        public object RunStartupScript(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
                    throw new InvalidOperationException("NotReady");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");
                var targetPath = File.Exists(activePath) ? activePath : (File.Exists(disabledPath) ? disabledPath : activePath);
                if (!File.Exists(targetPath))
                    throw new InvalidOperationException("NotFound");

                var content = ReadConsoleScriptContent(targetPath);
                string? result = null;
                MainThread.Run(() =>
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
                            result = ret != null ? ret.ToString() : null;
                        }
                    }
                    catch (Exception ex)
                    {
                        result = "Error: " + ex.Message;
                    }
                });

                return new { ok = true, result, path = targetPath };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private string ReadConsoleScriptContent(string path)
        {
            var info = new FileInfo(path);
            var sizeBytes = info.Length;
            if (sizeBytes > MaxConsoleScriptBytes)
                throw new ArgumentException("Content too large; max " + MaxConsoleScriptBytes + " bytes");

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
    }
}
#endif
