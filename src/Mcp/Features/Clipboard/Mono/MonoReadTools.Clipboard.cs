#if MONO && !INTEROP
#nullable enable
using UnityEngine;
using UnityExplorer.UI.Panels;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        private const int ClipboardPreviewLimit = 256;

        public ClipboardDto GetClipboard()
        {
            return MainThread.Run(() => SnapshotClipboard());
        }

        private ClipboardDto SnapshotClipboard()
        {
            var current = ClipboardPanel.Current;
            var hasValue = current != null;
            string? type = null;
            string? preview = null;
            string? objectId = null;

            if (hasValue)
            {
                var actualType = current?.GetActualType() ?? current?.GetType();
                type = actualType?.FullName ?? actualType?.Name;
                preview = SafePreview(current!);
                if (current is UnityEngine.Object uo && uo != null)
                    objectId = "obj:" + uo.GetInstanceID();
            }

            return new ClipboardDto
            {
                HasValue = hasValue,
                Type = type,
                Preview = preview,
                ObjectId = objectId
            };
        }

        private string SafePreview(object value)
        {
            var text = ToStringUtility.ToStringWithType(value, typeof(object), false) ?? "<null>";
            if (text.Length > ClipboardPreviewLimit)
                return text.Substring(0, ClipboardPreviewLimit) + "...";
            return text;
        }
    }
}
#endif
