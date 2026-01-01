using System;
using System.Collections.Generic;
using System.Threading;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal static class McpObjectRefs
    {
        private const int MaxRefs = 1024;
        private static readonly object Gate = new();
        private static readonly Dictionary<string, object> ById = new Dictionary<string, object>(StringComparer.Ordinal);
        private static readonly Queue<string> Order = new Queue<string>();
        private static long _nextId;
        private static readonly string Prefix = "ref:" + Guid.NewGuid().ToString("N").Substring(0, 8) + ":";

        private static bool IsNullOrWhiteSpace(string? value)
        {
            if (value == null) return true;
            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    return false;
            }
            return true;
        }

        public static string Capture(object value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            var id = Prefix + Interlocked.Increment(ref _nextId);
            lock (Gate)
            {
                ById[id] = value;
                Order.Enqueue(id);
                while (Order.Count > MaxRefs)
                {
                    var old = Order.Dequeue();
                    ById.Remove(old);
                }
            }
            return id;
        }

        public static bool TryGet(string refId, out object? value)
        {
            if (IsNullOrWhiteSpace(refId))
            {
                value = null;
                return false;
            }

            lock (Gate)
            {
                return ById.TryGetValue(refId, out value);
            }
        }

        public static bool Release(string refId)
        {
            if (IsNullOrWhiteSpace(refId)) return false;
            lock (Gate)
            {
                return ById.Remove(refId);
            }
        }
    }
}
