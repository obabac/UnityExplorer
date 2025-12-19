#if MONO && !INTEROP
#nullable enable
using System;
using UnityEngine;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoWriteTools
    {
        public object SetFreecamEnabled(bool enabled, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (enabled)
                        FreeCamPanel.BeginFreecam();
                    else
                        FreeCamPanel.EndFreecam();
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SetFreecamSpeed(float speed, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            var clamped = Mathf.Clamp(speed, 0f, 1000f);
            try
            {
                MainThread.Run(() => FreeCamPanel.desiredMoveSpeed = clamped);
                return new { ok = true, speed = clamped };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SetFreecamPose(Vector3Dto pos, Vector3Dto rot, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (pos == null || rot == null) return ToolError("InvalidArgument", "pos and rot are required");

            try
            {
                MainThread.Run(() =>
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
