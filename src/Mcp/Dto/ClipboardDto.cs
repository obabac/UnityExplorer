using System;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record ClipboardDto(bool HasValue, string? Type, string? Preview, string? ObjectId);
#elif MONO
    public class ClipboardDto
    {
        public bool HasValue { get; set; }
        public string? Type { get; set; }
        public string? Preview { get; set; }
        public string? ObjectId { get; set; }
    }
#endif
}
