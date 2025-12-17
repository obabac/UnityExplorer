using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record SelectionDto(string? ActiveId, IReadOnlyList<string> Items);
#elif MONO
    public class SelectionDto
    {
        public string? ActiveId { get; set; }
        public IList<string> Items { get; set; }

        public SelectionDto()
        {
            Items = new List<string>();
        }
    }
#endif
}
