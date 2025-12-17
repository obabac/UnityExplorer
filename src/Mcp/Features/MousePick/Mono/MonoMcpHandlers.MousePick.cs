#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_MousePick(List<object> list)
        {
            list.Add(new
            {
                name = "MousePick",
                description = "Raycast at current mouse position to pick a world or UI object.",
                inputSchema = Schema(new Dictionary<string, object>
                {
                    { "mode", new { type = "string", @enum = new[] { "world", "ui" }, @default = "world" } },
                    { "x", Number() },
                    { "y", Number() },
                    { "normalized", Bool(false) }
                })
            });
        }

        private bool TryCallTool_MousePick(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "mousepick")
            {
                result = _tools.MousePick(GetString(args, "mode"), GetFloat(args, "x"), GetFloat(args, "y"), GetBool(args, "normalized") ?? false);
                return true;
            }
            return false;
        }
    }
}
#endif
