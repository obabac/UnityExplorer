#if INTEROP
#nullable enable
using UnityEngine;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private static string SafeTag(GameObject go)
        {
            try { return go.tag; }
            catch { return string.Empty; }
        }
    }
}
#endif
