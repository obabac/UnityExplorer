#if MONO && !INTEROP
#nullable enable
using System;
using System.IO;

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
    }
}
#endif
