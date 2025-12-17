#if INTEROP
#nullable enable
using System;
using System.ComponentModel;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Update MCP config settings and optionally restart the server.")]
        public static object SetConfig(
            bool? allowWrites = null,
            bool? requireConfirm = null,
            bool? enableConsoleEval = null,
            string[]? componentAllowlist = null,
            string[]? reflectionAllowlistMembers = null,
            string[]? hookAllowlistSignatures = null,
            bool restart = false)
        {
            try
            {
                var cfg = McpConfig.Load();
                if (allowWrites.HasValue) cfg.AllowWrites = allowWrites.Value;
                if (requireConfirm.HasValue) cfg.RequireConfirm = requireConfirm.Value;
                if (enableConsoleEval.HasValue) cfg.EnableConsoleEval = enableConsoleEval.Value;
                if (componentAllowlist != null) cfg.ComponentAllowlist = componentAllowlist;
                if (reflectionAllowlistMembers != null) cfg.ReflectionAllowlistMembers = reflectionAllowlistMembers;
                if (hookAllowlistSignatures != null) cfg.HookAllowlistSignatures = hookAllowlistSignatures;
                McpConfig.Save(cfg);
                if (restart)
                {
                    Mcp.McpHost.Stop();
                    Mcp.McpHost.StartIfEnabled();
                }
                return new { ok = true };
            }
            catch (Exception ex) { return ToolError("Internal", ex.Message); }
        }

        [McpServerTool, Description("Read current MCP config (sanitized).")]
        public static object GetConfig()
        {
            try
            {
                var cfg = McpConfig.Load();
                return new
                {
                    ok = true,
                    enabled = cfg.Enabled,
                    bindAddress = cfg.BindAddress,
                    port = cfg.Port,
                    allowWrites = cfg.AllowWrites,
                    requireConfirm = cfg.RequireConfirm,
                    exportRoot = cfg.ExportRoot,
                    logLevel = cfg.LogLevel,
                    componentAllowlist = cfg.ComponentAllowlist,
                    reflectionAllowlistMembers = cfg.ReflectionAllowlistMembers,
                    enableConsoleEval = cfg.EnableConsoleEval,
                    hookAllowlistSignatures = cfg.HookAllowlistSignatures
                };
            }
            catch (Exception ex)
            {
                return ToolError("Internal", ex.Message);
            }
        }
    }
}
#endif
