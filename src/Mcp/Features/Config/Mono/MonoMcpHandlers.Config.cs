#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Config(List<object> list)
        {
            list.Add(new { name = "SetConfig", description = "Update MCP config settings and optionally restart the server.", inputSchema = Schema(new Dictionary<string, object> { { "allowWrites", Bool() }, { "requireConfirm", Bool() }, { "enableConsoleEval", Bool() }, { "componentAllowlist", new { type = "array", items = String() } }, { "reflectionAllowlistMembers", new { type = "array", items = String() } }, { "hookAllowlistSignatures", new { type = "array", items = String() } }, { "restart", Bool() } }) });
            list.Add(new { name = "GetConfig", description = "Read current MCP config (sanitized).", inputSchema = Schema(new Dictionary<string, object>()) });
        }

        private bool TryCallTool_Config(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "setconfig":
                    result = _write.SetConfig(
                        GetBool(args, "allowWrites"),
                        GetBool(args, "requireConfirm"),
                        GetBool(args, "enableConsoleEval"),
                        GetStringArray(args, "componentAllowlist"),
                        GetStringArray(args, "reflectionAllowlistMembers"),
                        GetStringArray(args, "hookAllowlistSignatures"),
                        GetBool(args, "restart") ?? false);
                    return true;
                case "getconfig":
                    result = _write.GetConfig();
                    return true;
                default:
                    return false;
            }
        }
    }
}
#endif
