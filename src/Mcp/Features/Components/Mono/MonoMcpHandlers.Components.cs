#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Components(List<object> list)
        {
            list.Add(new { name = "GetComponents", description = "List component cards for an object.", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "objectId" }) });
            list.Add(new { name = "AddComponent", description = "Add a component by full type name to a GameObject (guarded by allowlist).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "type", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "type" }) });
            list.Add(new { name = "RemoveComponent", description = "Remove a component by full type name or index from a GameObject (allowlist enforced when by type).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "typeOrIndex", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "typeOrIndex" }) });
            list.Add(new { name = "SetMember", description = "Set a field or property on a component (allowlist enforced).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "componentType", String() }, { "member", String() }, { "jsonValue", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "componentType", "member", "jsonValue" }) });
            list.Add(new { name = "CallMethod", description = "Call a method on a component (allowlist enforced).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "componentType", String() }, { "method", String() }, { "argsJson", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "componentType", "method" }) });
        }

        private bool TryCallTool_Components(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "getcomponents":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        result = _tools.GetComponents(oid, GetInt(args, "limit"), GetInt(args, "offset"));
                        return true;
                    }
                case "addcomponent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "type", "Invalid params: 'type' is required.");
                        result = _write.AddComponent(oid, type, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "removecomponent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var typeOrIndex = RequireString(args, "typeOrIndex", "Invalid params: 'typeOrIndex' is required.");
                        result = _write.RemoveComponent(oid, typeOrIndex, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "setmember":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "componentType", "Invalid params: 'componentType' is required.");
                        var member = RequireString(args, "member", "Invalid params: 'member' is required.");
                        var jsonValue = RequireString(args, "jsonValue", "Invalid params: 'jsonValue' is required.");
                        result = _write.SetMember(oid, type, member, jsonValue, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "callmethod":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var type = RequireString(args, "componentType", "Invalid params: 'componentType' is required.");
                        var method = RequireString(args, "method", "Invalid params: 'method' is required.");
                        var argsJson = GetString(args, "argsJson") ?? "[]";
                        result = _write.CallMethod(oid, type, method, argsJson, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
