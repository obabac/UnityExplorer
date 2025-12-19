#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record SingletonDto(
        string Id,
        string DeclaringType,
        string InstanceType,
        string Preview,
        string? ObjectId);
#elif MONO
    public sealed class SingletonDto
    {
        public string Id { get; set; }
        public string DeclaringType { get; set; }
        public string InstanceType { get; set; }
        public string Preview { get; set; }
        public string? ObjectId { get; set; }

        public SingletonDto()
        {
            Id = string.Empty;
            DeclaringType = string.Empty;
            InstanceType = string.Empty;
            Preview = string.Empty;
        }
    }
#endif
}
