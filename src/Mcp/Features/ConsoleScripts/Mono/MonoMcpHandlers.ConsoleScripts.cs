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
            list.Add(new { name = "GetStartupScript", description = "Get the startup script and enabled state (startup.cs or startup.disabled.cs).", inputSchema = Schema(new Dictionary<string, object> { }, new string[0]) });
            list.Add(new { name = "WriteConsoleScript", description = "Write a C# console script file (guarded; validated to stay within the Scripts folder; fixed max bytes; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "content", String() }, { "confirm", Bool(false) } }, new[] { "path", "content" }) });
            list.Add(new { name = "DeleteConsoleScript", description = "Delete a C# console script file (guarded; validated to stay within the Scripts folder; .cs only).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "confirm", Bool(false) } }, new[] { "path" }) });
            list.Add(new { name = "RunConsoleScript", description = "Run a C# console script file (guarded; requires enableConsoleEval).", inputSchema = Schema(new Dictionary<string, object> { { "path", String() }, { "confirm", Bool(false) } }, new[] { "path" }) });
            list.Add(new { name = "SetStartupScriptEnabled", description = "Enable or disable the startup script (startup.cs ↔ startup.disabled.cs).", inputSchema = Schema(new Dictionary<string, object> { { "enabled", Bool() }, { "confirm", Bool(false) } }, new[] { "enabled" }) });
            list.Add(new { name = "WriteStartupScript", description = "Write the startup script (startup.cs) with provided content (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "content", String() }, { "confirm", Bool(false) } }, new[] { "content" }) });
            list.Add(new { name = "RunStartupScript", description = "Run the startup script (startup.cs or startup.disabled.cs) (guarded; requires enableConsoleEval).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }, new string[0]) });
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
                case "getstartupscript":
                    {
                        result = _tools.GetStartupScript();
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
                case "runconsolescript":
                    {
                        var p = RequireString(args, "path", "Invalid params: 'path' is required.");
                        result = _write.RunConsoleScript(p, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "setstartupscriptenabled":
                    {
                        var enabled = GetBool(args, "enabled");
                        if (!enabled.HasValue) throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "'enabled' is required");
                        result = _write.SetStartupScriptEnabled(enabled.Value, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "writestartupscript":
                    {
                        var content = RequireString(args, "content", "Invalid params: 'content' is required.");
                        result = _write.WriteStartupScript(content, GetBool(args, "confirm") ?? false);
                        return true;
                    }
                case "runstartupscript":
                    {
                        result = _write.RunStartupScript(GetBool(args, "confirm") ?? false);
                        return true;
                    }
                default:
                    return false;
            }
        }
    }
}
#endif
