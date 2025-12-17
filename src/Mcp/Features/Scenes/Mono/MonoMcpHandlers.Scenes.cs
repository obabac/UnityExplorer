#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Scenes(List<object> list)
        {
            list.Add(new { name = "ListScenes", description = "List scenes (paged).", inputSchema = Schema(new Dictionary<string, object> { { "limit", Integer() }, { "offset", Integer() } }) });
        }

        private bool TryCallTool_Scenes(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "listscenes")
            {
                result = _tools.ListScenes(GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            return false;
        }
    }
}
#endif
