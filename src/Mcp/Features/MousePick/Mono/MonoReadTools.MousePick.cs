#if MONO && !INTEROP
#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UniverseLib.Input;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public PickResultDto MousePick(string? mode = "world", float? x = null, float? y = null, bool normalized = false)
        {
            return MainThread.Run(() =>
            {
                var normalizedMode = string.IsNullOrEmpty(mode) ? "world" : mode.ToLowerInvariant();
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
                        return new PickResultDto
                        {
                            Mode = "ui",
                            Hit = false,
                            Id = null,
                            Items = new List<PickHit>()
                        };

                    var pointer = new PointerEventData(eventSystem)
                    {
                        position = pos
                    };
                    var raycastResults = new List<RaycastResult>();
                    eventSystem.RaycastAll(pointer, raycastResults);
                    var items = new List<PickHit>(raycastResults.Count);
                    foreach (var rr in raycastResults)
                    {
                        var go = rr.gameObject;
                        if (go == null) continue;

                        var resolved = FindByInstanceId(go.GetInstanceID());
                        if (resolved == null) continue;

                        var id = "obj:" + resolved.GetInstanceID();
                        var path = BuildPath(resolved.transform);
                        items.Add(new PickHit { Id = id, Name = resolved.name, Path = path });
                    }

                    var primaryId = items.Count > 0 ? items[0].Id : null;
                    return new PickResultDto
                    {
                        Mode = "ui",
                        Hit = items.Count > 0,
                        Id = primaryId,
                        Items = items
                    };
                }

                var cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0)
                    cam = Camera.allCameras[0];
                if (cam == null)
                    return new PickResultDto
                    {
                        Mode = "world",
                        Hit = false,
                        Id = null,
                        Items = null
                    };

                var ray = cam.ScreenPointToRay(pos);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    var go = hit.collider != null ? hit.collider.gameObject : null;
                    var id = go != null ? "obj:" + go.GetInstanceID() : null;
                    return new PickResultDto
                    {
                        Mode = "world",
                        Hit = go != null,
                        Id = id,
                        Items = null
                    };
                }

                return new PickResultDto
                {
                    Mode = "world",
                    Hit = false,
                    Id = null,
                    Items = null
                };
            });
        }
    }
}
#endif
