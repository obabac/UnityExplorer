#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_ConsoleScripts(List<object> list)
        {
            list.Add(new { name = "ReadConsoleScript", description = "Read a C# console script file (validated to stay within the Scripts folder; fixed max bytes; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() } }, new[] { "path" }) });
            list.Add(new { name = "WriteConsoleScript", description = "Write a C# console script file (guarded; validated to stay within the Scripts folder; fixed max bytes; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "content", String() }, { "confirm", Bool(false) } }, new[] { "path", "content" }) });
            list.Add(new { name = "DeleteConsoleScript", description = "Delete a C# console script file (guarded; validated to stay within the Scripts folder; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "confirm", Bool(false) } }, new[] { "path" }) });
        }

        private bool TryCallTool_ConsoleScripts(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "readconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        result = _tools.ReadConsoleScript(p);
                        return true;
                    }
                case "writeconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        var c = RequireString(args, "content", "Invalid params: 'content' is required.");
                        result = _write.WriteConsoleScript(p, c, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "deleteconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        result = _write.DeleteConsoleScript(p, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
