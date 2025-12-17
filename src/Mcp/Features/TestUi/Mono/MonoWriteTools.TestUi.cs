#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object SpawnTestUi(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (_testUiRoot != null)
                    {
                        if (_testUiLeft == null)
                        {
                            var foundLeft = _testUiRoot.transform.Find("McpTestBlock_Left");
                            _testUiLeft = foundLeft != null ? foundLeft.gameObject : AddTestBlock(_testUiRoot, "McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                        }
                        if (_testUiRight == null)
                        {
                            var foundRight = _testUiRoot.transform.Find("McpTestBlock_Right");
                            _testUiRight = foundRight != null ? foundRight.gameObject : AddTestBlock(_testUiRoot, "McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                        }
                        return;
                    }

                    if (EventSystem.current == null)
                    {
                        var es = new GameObject("McpTest_EventSystem");
                        es.AddComponent<EventSystem>();
                        es.AddComponent<StandaloneInputModule>();
                        es.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }

                    var root = new GameObject("McpTestCanvas");
                    var canvas = root.AddComponent<Canvas>();
                    root.AddComponent<CanvasScaler>();
                    root.AddComponent<GraphicRaycaster>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                    var scaler = root.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = 0.5f;

                    _testUiRoot = root;
                    _testUiLeft = AddTestBlock(root, "McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                    _testUiRight = AddTestBlock(root, "McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                });

                var blocks = new List<object>();
                if (_testUiLeft != null) blocks.Add(new { name = _testUiLeft.name, id = ObjectId(_testUiLeft) });
                if (_testUiRight != null) blocks.Add(new { name = _testUiRight.name, id = ObjectId(_testUiRight) });

                return new
                {
                    ok = true,
                    rootId = _testUiRoot != null ? ObjectId(_testUiRoot) : null,
                    blocks = blocks.ToArray()
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object DestroyTestUi(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (_testUiRoot != null)
                    {
                        try { GameObject.Destroy(_testUiRoot); } catch { }
                    }
                    _testUiRoot = null;
                    _testUiLeft = null;
                    _testUiRight = null;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private static GameObject AddTestBlock(GameObject root, string name, Color color, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var rt = go.AddComponent<RectTransform>();
            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            img.color = color;
            img.raycastTarget = true;
            return go;
        }
    }
}
#endif
