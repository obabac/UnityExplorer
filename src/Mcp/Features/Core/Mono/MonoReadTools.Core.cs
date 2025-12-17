#if MONO && !INTEROP
#nullable enable
using UnityEngine;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private static string SafeTag(GameObject go)
        {
            try { return go.tag; }
            catch { return string.Empty; }
        }
    }
}
#endif
