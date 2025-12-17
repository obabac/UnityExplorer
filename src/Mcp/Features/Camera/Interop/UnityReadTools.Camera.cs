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
        [McpServerTool, Description("Get active camera information (name, FOV, position, rotation).")]
        public static async Task<CameraInfoDto> GetCameraInfo(CancellationToken ct)
        {
            return await MainThread.Run(() =>
            {
                var freecam = FreeCamPanel.inFreeCamMode;
                Camera? cam = null;

                if (freecam)
                {
                    if (FreeCamPanel.ourCamera != null)
                    {
                        cam = FreeCamPanel.ourCamera;
                    }
                    else if (FreeCamPanel.lastMainCamera != null)
                    {
                        cam = FreeCamPanel.lastMainCamera;
                    }
                    else if (Camera.main != null)
                    {
                        cam = Camera.main;
                    }
                }

                if (cam == null && Camera.main != null)
                    cam = Camera.main;
                if (cam == null && Camera.allCamerasCount > 0)
                    cam = Camera.allCameras[0];

                if (cam == null)
                    return new CameraInfoDto(freecam, "<none>", 0f, new Vector3Dto(0, 0, 0), new Vector3Dto(0, 0, 0));

                var pos = cam.transform.position;
                var rot = cam.transform.eulerAngles;
                return new CameraInfoDto(freecam, cam.name, cam.fieldOfView,
                    new Vector3Dto(pos.x, pos.y, pos.z),
                    new Vector3Dto(rot.x, rot.y, rot.z));
            });
        }
    }
}
#endif
