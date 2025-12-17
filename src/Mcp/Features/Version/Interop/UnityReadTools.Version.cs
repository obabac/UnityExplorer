#if INTEROP
#nullable enable
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Version information for UnityExplorer MCP and Unity runtime.")]
        public static async Task<VersionInfoDto> GetVersion(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var explorerVersion = ExplorerCore.VERSION;
                var mcpVersion = typeof(McpSimpleHttp).Assembly.GetName().Version?.ToString() ?? "0.0.0";
                var unityVersion = Application.unityVersion;
                var runtime = Universe.Context.ToString();
                return new VersionInfoDto(explorerVersion, mcpVersion, unityVersion, runtime);
            });
        }
    }
}
#endif
