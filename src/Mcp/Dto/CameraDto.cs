#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record CameraInfoDto(bool IsFreecam, string Name, float Fov, Vector3Dto Pos, Vector3Dto Rot);
#elif MONO
    public class CameraInfoDto
    {
        public bool IsFreecam { get; set; }
        public string Name { get; set; }
        public float Fov { get; set; }
        public Vector3Dto Pos { get; set; }
        public Vector3Dto Rot { get; set; }

        public CameraInfoDto()
        {
            Name = string.Empty;
            Pos = new Vector3Dto();
            Rot = new Vector3Dto();
        }
    }
#endif
}
