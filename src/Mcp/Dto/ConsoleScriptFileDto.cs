#nullable enable

using System;

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record ConsoleScriptFileDto(
        string Name,
        string Path,
        string Content,
        long SizeBytes,
        DateTimeOffset LastModifiedUtc,
        bool Truncated);
#elif MONO
    public class ConsoleScriptFileDto
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public string Content { get; set; }
        public long SizeBytes { get; set; }
        public DateTimeOffset LastModifiedUtc { get; set; }
        public bool Truncated { get; set; }

        public ConsoleScriptFileDto()
        {
            Name = string.Empty;
            Path = string.Empty;
            Content = string.Empty;
            SizeBytes = 0;
            LastModifiedUtc = default;
            Truncated = false;
        }
    }
#endif
}

