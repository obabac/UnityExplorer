#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record SceneDto(string Id, string Name, int Index, bool IsLoaded, int RootCount);
#elif MONO
    public class SceneDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Index { get; set; }
        public bool IsLoaded { get; set; }
        public int RootCount { get; set; }

        public SceneDto()
        {
            Id = string.Empty;
            Name = string.Empty;
        }
    }
#endif
}
