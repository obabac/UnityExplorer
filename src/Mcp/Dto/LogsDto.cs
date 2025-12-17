using System;
using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record LogLine(DateTimeOffset T, string Level, string Message, string Source, string? Category = null);
    public record LogTailDto(IReadOnlyList<LogLine> Items);
#elif MONO
    public class LogLine
    {
        public DateTimeOffset T { get; set; }
        public string Level { get; set; }
        public string Message { get; set; }
        public string Source { get; set; }
        public string? Category { get; set; }

        public LogLine()
        {
            Level = string.Empty;
            Message = string.Empty;
            Source = string.Empty;
            Category = null;
        }
    }

    public class LogTailDto
    {
        public IList<LogLine> Items { get; set; }

        public LogTailDto()
        {
            Items = new List<LogLine>();
        }
    }
#endif
}
