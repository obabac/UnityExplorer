#if INTEROP
#nullable enable
using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    public static partial class UnityWriteTools
    {
        [McpServerTool, Description("Enable or disable UnityExplorer freecam (guarded).")]
        public static async Task<object> SetFreecamEnabled(bool enabled, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    if (enabled)
                        FreeCamPanel.BeginFreecam();
                    else
                        FreeCamPanel.EndFreecam();
                    await Task.CompletedTask;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Set freecam move speed (guarded).")]
        public static async Task<object> SetFreecamSpeed(float speed, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            var clamped = Mathf.Clamp(speed, 0f, 1000f);
            try
            {
                await MainThread.RunAsync(async () =>
                {
                    FreeCamPanel.desiredMoveSpeed = clamped;
                    await Task.CompletedTask;
                });
                return new { ok = true, speed = clamped };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        [McpServerTool, Description("Set freecam pose (position + euler rotation, guarded).")]
        public static async Task<object> SetFreecamPose(Vector3Dto pos, Vector3Dto rot, bool confirm = false, CancellationToken ct = default)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (pos == null || rot == null) return ToolError("InvalidArgument", "pos and rot are required");

            try
            {
                await MainThread.RunAsync(async () =>
                {
                    var posVec = new Vector3(pos.X, pos.Y, pos.Z);
                    var rotEuler = new Vector3(rot.X, rot.Y, rot.Z);
                    var rotQuat = Quaternion.Euler(rotEuler);

                    FreeCamPanel.currentUserCameraPosition = posVec;
                    FreeCamPanel.currentUserCameraRotation = rotQuat;

                    if (FreeCamPanel.ourCamera != null)
                    {
                        FreeCamPanel.ourCamera.transform.position = posVec;
                        FreeCamPanel.ourCamera.transform.rotation = rotQuat;
                        FreeCamPanel.lastSetCameraPosition = posVec;
                    }
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
