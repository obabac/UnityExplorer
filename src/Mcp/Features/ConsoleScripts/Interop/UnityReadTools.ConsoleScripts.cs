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
    public static partial class UnityReadTools
    {
        private const int MaxConsoleScriptBytes = 256 * 1024;

        [McpServerTool, Description("Read a C# console script file (validated to stay within the Scripts folder; fixed max bytes; .cs only).")]
        public static Task<ConsoleScriptFileDto> ReadConsoleScript(string path, CancellationToken ct = default)
        {
            return MainThread.Run(() =>
            {
                var fullPath = ResolveConsoleScriptPath(path);
                if (!File.Exists(fullPath))
                    throw new InvalidOperationException("NotFound");

                return ReadConsoleScriptInternal(fullPath);
            });
        }

        [McpServerTool, Description("Get the startup script (startup.cs or startup.disabled.cs) and enabled state.")]
        public static Task<object> GetStartupScript(CancellationToken ct = default)
        {
            return MainThread.Run<object>(() =>
            {
                var scriptsFolder = ConsoleController.ScriptsFolder;
                if (string.IsNullOrWhiteSpace(scriptsFolder))
                    throw new InvalidOperationException("NotReady");

                var scriptsRoot = Path.GetFullPath(scriptsFolder);
                var activePath = Path.Combine(scriptsRoot, "startup.cs");
                var disabledPath = Path.Combine(scriptsRoot, "startup.disabled.cs");

                var enabled = File.Exists(activePath);
                var chosenPath = enabled ? activePath : (File.Exists(disabledPath) ? disabledPath : activePath);

                ConsoleScriptFileDto? fileDto = null;
                if (File.Exists(chosenPath))
                    fileDto = ReadConsoleScriptInternal(chosenPath);

                return (object)new
                {
                    ok = true,
                    enabled,
                    path = chosenPath,
                    content = fileDto?.Content
                };
            });
        }

        private static ConsoleScriptFileDto ReadConsoleScriptInternal(string fullPath)
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

            return new ConsoleScriptFileDto(
                Name: Path.GetFileName(fullPath),
                Path: fullPath,
                Content: content,
                SizeBytes: sizeBytes,
                LastModifiedUtc: lastModifiedUtc,
                Truncated: truncated);
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
