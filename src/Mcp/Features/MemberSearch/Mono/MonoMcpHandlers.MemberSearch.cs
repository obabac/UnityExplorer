#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_MemberSearch(List<object> list)
        {
            list.Add(new { name = "SearchComponentMembers", description = "Search component member names across all objects (fields + properties).", inputSchema = Schema(new Dictionary<string, object> { { "query", String() }, { "componentType", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "query" }) });
        }

        private bool TryCallTool_MemberSearch(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "searchcomponentmembers")
            {
                var query = GetString(args, "query");
                if (IsNullOrWhiteSpace(query))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'query' is required.");
                result = _tools.SearchComponentMembers(query!, GetString(args, "componentType"), GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            return false;
        }
    }
}
#endif

