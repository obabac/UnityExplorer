using System;
using System.IO;
using System.Text.Json;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal static class McpDiscovery
    {
        internal sealed record Info(string BaseUrl, int Port);

        public static Info? TryLoad()
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
                if (!File.Exists(path)) return null;
                using var fs = File.OpenRead(path);
                using var doc = JsonDocument.Parse(fs);
                var root = doc.RootElement;
                var port = root.TryGetProperty("port", out var p) ? p.GetInt32() : 0;
                var url = root.TryGetProperty("baseUrl", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(url)) url = $"http://127.0.0.1:{port}/";
                return new Info(url!, port);
            }
            catch { return null; }
        }
    }
#endif
}
