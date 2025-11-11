using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityExplorer.Mcp
{
#if INTEROP
    internal static class LogBuffer
    {
        private static readonly object Gate = new();
        private static readonly Queue<LogLine> Buffer = new();
        private const int MaxLines = 2000;

        public static void Add(string level, string message)
        {
            lock (Gate)
            {
                Buffer.Enqueue(new LogLine(DateTimeOffset.UtcNow, level, message));
                while (Buffer.Count > MaxLines)
                    Buffer.Dequeue();
            }
            try
            {
                var http = McpSimpleHttp.Current;
                if (http != null)
                {
                    _ = http.BroadcastNotificationAsync("log", new { level, message, t = DateTimeOffset.UtcNow });
                }
            }
            catch { }
        }

        public static LogTailDto Tail(int count)
        {
            lock (Gate)
            {
                var items = Buffer.Reverse().Take(Math.Max(1, count)).Reverse().ToArray();
                return new LogTailDto(items);
            }
        }
    }
#endif
}
