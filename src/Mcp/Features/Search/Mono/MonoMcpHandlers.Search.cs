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
            list.Add(new { name = "SearchSingletons", description = "Search singleton instances by declaring type.", inputSchema = Schema(new Dictionary<string, object> { { "query", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "query" }) });
            list.Add(new { name = "SearchStaticClasses", description = "Search static classes by full name.", inputSchema = Schema(new Dictionary<string, object> { { "query", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "query" }) });
            list.Add(new { name = "ListStaticMembers", description = "List static members for a static class (fields/properties, methods optional).", inputSchema = Schema(new Dictionary<string, object> { { "typeFullName", String() }, { "includeMethods", Bool(false) }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "typeFullName" }) });
        }

        private bool TryCallTool_Search(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "searchobjects")
            {
                result = _tools.SearchObjects(GetString(args, "query"), GetString(args, "name"), GetString(args, "type"), GetString(args, "path"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "searchsingletons")
            {
                var query = GetString(args, "query");
                if (IsNullOrWhiteSpace(query))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'query' is required.");
                result = _tools.SearchSingletons(query!, GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "searchstaticclasses")
            {
                var query = GetString(args, "query");
                if (IsNullOrWhiteSpace(query))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'query' is required.");
                result = _tools.SearchStaticClasses(query!, GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "liststaticmembers")
            {
                var typeFullName = GetString(args, "typeFullName");
                if (IsNullOrWhiteSpace(typeFullName))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'typeFullName' is required.");
                result = _tools.ListStaticMembers(typeFullName!, GetBool(args, "includeMethods") ?? false, GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            return false;
        }
    }
}
#endif
