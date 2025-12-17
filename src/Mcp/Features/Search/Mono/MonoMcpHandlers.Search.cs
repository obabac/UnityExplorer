#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Search(List<object> list)
        {
            list.Add(new { name = "SearchObjects", description = "Search objects by name/type/path.", inputSchema = Schema(new Dictionary<string, object> { { "query", String() }, { "name", String() }, { "type", String() }, { "path", String() }, { "activeOnly", Bool() }, { "limit", Integer() }, { "offset", Integer() } }) });
        }

        private bool TryCallTool_Search(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "searchobjects")
            {
                result = _tools.SearchObjects(GetString(args, "query"), GetString(args, "name"), GetString(args, "type"), GetString(args, "path"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            return false;
        }
    }
}
#endif
