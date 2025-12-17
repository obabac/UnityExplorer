#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Hooks(List<object> list)
        {
            list.Add(new { name = "HookListAllowedTypes", description = "List hook-allowed types (mirrors config hookAllowlistSignatures).", inputSchema = Schema(new Dictionary<string, object> { }) });
            list.Add(new { name = "HookListMethods", description = "List methods for a hook-allowed type (paged).", inputSchema = Schema(new Dictionary<string, object> { { "type", String() }, { "filter", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "type" }) });
            list.Add(new { name = "HookGetSource", description = "Get hook patch source by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() } }, new[] { "signature" }) });
            list.Add(new { name = "HookAdd", description = "Add a Harmony hook for the given type and method (guarded by hook allowlist).", inputSchema = Schema(new Dictionary<string, object> { { "type", String() }, { "method", String() }, { "confirm", Bool(false) } }, new[] { "type", "method" }) });
            list.Add(new { name = "HookSetEnabled", description = "Enable or disable a previously added Harmony hook by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "enabled", Bool() }, { "confirm", Bool(false) } }, new[] { "signature", "enabled" }) });
            list.Add(new { name = "HookSetSource", description = "Update the patch source for a previously added Harmony hook (requires enableConsoleEval).", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "source", String() }, { "confirm", Bool(false) } }, new[] { "signature", "source" }) });
            list.Add(new { name = "HookRemove", description = "Remove a previously added Harmony hook by signature.", inputSchema = Schema(new Dictionary<string, object> { { "signature", String() }, { "confirm", Bool(false) } }, new[] { "signature" }) });
        }

        private bool TryCallTool_Hooks(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "hooklistallowedtypes":
                    result = _tools.HookListAllowedTypes();
                    return true;
                case "hooklistmethods":
                    {
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        result = _tools.HookListMethods(type, GetString(args, "filter"), GetInt(args, "limit"), GetInt(args, "offset"));
                        return true;
                    }
                case "hookgetsource":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        result = _tools.HookGetSource(signature);
                        return true;
                    }
                case "hookadd":
                    {
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        var method = RequireString(args, "method", "Invalid params: 'method' is required.");
                        result = _write.HookAdd(type, method, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "hooksetenabled":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        var enabled = GetBool(args, "enabled");
                        if (enabled == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'enabled' is required.");
                        result = _write.HookSetEnabled(signature, enabled.Value, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "hooksetsource":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        var source = RequireString(args, "source", "Invalid params: 'source' is required.");
                        result = _write.HookSetSource(signature, source, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "hookremove":
                    {
                        var signature = RequireString(args, "signature", "Invalid params: 'signature' is required.");
                        result = _write.HookRemove(signature, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
