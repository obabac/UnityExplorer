#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record TransformDto(Vector3Dto Pos, Vector3Dto Rot, Vector3Dto Scale);
    public record Vector3Dto(float X, float Y, float Z);
#elif MONO
    public class TransformDto
    {
        public Vector3Dto Pos { get; set; }
        public Vector3Dto Rot { get; set; }
        public Vector3Dto Scale { get; set; }

        public TransformDto()
        {
            Pos = new Vector3Dto();
            Rot = new Vector3Dto();
            Scale = new Vector3Dto();
        }
    }

    public class Vector3Dto
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }
#endif
}
