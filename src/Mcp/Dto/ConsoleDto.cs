#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record ConsoleScriptDto(string Name, string Path);
#elif MONO
    public class ConsoleScriptDto
    {
        public string Name { get; set; }
        public string Path { get; set; }

        public ConsoleScriptDto()
        {
            Name = string.Empty;
            Path = string.Empty;
        }
    }
#endif
}
