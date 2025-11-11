using System;
using System.IO;
using System.Text.Json;

namespace UnityExplorer.Mcp
{
    internal sealed class McpConfig
    {
        public bool Enabled { get; set; } = true;
        public string BindAddress { get; set; } = "127.0.0.1";
        public int Port { get; set; } = 0; // 0 = ephemeral
        public string TransportPreference { get; set; } = "auto"; // auto|streamable-http|sse
        public bool AllowWrites { get; set; } = false;
        public bool RequireConfirm { get; set; } = true;
        public string? ReflectionAllowlistPath { get; set; }
        public string[]? ReflectionAllowlistMembers { get; set; }
        public string? ExportRoot { get; set; }
        public string LogLevel { get; set; } = "Information";
        public string? AuthToken { get; set; }
        public string[]? ComponentAllowlist { get; set; }

        public static McpConfig Load()
        {
            try
            {
                var folder = ExplorerCore.ExplorerFolder;
                var path = Path.Combine(folder, "mcp.config.json");
                if (!File.Exists(path))
                {
                    var cfg = new McpConfig();
                    try
                    {
                        Directory.CreateDirectory(folder);
                        File.WriteAllText(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    catch { /* ignore */ }
                    return cfg;
                }
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<McpConfig>(json);
                return loaded ?? new McpConfig();
            }
            catch (Exception)
            {
                return new McpConfig();
            }
        }

        public static void Save(McpConfig cfg)
        {
            try
            {
                var folder = ExplorerCore.ExplorerFolder;
                var path = Path.Combine(folder, "mcp.config.json");
                Directory.CreateDirectory(folder);
                File.WriteAllText(path, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
