#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityExplorer.UI.Widgets;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Get current Unity time-scale and lock state (read-only).")]
        public static async Task<object> GetTimeScale(CancellationToken ct = default)
        {
            bool locked = false;
            float value = Time.timeScale;
            await MainThread.RunAsync(async () =>
            {
                TryGetTimeScaleState(TimeScaleWidget.Instance, out locked, out value);
                await Task.CompletedTask;
            });
            return new { ok = true, value, locked };
        }

        [McpServerTool, Description("Set Unity Time.timeScale (guarded by allowWrites + confirm).")]
        public static async Task<object> SetTimeScale(float value, bool? @lock = null, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            var clamped = Mathf.Clamp(value, 0f, 4f);
            try
            {
                bool locked = false;
                float applied = clamped;
                await MainThread.RunAsync(async () =>
                {
                    var widget = TimeScaleWidget.Instance;
                    if (@lock == true && widget != null)
                    {
                        widget.LockTo(clamped);
                    }
                    else
                    {
                        Time.timeScale = clamped;
                        if (@lock == false && widget != null)
                        {
                            UnlockTimeScale(widget);
                        }
                    }
                    TryGetTimeScaleState(widget, out locked, out applied);
                    await Task.CompletedTask;
                });
                return new { ok = true, value = applied, locked };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private static void UnlockTimeScale(TimeScaleWidget widget)
        {
            try
            {
                var lockedField = typeof(TimeScaleWidget).GetField("locked", BindingFlags.NonPublic | BindingFlags.Instance);
                lockedField?.SetValue(widget, false);
                var updateUi = typeof(TimeScaleWidget).GetMethod("UpdateUi", BindingFlags.NonPublic | BindingFlags.Instance);
                updateUi?.Invoke(widget, null);
            }
            catch { }
        }

        private static void TryGetTimeScaleState(TimeScaleWidget? widget, out bool locked, out float value)
        {
            locked = false;
            value = Time.timeScale;
            if (widget == null) return;
            try
            {
                var lockedField = typeof(TimeScaleWidget).GetField("locked", BindingFlags.NonPublic | BindingFlags.Instance);
                var lockedVal = lockedField?.GetValue(widget);
                if (lockedVal is bool b) locked = b;
            }
            catch { }
        }
    }
}
#endif
