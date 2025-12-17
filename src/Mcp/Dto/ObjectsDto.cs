#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record ObjectCardDto(string Id, string Name, string Path, string Tag, int Layer, bool Active, int ComponentCount);
    public record ComponentCardDto(string Type, string? Summary);
#elif MONO
    public class ObjectCardDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Tag { get; set; }
        public int Layer { get; set; }
        public bool Active { get; set; }
        public int ComponentCount { get; set; }

        public ObjectCardDto()
        {
            Id = string.Empty;
            Name = string.Empty;
            Path = string.Empty;
            Tag = string.Empty;
        }
    }

    public class ComponentCardDto
    {
        public string Type { get; set; }
        public string Summary { get; set; }

        public ComponentCardDto()
        {
            Type = string.Empty;
            Summary = string.Empty;
        }
    }
#endif
}
