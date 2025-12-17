using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record StatusDto(
        string Version,
        string UnityVersion,
        string Platform,
        string Runtime,
        string ExplorerVersion,
        bool Ready,
        int ScenesLoaded,
        IReadOnlyList<string> Selection);
#elif MONO
    public class StatusDto
    {
        public string Version { get; set; }
        public string UnityVersion { get; set; }
        public string Platform { get; set; }
        public string Runtime { get; set; }
        public string ExplorerVersion { get; set; }
        public bool Ready { get; set; }
        public int ScenesLoaded { get; set; }
        public IList<string> Selection { get; set; }

        public StatusDto()
        {
            Version = string.Empty;
            UnityVersion = string.Empty;
            Platform = string.Empty;
            Runtime = string.Empty;
            ExplorerVersion = string.Empty;
            Selection = new List<string>();
        }
    }
#endif
}
