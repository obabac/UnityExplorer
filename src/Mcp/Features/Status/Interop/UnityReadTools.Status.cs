#if INTEROP
#nullable enable
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Status snapshot of Unity Explorer and Unity runtime.")]
        public static async Task<StatusDto> GetStatus(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var scenesLoaded = SceneManager.sceneCount;
                var platform = Application.platform.ToString();
                var runtime = Universe.Context.ToString();
                var ready = scenesLoaded > 0;
                var (_, selectionItems) = SnapshotSelection();
                return new StatusDto(
                    Version: "0.1.0",
                    UnityVersion: Application.unityVersion,
                    Platform: platform,
                    Runtime: runtime,
                    ExplorerVersion: ExplorerCore.VERSION,
                    Ready: ready,
                    ScenesLoaded: scenesLoaded,
                    Selection: selectionItems
                );
            });
        }
    }
}
#endif
