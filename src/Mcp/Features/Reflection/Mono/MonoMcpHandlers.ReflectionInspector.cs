#if MONO && !INTEROP
#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_ReflectionInspector(List<object> list)
        {
            list.Add(new { name = "ReadStaticMember", description = "Read a static field or property value (safe, bounded).", inputSchema = Schema(new Dictionary<string, object> { { "typeFullName", String() }, { "name", String() } }, new[] { "typeFullName", "name" }) });
            list.Add(new { name = "ListSingletonMembers", description = "List members for a singleton instance (fields/properties/methods).", inputSchema = Schema(new Dictionary<string, object> { { "singletonId", String() }, { "includeMethods", Bool(false) }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "singletonId" }) });
            list.Add(new { name = "ReadSingletonMember", description = "Read a singleton member value (safe, bounded).", inputSchema = Schema(new Dictionary<string, object> { { "singletonId", String() }, { "name", String() } }, new[] { "singletonId", "name" }) });
        }

        private bool TryCallTool_ReflectionInspector(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "readstaticmember")
            {
                var typeFullName = RequireString(args, "typeFullName", "typeFullName is required");
                var name = RequireString(args, "name", "name is required");
                result = _tools.ReadStaticMember(typeFullName, name);
                return true;
            }
            if (key == "listsingletonmembers")
            {
                var singletonId = RequireString(args, "singletonId", "singletonId is required");
                result = _tools.ListSingletonMembers(singletonId, GetBool(args, "includeMethods") ?? false, GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "readsingletonmember")
            {
                var singletonId = RequireString(args, "singletonId", "singletonId is required");
                var name = RequireString(args, "name", "name is required");
                result = _tools.ReadSingletonMember(singletonId, name);
                return true;
            }
            return false;
        }
    }
}
#endif
