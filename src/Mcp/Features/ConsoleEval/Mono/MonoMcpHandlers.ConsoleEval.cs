#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_ConsoleEval(List<object> list)
        {
            list.Add(new { name = "ConsoleEval", description = "Evaluate a small C# snippet in the UnityExplorer console context (guarded by config).", inputSchema = Schema(new Dictionary<string, object> { { "code", String() }, { "confirm", Bool(false) } }, new[] { "code" }) });
        }

        private bool TryCallTool_ConsoleEval(string key, JObject? args, out object result)
        {
            result = null!;
            if (key == "consoleeval")
            {
                var code = RequireString(args, "code", "Invalid params: 'code' is required.");
                result = _write.ConsoleEval(code, GetBool(args, "confirm") ?? false);
                return true;
            }
            return false;
        }
    }
}
#endif
