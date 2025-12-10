using System;
using System.IO;
#if INTEROP
using System.Text.Json;
#endif
#if MONO
using Newtonsoft.Json.Linq;
#endif

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
#elif MONO
    internal static class McpDiscovery
    {
        internal sealed class Info
        {
            public string BaseUrl { get; private set; }
            public int Port { get; private set; }

            public Info(string baseUrl, int port)
            {
                BaseUrl = baseUrl;
                Port = port;
            }
        }

        public static Info? TryLoad()
        {
            try
            {
                var path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
                if (!File.Exists(path)) return null;
                var json = File.ReadAllText(path);
                var root = JObject.Parse(json);
                var port = root.Value<int?>("port") ?? 0;
                var url = root.Value<string>("baseUrl") ?? string.Empty;
                if (string.IsNullOrEmpty(url)) url = "http://127.0.0.1:" + port + "/";
                return new Info(url, port);
            }
            catch
            {
                return null;
            }
        }
    }
#endif
}
