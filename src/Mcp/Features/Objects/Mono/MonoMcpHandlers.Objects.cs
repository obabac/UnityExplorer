#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Objects(List<object> list)
        {
            list.Add(new { name = "ListObjects", description = "List objects in a scene or all scenes.", inputSchema = Schema(new Dictionary<string, object> { { "sceneId", String() }, { "name", String() }, { "type", String() }, { "activeOnly", Bool() }, { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "GetObject", description = "Get object details by id.", inputSchema = Schema(new Dictionary<string, object> { { "id", String() } }, new[] { "id" }) });
            list.Add(new { name = "ListChildren", description = "List direct children for an object (paged).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "limit", Integer() }, { "offset", Integer() } }, new[] { "objectId" }) });
            list.Add(new { name = "SetActive", description = "Set GameObject active state (guarded by allowWrites/confirm).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "active", Bool() }, { "confirm", Bool(false) } }, new[] { "objectId", "active" }) });
            list.Add(new { name = "Reparent", description = "Reparent a GameObject under a new parent (guarded; SpawnTestUi blocks recommended).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "newParentId", String() }, { "confirm", Bool(false) } }, new[] { "objectId", "newParentId" }) });
            list.Add(new { name = "DestroyObject", description = "Destroy a GameObject (guarded; SpawnTestUi blocks recommended).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "confirm", Bool(false) } }, new[] { "objectId" }) });
        }

        private bool TryCallTool_Objects(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "listobjects":
                    result = _tools.ListObjects(GetString(args, "sceneId"), GetString(args, "name"), GetString(args, "type"), GetBool(args, "activeOnly"), GetInt(args, "limit"), GetInt(args, "offset"));
                    return true;
                case "getobject":
                    {
                        var id = RequireString(args, "id", "Invalid params: 'id' is required.");
                        result = _tools.GetObject(id);
                        return true;
                    }
                case "listchildren":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        result = _tools.ListChildren(oid, GetInt(args, "limit"), GetInt(args, "offset"));
                        return true;
                    }
                case "setactive":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var active = GetBool(args, "active");
                        if (active == null)
                            throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'active' is required.");
                        result = _write.SetActive(oid, active.Value, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "reparent":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        var pid = RequireString(args, "newParentId", "Invalid params: 'newParentId' is required.");
                        result = _write.Reparent(oid, pid, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "destroyobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        result = _write.DestroyObject(oid, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
