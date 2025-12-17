using System.Collections.Generic;

#nullable enable

namespace UnityExplorer.Mcp
{
#if INTEROP
    public record Page<T>(int Total, IReadOnlyList<T> Items);
#elif MONO
    public class Page<T>
    {
        public int Total { get; set; }
        public IList<T> Items { get; set; }

        public Page()
            : this(0, new List<T>())
        {
        }

        public Page(int total, IList<T> items)
        {
            Total = total;
            Items = items ?? new List<T>();
        }
    }
#endif
}
