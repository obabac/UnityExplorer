#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Selection(List<object> list)
        {
            list.Add(new { name = "GetSelection", description = "Current selection / inspected tabs.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SelectObject", description = "Select a GameObject in the inspector (requires allowWrites).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() } }, new[] { "objectId" }) });
        }

        private bool TryCallTool_Selection(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "getselection":
                    result = _tools.GetSelection();
                    return true;
                case "selectobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        result = _write.SelectObject(oid);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
