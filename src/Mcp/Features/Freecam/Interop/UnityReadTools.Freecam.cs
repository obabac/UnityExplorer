#if INTEROP
#nullable enable
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Get freecam state (enabled, pose, speed).")]
        public static async Task<FreecamDto> GetFreecam(CancellationToken ct)
        {
            return await MainThread.Run(BuildFreecamDto);
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

            return new FreecamDto(
                enabled,
                usingGameCamera,
                speed,
                new Vector3Dto(pos.x, pos.y, pos.z),
                new Vector3Dto(rot.x, rot.y, rot.z));
        }
    }
}
#endif
