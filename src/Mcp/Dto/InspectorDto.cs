#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record InspectorMemberDto(string Name, string Kind, string Type, bool CanRead, bool CanWrite);
#elif MONO
    public class InspectorMemberDto
    {
        public string Name { get; set; }
        public string Kind { get; set; }
        public string Type { get; set; }
        public bool CanRead { get; set; }
        public bool CanWrite { get; set; }

        public InspectorMemberDto()
        {
            Name = string.Empty;
            Kind = string.Empty;
            Type = string.Empty;
        }
    }
#endif
}
