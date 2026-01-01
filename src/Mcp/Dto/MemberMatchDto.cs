#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record MemberMatchDto(string ObjectId, string ObjectPath, string ComponentType, string Member, string Kind);
#elif MONO
    public class MemberMatchDto
    {
        public string ObjectId { get; set; }
        public string ObjectPath { get; set; }
        public string ComponentType { get; set; }
        public string Member { get; set; }
        public string Kind { get; set; }

        public MemberMatchDto()
        {
            ObjectId = string.Empty;
            ObjectPath = string.Empty;
            ComponentType = string.Empty;
            Member = string.Empty;
            Kind = string.Empty;
        }
    }
#endif
}

