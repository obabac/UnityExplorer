#if MONO && !INTEROP
#nullable enable
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public StatusDto GetStatus()
        {
            return MainThread.Run(() =>
            {
                var scenesLoaded = SceneManager.sceneCount;
                var platform = Application.platform.ToString();
                var runtime = Universe.Context.ToString();
                var selection = CaptureSelection().Items;
                return new StatusDto
                {
                    Version = "0.1.0",
                    UnityVersion = Application.unityVersion,
                    Platform = platform,
                    Runtime = runtime,
                    ExplorerVersion = ExplorerCore.VERSION,
                    Ready = scenesLoaded > 0,
                    ScenesLoaded = scenesLoaded,
                    Selection = selection
                };
            });
        }
    }
}
#endif
