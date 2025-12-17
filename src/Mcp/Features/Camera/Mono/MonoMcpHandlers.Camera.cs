#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Camera(List<object> list)
        {
            list.Add(new { name = "GetCameraInfo", description = "Get active camera info.", inputSchema = Schema(new Dictionary<string, object>()) });
        }

        private bool TryCallTool_Camera(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "getcamerainfo")
            {
                result = _tools.GetCameraInfo();
                return true;
            }
            return false;
        }
    }
}
#endif
