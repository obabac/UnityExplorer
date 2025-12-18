#if MONO && !INTEROP
#nullable enable
using System;
using System.IO;
using System.Text;
using UnityExplorer.CSConsole;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public ConsoleScriptFileDto ReadConsoleScript(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "path is required");
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Only .cs files are allowed");

            return MainThread.Run(() =>
            {
                var fullPath = ResolveConsoleScriptPath(path);
                if (!File.Exists(fullPath))
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                return ReadConsoleScriptInternal(fullPath);
            });
        }

        public object GetStartupScript()
        {
            return MainThread.Run(() =>
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
                    throw new MonoMcpHandlers.McpError(-32001, 503, "NotReady", "Not ready");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");

                var enabled = File.Exists(activePath);
                var chosenPath = enabled ? activePath : (File.Exists(disabledPath) ? disabledPath : activePath);

                ConsoleScriptFileDto? dto = null;
                if (File.Exists(chosenPath))
                    dto = ReadConsoleScriptInternal(chosenPath);

                return new
                {
                    ok = true,
                    enabled,
                    path = chosenPath,
                    content = dto?.Content
                };
            });
        }

        private ConsoleScriptFileDto ReadConsoleScriptInternal(string fullPath)
        {
            var info = new FileInfo(fullPath);
            var sizeBytes = info.Length;
            var lastModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);

            string content;
            bool truncated;
            using (var fs = File.OpenRead(fullPath))
            {
                var toRead = (int)Math.Min(sizeBytes, MaxConsoleScriptBytes + 1L);
                var bytes = new byte[toRead];
                var read = fs.Read(bytes, 0, toRead);
                truncated = read > MaxConsoleScriptBytes;
                var used = truncated ? MaxConsoleScriptBytes : read;
                content = Encoding.UTF8.GetString(bytes, 0, used);
            }

            if (!string.IsNullOrEmpty(content) && content[0] == '\uFEFF')
                content = content.Substring(1);

            return new ConsoleScriptFileDto
            {
                Name = Path.GetFileName(fullPath),
                Path = fullPath,
                Content = content,
                SizeBytes = sizeBytes,
                LastModifiedUtc = lastModifiedUtc,
                Truncated = truncated
            };
        }

        private static string ResolveConsoleScriptPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "path is required");
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Only .cs files are allowed");

            var scriptsFolder = ConsoleController.ScriptsFolder;
            if (string.IsNullOrEmpty(scriptsFolder) || scriptsFolder.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32001, 503, "NotReady", "Not ready");

            var scriptsRoot = Path.GetFullPath(scriptsFolder);
            if (!scriptsRoot.EndsWith(Path.DirectorySeparatorChar.ToString()) && !scriptsRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                scriptsRoot += Path.DirectorySeparatorChar;

            var candidate = Path.IsPathRooted(path) ? path : Path.Combine(scriptsRoot, path);
            var fullPath = Path.GetFullPath(candidate);
            if (!fullPath.StartsWith(scriptsRoot, StringComparison.OrdinalIgnoreCase))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "path must stay inside the Scripts folder");

            return fullPath;
        }
    }
}
#endif
