#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record VersionInfoDto(string ExplorerVersion, string McpVersion, string UnityVersion, string Runtime);
#elif MONO
    public class VersionInfoDto
    {
        public string ExplorerVersion { get; set; }
        public string McpVersion { get; set; }
        public string UnityVersion { get; set; }
        public string Runtime { get; set; }

        public VersionInfoDto()
        {
            ExplorerVersion = string.Empty;
            McpVersion = string.Empty;
            UnityVersion = string.Empty;
            Runtime = string.Empty;
        }
    }
#endif
}
