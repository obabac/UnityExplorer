#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        private static GameObject? _testUiRoot;
        private static GameObject? _testUiLeft;
        private static GameObject? _testUiRight;

        private static string ObjectId(GameObject go) => $"obj:{go.GetInstanceID()}";

        [McpServerTool, Description("Spawn a simple UI canvas with raycastable elements for testing MousePick (requires allowWrites + confirm).")]
        public static async Task<object> SpawnTestUi(bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    if (_testUiRoot != null)
                    {
                        if (_testUiLeft == null)
                        {
                            var foundLeft = _testUiRoot.transform.Find("McpTestBlock_Left");
                            if (foundLeft != null) _testUiLeft = foundLeft.gameObject;
                        }
                        if (_testUiRight == null)
                        {
                            var foundRight = _testUiRoot.transform.Find("McpTestBlock_Right");
                            if (foundRight != null) _testUiRight = foundRight.gameObject;
                        }
                        await Task.CompletedTask;
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

                    GameObject AddBlock(string name, Color color, Vector2 anchor, Vector2 size)
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

                    _testUiRoot = root;
                    _testUiLeft = AddBlock("McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                    _testUiRight = AddBlock("McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                    await Task.CompletedTask;
                });

                var blocks = new List<object>();
                if (_testUiLeft != null) blocks.Add(new { name = _testUiLeft.name, id = ObjectId(_testUiLeft) });
                if (_testUiRight != null) blocks.Add(new { name = _testUiRight.name, id = ObjectId(_testUiRight) });

                return new { ok = true, rootId = _testUiRoot != null ? ObjectId(_testUiRoot) : null, blocks = blocks.ToArray() };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Destroy the test UI canvas created by SpawnTestUi (if present).")]
        public static async Task<object> DestroyTestUi(bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    if (_testUiRoot != null)
                    {
                        try { GameObject.Destroy(_testUiRoot); } catch { }
                    }
                    _testUiRoot = null;
                    _testUiLeft = null;
                    _testUiRight = null;
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }
    }
}
#endif
