#if MONO && !INTEROP
#nullable enable
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private void AddTools_TestUi(List<object> list)
        {
            list.Add(new { name = "SpawnTestUi", description = "Spawn a simple UI canvas for MousePick UI validation (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }) });
            list.Add(new { name = "DestroyTestUi", description = "Destroy the test UI canvas (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "confirm", Bool(false) } }) });
        }

        private bool TryCallTool_TestUi(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "spawntestui":
                    result = _write.SpawnTestUi(GetBool(args, "confirm") ?? false);
                    return true;
                case "destroytestui":
                    result = _write.DestroyTestUi(GetBool(args, "confirm") ?? false);
                    return true;
                default:
                    return false;
            }
        }
    }
}
#endif
