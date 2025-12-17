#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record HookMethodDto(string Name, string Signature);
#elif MONO
    public class HookMethodDto
    {
        public string Name { get; set; }
        public string Signature { get; set; }

        public HookMethodDto()
        {
            Name = string.Empty;
            Signature = string.Empty;
        }

        public HookMethodDto(string name, string signature)
        {
            Name = name ?? string.Empty;
            Signature = signature ?? string.Empty;
        }
    }
#endif
}

