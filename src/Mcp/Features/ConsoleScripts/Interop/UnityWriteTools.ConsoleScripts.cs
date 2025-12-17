#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Text;
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
