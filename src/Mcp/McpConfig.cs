using System;
using System.IO;
#if INTEROP
using System.Text.Json;
#endif
#if MONO
using Newtonsoft.Json;
#endif

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal sealed class McpConfig
    {
        public bool Enabled { get; set; } = true;
        // BindAddress is where the MCP HTTP listener binds. Default to 0.0.0.0 so
        // remote tools on the LAN (e.g. dev VM) can connect directly to the
        // Test-VM.
        public string BindAddress { get; set; } = "0.0.0.0";
        // Use a fixed default port to make testing simpler. Port 51477 is used
        // by convention; set to 0 explicitly in config if you really want an
        // ephemeral port.
        public int Port { get; set; } = 51477;
        public string TransportPreference { get; set; } = "auto"; // auto|streamable-http
        public bool AllowWrites { get; set; } = false;
        public bool RequireConfirm { get; set; } = true;
        public string? ReflectionAllowlistPath { get; set; }
        public string[]? ReflectionAllowlistMembers { get; set; }
        public string? ExportRoot { get; set; }
        public string LogLevel { get; set; } = "Information";
        public string[]? ComponentAllowlist { get; set; }
        // Phase-2 / advanced features (disabled by default).
        public bool EnableConsoleEval { get; set; } = false;
        public string[]? HookAllowlistSignatures { get; set; }

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
                var loaded = JsonSerializer.Deserialize<McpConfig>(json) ?? new McpConfig();

                // Migration: older configs used 127.0.0.1 and port 0 (ephemeral).
                // For remote tooling (e.g. dev VM connecting to Test-VM), binding to
                // 0.0.0.0 on a fixed port is more convenient. If a different bind
                // address/port is explicitly set, respect it.
                if (string.Equals(loaded.BindAddress, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                    loaded.BindAddress = "0.0.0.0";
                if (loaded.Port == 0)
                    loaded.Port = 51477;

                return loaded;
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
#elif MONO
    internal sealed class McpConfig
    {
        public bool Enabled { get; set; } = true;
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 51477;
        public string TransportPreference { get; set; } = "auto";
        public bool AllowWrites { get; set; } = false;
        public bool RequireConfirm { get; set; } = true;
        public string? ReflectionAllowlistPath { get; set; }
        public string[]? ReflectionAllowlistMembers { get; set; }
        public string? ExportRoot { get; set; }
        public string LogLevel { get; set; } = "Information";
        public string[]? ComponentAllowlist { get; set; }
        public bool EnableConsoleEval { get; set; } = false;
        public string[]? HookAllowlistSignatures { get; set; }

        public static McpConfig Load()
        {
            try
            {
                var folder = ExplorerCore.ExplorerFolder;
                var path = Path.Combine(folder, "mcp.config.json");
                if (!File.Exists(path))
                {
                    var cfg = new McpConfig();
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
                    return cfg;
                }

                var json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<McpConfig>(json) ?? new McpConfig();
                if (string.Equals(loaded.BindAddress, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
                    loaded.BindAddress = "0.0.0.0";
                if (loaded.Port == 0) loaded.Port = 51477;
                return loaded;
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
                File.WriteAllText(path, JsonConvert.SerializeObject(cfg, Formatting.Indented));
            }
            catch (Exception)
            {
            }
        }
    }
#else
    internal sealed class McpConfig
    {
        public bool Enabled { get; set; } = false;
        public string BindAddress { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 51477;
        public string TransportPreference { get; set; } = "disabled";
        public bool AllowWrites { get; set; } = false;
        public bool RequireConfirm { get; set; } = true;
        public string? ReflectionAllowlistPath { get; set; }
        public string[]? ReflectionAllowlistMembers { get; set; }
        public string? ExportRoot { get; set; }
        public string LogLevel { get; set; } = "Information";
        public string[]? ComponentAllowlist { get; set; }
        public bool EnableConsoleEval { get; set; } = false;
        public string[]? HookAllowlistSignatures { get; set; }

        public static McpConfig Load() => new();
        public static void Save(McpConfig cfg) { }
    }
#endif
}
