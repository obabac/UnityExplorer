#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Status(List<object> list)
        {
            list.Add(new { name = "GetStatus", description = "Status snapshot of Unity Explorer.", inputSchema = Schema(new Dictionary<string, object>()) });
        }

        private bool TryCallTool_Status(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "getstatus")
            {
                result = _tools.GetStatus();
                return true;
            }
            return false;
        }
    }
}
#endif
