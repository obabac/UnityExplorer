using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record PickHit(string Id, string Name, string Path);
    public record PickResultDto(string Mode, bool Hit, string? Id, IReadOnlyList<PickHit>? Items);
#elif MONO
    public class PickHit
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }

        public PickHit()
        {
            Id = string.Empty;
            Name = string.Empty;
            Path = string.Empty;
        }
    }

    public class PickResultDto
    {
        public string Mode { get; set; }
        public bool Hit { get; set; }
        public string? Id { get; set; }
        public IList<PickHit>? Items { get; set; }

        public PickResultDto()
        {
            Mode = string.Empty;
            Items = null;
            Id = null;
        }
    }
#endif
}
