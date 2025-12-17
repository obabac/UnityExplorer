#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Version(List<object> list)
        {
            list.Add(new { name = "GetVersion", description = "Version info for Unity Explorer MCP.", inputSchema = Schema(new Dictionary<string, object>()) });
        }

        private bool TryCallTool_Version(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "getversion")
            {
                result = _tools.GetVersion();
                return true;
            }
            return false;
        }
    }
}
#endif
