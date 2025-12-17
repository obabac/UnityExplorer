#if MONO && !INTEROP
#nullable enable
using UnityEngine;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public VersionInfoDto GetVersion()
        {
            return MainThread.Run(() =>
            {
                var explorerVersion = ExplorerCore.VERSION;
                var mcpVersion = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var unityVersion = Application.unityVersion;
                var runtime = Universe.Context.ToString();
                return new VersionInfoDto
                {
                    ExplorerVersion = explorerVersion,
                    McpVersion = mcpVersion,
                    UnityVersion = unityVersion,
                    Runtime = runtime
                };
            });
        }
    }
}
#endif
