#if MONO && !INTEROP
#nullable enable
namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public SelectionDto GetSelection()
        {
            return MainThread.Run(() =>
            {
                var snap = CaptureSelection();
                return new SelectionDto { ActiveId = snap.ActiveId, Items = snap.Items };
            });
        }
    }
}
#endif
