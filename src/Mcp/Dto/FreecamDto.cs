#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record FreecamDto(bool Enabled, bool UsingGameCamera, float Speed, Vector3Dto Pos, Vector3Dto Rot);
#elif MONO
    public class FreecamDto
    {
        public bool Enabled { get; set; }
        public bool UsingGameCamera { get; set; }
        public float Speed { get; set; }
        public Vector3Dto Pos { get; set; }
        public Vector3Dto Rot { get; set; }

        public FreecamDto()
        {
            Pos = new Vector3Dto();
            Rot = new Vector3Dto();
        }
    }
#endif
}
