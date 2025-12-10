using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
#if INTEROP
using System.Text.Json;
using System.Threading.Tasks;
#endif
using System.Threading;
#if MONO
using Newtonsoft.Json;
#endif

#nullable enable

// ASP.NET Core transport removed for net6.0 compatibility. Lightweight HTTP streaming transport is implemented in McpSimpleHttp.

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal static class McpHost
    {
        private static readonly object _gate = new();
        private static bool _started;
        private static McpSimpleHttp? _http;


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
                _http = new McpSimpleHttp(cfg.BindAddress, cfg.Port);
                _http.Start();
                WriteDiscovery($"http://{cfg.BindAddress}:{_http.Port}");
                ExplorerCore.Log($"MCP (streamable-http) listening on http://{cfg.BindAddress}:{_http.Port}");
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning($"MCP simple HTTP failed: {ex.Message}");
                WriteDiscovery($"http://{cfg.BindAddress}:{(cfg.Port == 0 ? 0 : cfg.Port)}");
            }
        }

        public static void Stop()
        {
            try { _http?.Dispose(); } catch { }
            _http = null;
        }

        private static void WriteDiscovery(string baseUrl)
        {
            try
            {
                var uri = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
                var port = uri.Port;
                var discovery = new
                {
                    pid = Process.GetCurrentProcess().Id,
                    port,
                    baseUrl = uri.ToString(),
                    modeHints = new[] { "streamable-http" },
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
#elif MONO
    internal static class McpHost
    {
        private static readonly object _gate = new object();
        private static bool _started;
        private static McpSimpleHttp? _http;

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
                _http = new McpSimpleHttp(cfg.BindAddress, cfg.Port);
                _http.Start();
                WriteDiscovery("http://" + cfg.BindAddress + ":" + _http.Port);
                ExplorerCore.Log("MCP (Mono) listening on http://" + cfg.BindAddress + ":" + _http.Port);
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning("MCP simple HTTP (Mono) failed: " + ex.Message);
                WriteDiscovery("http://" + cfg.BindAddress + ":" + (cfg.Port == 0 ? 0 : cfg.Port));
            }
        }

        public static void Stop()
        {
            try { _http?.Dispose(); } catch { }
            _http = null;
        }

        private static void WriteDiscovery(string baseUrl)
        {
            try
            {
                var uri = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/");
                var port = uri.Port;
                var discovery = new
                {
                    pid = Process.GetCurrentProcess().Id,
                    port,
                    baseUrl = uri.ToString(),
                    modeHints = new[] { "streamable-http" },
                    startedAt = DateTimeOffset.UtcNow.ToString("o")
                };
                var path = Path.Combine(Path.GetTempPath(), "unity-explorer-mcp.json");
                File.WriteAllText(path, JsonConvert.SerializeObject(discovery, Formatting.Indented));
            }
            catch (Exception ex)
            {
                ExplorerCore.LogWarning("Failed writing MCP discovery file (Mono): " + ex.Message);
            }
        }
    }
#else
    internal static class McpHost
    {
        public static void StartIfEnabled()
        {
            ExplorerCore.Log("MCP server is disabled in this build (INTEROP not defined).");
        }

        public static void Stop()
        {
        }
    }
#endif
}
