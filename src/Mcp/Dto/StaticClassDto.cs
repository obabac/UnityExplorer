#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record StaticClassDto(
        string Id,
        string Type,
        string Assembly,
        int MemberCount);
#elif MONO
    public sealed class StaticClassDto
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Assembly { get; set; }
        public int MemberCount { get; set; }

        public StaticClassDto()
        {
            Id = string.Empty;
            Type = string.Empty;
            Assembly = string.Empty;
        }
    }
#endif
}
