#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityExplorer.UI.Panels;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private const int ClipboardPreviewLimit = 256;

        [McpServerTool, Description("Return clipboard snapshot (same as unity://clipboard).")]
        public static Task<ClipboardDto> GetClipboard(CancellationToken ct = default)
        {
            return MainThread.Run(() => CaptureClipboard());
        }

        internal static ClipboardDto CaptureClipboard()
        {
            object? current = ClipboardPanel.Current;
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
                {
                    objectId = $"obj:{uo.GetInstanceID()}";
                }
            }

            return new ClipboardDto(hasValue, type, preview, objectId);
        }

        private static string SafePreview(object value)
        {
            var text = ToStringUtility.ToStringWithType(value, typeof(object), false) ?? "<null>";
            if (text.Length > ClipboardPreviewLimit)
                return text.Substring(0, ClipboardPreviewLimit) + "...";
            return text;
        }
    }
}
#endif
