#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Refs(List<object> list)
        {
            list.Add(new { name = "InspectRef", description = "Inspect an object reference handle (ref:...).", inputSchema = Schema(new Dictionary<string, object> { { "refId", String() } }, new[] { "refId" }) });
            list.Add(new { name = "ListRefMembers", description = "List members for an object reference handle (fields/properties, methods optional).", inputSchema = Schema(new Dictionary<string, object> { { "refId", String() }, { "includeMethods", Bool(false) }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "refId" }) });
            list.Add(new { name = "ReadRefMember", description = "Read a field or property value on an object reference handle (safe, bounded).", inputSchema = Schema(new Dictionary<string, object> { { "refId", String() }, { "name", String() } }, new[] { "refId", "name" }) });
            list.Add(new { name = "ListRefItems", description = "List items for an enumerable or dictionary reference handle (paged).", inputSchema = Schema(new Dictionary<string, object> { { "refId", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "refId" }) });
            list.Add(new { name = "ReleaseRef", description = "Release an object reference handle from the server cache.", inputSchema = Schema(new Dictionary<string, object> { { "refId", String() } }, new[] { "refId" }) });
        }

        private bool TryCallTool_Refs(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "inspectref")
            {
                var refId = GetString(args, "refId");
                if (IsNullOrWhiteSpace(refId))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'refId' is required.");
                result = _tools.InspectRef(refId!);
                return true;
            }
            if (key == "listrefmembers")
            {
                var refId = GetString(args, "refId");
                if (IsNullOrWhiteSpace(refId))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'refId' is required.");
                result = _tools.ListRefMembers(refId!, GetBool(args, "includeMethods") ?? false, GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "readrefmember")
            {
                var refId = GetString(args, "refId");
                var name = GetString(args, "name");
                if (IsNullOrWhiteSpace(refId) || IsNullOrWhiteSpace(name))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'refId' and 'name' are required.");
                result = _tools.ReadRefMember(refId!, name!);
                return true;
            }
            if (key == "listrefitems")
            {
                var refId = GetString(args, "refId");
                if (IsNullOrWhiteSpace(refId))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'refId' is required.");
                result = _tools.ListRefItems(refId!, GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "releaseref")
            {
                var refId = GetString(args, "refId");
                if (IsNullOrWhiteSpace(refId))
                    throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'refId' is required.");
                result = _tools.ReleaseRef(refId!);
                return true;
            }
            return false;
        }
    }
}
#endif

