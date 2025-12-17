#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UniverseLib.Input;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Raycast at current mouse position to pick a world or UI object.")]
        public static async Task<PickResultDto> MousePick(string mode = "world", float? x = null, float? y = null, bool normalized = false, CancellationToken ct = default)
        {
            return await MainThread.Run(() =>
            {
                var normalizedMode = string.IsNullOrWhiteSpace(mode) ? "world" : mode.ToLowerInvariant();

                var pos = InputManager.MousePosition;
                if (x.HasValue || y.HasValue)
                {
                    if (normalized)
                    {
                        var nx = Mathf.Clamp01(x ?? 0f);
                        var ny = Mathf.Clamp01(y ?? 0f);
                        pos = new Vector2(nx * Screen.width, ny * Screen.height);
                    }
                    else
                    {
                        pos = new Vector2(x ?? pos.x, y ?? pos.y);
                    }
                }

                if (normalizedMode == "ui")
                {
                    var eventSystem = EventSystem.current;
                    if (eventSystem == null)
                        return new PickResultDto("ui", false, null, Array.Empty<PickHit>());

                    var pointer = new PointerEventData(eventSystem)
                    {
                        position = pos
                    };
                    var raycastResults = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
                    eventSystem.RaycastAll(pointer, raycastResults);
                    var items = new List<PickHit>(raycastResults.Count);
                    for (int i = 0; i < raycastResults.Count; i++)
                    {
                        var rr = raycastResults[i];
                        var go = rr.gameObject;
                        if (go == null) continue;

                        var resolved = UnityQuery.FindByInstanceId(go.GetInstanceID());
                        if (resolved == null) continue;

                        var id = $"obj:{resolved.GetInstanceID()}";
                        var path = UnityQuery.BuildPath(resolved.transform);
                        items.Add(new PickHit(id, resolved.name, path));
                    }

                    var primaryId = items.Count > 0 ? items[0].Id : null;
                    return new PickResultDto("ui", items.Count > 0, primaryId, items);
                }

                Camera cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0) cam = Camera.allCameras[0];
                if (cam == null) return new PickResultDto("world", false, null, null);
                var ray = cam.ScreenPointToRay(pos);
                if (Physics.Raycast(ray, out var hit))
                {
                    var col = hit.collider;
                    if (col == null) return new PickResultDto("world", false, null, null);
                    var go = col.gameObject;
                    var id = go != null ? $"obj:{go.GetInstanceID()}" : null;
                    return new PickResultDto("world", go != null, id, null);
                }
                return new PickResultDto("world", false, null, null);
            });
        }
    }
}
#endif
