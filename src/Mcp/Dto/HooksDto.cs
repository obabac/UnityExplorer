#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record HookDto(string Signature, bool Enabled);
#elif MONO
    public class HookDto
    {
        public string Signature { get; set; }
        public bool Enabled { get; set; }

        public HookDto()
        {
            Signature = string.Empty;
        }
    }
#endif
}
