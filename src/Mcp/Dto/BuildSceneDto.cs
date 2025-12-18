#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record BuildSceneDto(int Index, string Name, string Path);
#elif MONO
    public class BuildSceneDto
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }

        public BuildSceneDto()
        {
            Name = string.Empty;
            Path = string.Empty;
        }
    }
#endif
}
