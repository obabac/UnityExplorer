using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// ASP.NET Core transport removed for net6.0 compatibility. Custom HTTP/SSE transport will be added later.

namespace UnityExplorer.Mcp
{
    internal static class McpHost
    {
        private static readonly object _gate = new();
        private static bool _started;


        public static void StartIfEnabled()
        {
            var cfg = McpConfig.Load();
            if (!cfg.Enabled)
            {
                ExplorerCore.Log("MCP server disabled by config.");
                return;
            }

            lock (_gate)
            {
                if (_started) return;
                _started = true;
            }

            try
            {
                var http = new McpSimpleHttp(cfg.BindAddress, cfg.Port);
                http.Start();
                WriteDiscovery($"http://{cfg.BindAddress}:{http.Port}");
                ExplorerCore.Log($"MCP (SSE) listening on http://{cfg.BindAddress}:{http.Port}");
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning($"MCP simple HTTP failed: {ex.Message}");
                WriteDiscovery($"http://{cfg.BindAddress}:{(cfg.Port == 0 ? 0 : cfg.Port)}");
            }
        }

        public static void Stop()
        {
            // no-op for placeholder
        }

        private static void WriteDiscovery(string baseUrl)
        {
            try
            {
                var uri = new Uri(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");
                var port = uri.Port;
                var discovery = new
                {
                    pid = Process.GetCurrentProcess().Id,
                    port,
                    baseUrl = uri.ToString(),
                    modeHints = new[] { "streamable-http", "sse" },
                    startedAt = DateTimeOffset.UtcNow.ToString("o")
                };
                var path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
                File.WriteAllText(path, JsonSerializer.Serialize(discovery, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning($"Failed writing MCP discovery file: {ex.Message}");
            }
        }
    }
}
