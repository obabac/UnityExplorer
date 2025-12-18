#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_Scenes(List<object> list)
        {
            list.Add(new { name = "ListScenes", description = "List scenes (paged).", inputSchema = Schema(new Dictionary<string, object> { { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new { name = "ListBuildScenes", description = "List scenes from build settings (paged).", inputSchema = Schema(new Dictionary<string, object> { { "limit", Integer() }, { "offset", Integer() } }) });
            list.Add(new
            {
                name = "LoadScene",
                description = "Load a scene (Single/Additive).",
                inputSchema = Schema(
                    new Dictionary<string, object>
                    {
                        { "name", String() },
                        { "mode", new { type = "string", @enum = new[] { "single", "additive" }, @default = "single" } },
                        { "confirm", Bool() }
                    },
                    new[] { "name" })
            });
        }

        private bool TryCallTool_Scenes(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "listscenes")
            {
                result = _tools.ListScenes(GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "listbuildscenes")
            {
                result = _tools.ListBuildScenes(GetInt(args, "limit"), GetInt(args, "offset"));
                return true;
            }
            if (key == "loadscene")
            {
                var name = RequireString(args, "name", "Invalid params: 'name' is required.");
                var mode = GetString(args, "mode") ?? "single";
                var confirm = GetBool(args, "confirm") ?? false;
                result = _write.LoadScene(name, mode, confirm);
                return true;
            }
            return false;
        }
    }
}
#endif
