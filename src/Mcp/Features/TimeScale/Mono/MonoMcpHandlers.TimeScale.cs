#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_TimeScale(List<object> list)
        {
            list.Add(new { name = "GetTimeScale", description = "Get current time-scale (read-only).", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SetTimeScale", description = "Set Unity time-scale (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "value", Number() }, { "lock", Bool() }, { "confirm", Bool(false) } }, new[] { "value" }) });
        }

        private bool TryCallTool_TimeScale(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "gettimescale":
                    result = _write.GetTimeScale();
                    return true;
                case "settimescale":
                    {
                        var val = GetFloat(args, "value");
                        if (val == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'value' is required.");
                        result = _write.SetTimeScale(val.Value, GetBool(args, "lock"), GetBool(args, "confirm") ?? false);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
