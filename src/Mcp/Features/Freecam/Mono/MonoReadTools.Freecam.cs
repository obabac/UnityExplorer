#if MONO && !INTEROP
#nullable enable
using UnityEngine;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public FreecamDto GetFreecam()
        {
            return MainThread.Run(BuildFreecamDto);
        }

        internal static FreecamDto BuildFreecamDto()
        {
            var enabled = FreeCamPanel.inFreeCamMode;
            var usingGameCamera = FreeCamPanel.usingGameCamera;
            var speed = FreeCamPanel.desiredMoveSpeed;

            Vector3 pos = Vector3.zero;
            Vector3 rot = Vector3.zero;

            if (enabled && FreeCamPanel.ourCamera != null)
            {
                var t = FreeCamPanel.ourCamera.transform;
                pos = t.position;
                rot = t.eulerAngles;
            }
            else if (FreeCamPanel.currentUserCameraPosition.HasValue && FreeCamPanel.currentUserCameraRotation.HasValue)
            {
                pos = FreeCamPanel.currentUserCameraPosition.Value;
                rot = FreeCamPanel.currentUserCameraRotation.Value.eulerAngles;
            }
            else if (Camera.main != null)
            {
                pos = Camera.main.transform.position;
                rot = Camera.main.transform.eulerAngles;
            }
            else if (Camera.allCamerasCount > 0)
            {
                var cam = Camera.allCameras[0];
                pos = cam.transform.position;
                rot = cam.transform.eulerAngles;
            }

            return new FreecamDto
            {
                Enabled = enabled,
                UsingGameCamera = usingGameCamera,
                Speed = speed,
                Pos = new Vector3Dto { X = pos.x, Y = pos.y, Z = pos.z },
                Rot = new Vector3Dto { X = rot.x, Y = rot.y, Z = rot.z }
            };
        }
    }
}
#endif
