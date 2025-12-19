#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Clipboard(List<object> list)
        {
            list.Add(new { name = "GetClipboard", description = "Return clipboard snapshot.", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SetClipboardText", description = "Set clipboard text (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "text", String() }, { "confirm", Bool(false) } }, new[] { "text" }) });
            list.Add(new { name = "SetClipboardObject", description = "Set clipboard to a GameObject by id (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "objectId", String() }, { "confirm", Bool(false) } }, new[] { "objectId" }) });
            list.Add(new { name = "ClearClipboard", description = "Clear clipboard (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }, new string[0]) });
        }

        private bool TryCallTool_Clipboard(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "getclipboard":
                    result = _tools.GetClipboard();
                    return true;
                case "setclipboardtext":
                    {
                        var text = RequireString(args, "text", "Invalid params: 'text' is required.");
                        result = _write.SetClipboardText(text, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "setclipboardobject":
                    {
                        var oid = RequireString(args, "objectId", "Invalid params: 'objectId' is required.");
                        result = _write.SetClipboardObject(oid, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "clearclipboard":
                    result = _write.ClearClipboard(GetBool(args, "confirm") ?? false);
                    return true;
                default:
                    return false;
            }
        }
    }
}
#endif
