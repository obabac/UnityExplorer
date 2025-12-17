#if !INTEROP && !MONO
#nullable enable
using System;

namespace UnityExplorer.Mcp
{
    // Stub implementation for non-INTEROP/non-MONO targets so that builds that reference UnityExplorer.Mcp.McpSimpleHttp still compile.
    internal sealed partial class McpSimpleHttp : IDisposable
    {
        public static McpSimpleHttp? Current { get; private set; }
        public int Port { get; }

        public McpSimpleHttp(string bindAddress, int port)
        {
            Port = port;
        }

        public void Start()
        {
            Current = this;
        }

        public void Dispose()
        {
            Current = null;
        }
    }
}
#endif
