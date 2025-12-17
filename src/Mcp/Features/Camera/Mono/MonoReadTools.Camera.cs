#if MONO && !INTEROP
#nullable enable
using UnityEngine;
using UnityExplorer.UI.Panels;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public CameraInfoDto GetCameraInfo()
        {
            return MainThread.Run(() =>
            {
                var freecam = FreeCamPanel.inFreeCamMode;
                Camera cam = null;

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
                    return new CameraInfoDto
                    {
                        IsFreecam = freecam,
                        Name = "<none>",
                        Fov = 0f,
                        Pos = new Vector3Dto { X = 0f, Y = 0f, Z = 0f },
                        Rot = new Vector3Dto { X = 0f, Y = 0f, Z = 0f }
                    };

                var pos = cam.transform.position;
                var rot = cam.transform.eulerAngles;
                return new CameraInfoDto
                {
                    IsFreecam = freecam,
                    Name = cam.name,
                    Fov = cam.fieldOfView,
                    Pos = new Vector3Dto { X = pos.x, Y = pos.y, Z = pos.z },
                    Rot = new Vector3Dto { X = rot.x, Y = rot.y, Z = rot.z }
                };
            });
        }
    }
}
#endif
