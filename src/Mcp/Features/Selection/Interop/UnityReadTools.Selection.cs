#if INTEROP
#nullable enable
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private static string? _fallbackSelectionActive;
        private static readonly List<string> _fallbackSelectionItems = new();

        internal static void RecordSelection(string objectId)
        {
            _fallbackSelectionActive = objectId;
            if (!_fallbackSelectionItems.Contains(objectId))
                _fallbackSelectionItems.Insert(0, objectId);
        }

        [McpServerTool, Description("Return current selection/inspected tabs (best effort).")]
        public static async Task<SelectionDto> GetSelection(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var (active, items) = SnapshotSelection();
                return new SelectionDto(active, items);
            });
        }

        private static (string? ActiveId, List<string> Items) SnapshotSelection()
        {
            string? active = null;
            var items = new List<string>();
            try
            {
                if (InspectorManager.ActiveInspector?.Target is GameObject ago)
                    active = $"obj:{ago.GetInstanceID()}";
                foreach (var ins in InspectorManager.Inspectors)
                {
                    if (ins.Target is GameObject go)
                    {
                        var id = $"obj:{go.GetInstanceID()}";
                        if (!items.Contains(id))
                            items.Add(id);
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(active) && _fallbackSelectionActive != null)
                active = _fallbackSelectionActive;
            if (items.Count == 0 && _fallbackSelectionItems.Count > 0)
                items.AddRange(_fallbackSelectionItems);
            if (active != null && !items.Contains(active))
                items.Insert(0, active);
            return (active, items);
        }
    }
}
#endif
