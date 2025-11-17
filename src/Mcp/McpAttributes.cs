using System;

// Minimal attribute definitions to tag MCP tools without taking a dependency
// on the external ModelContextProtocol SDK at runtime.

namespace UnityExplorer.Mcp
{
#if INTEROP
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    internal sealed class McpServerToolTypeAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class McpServerToolAttribute : Attribute
    {
        public McpServerToolAttribute() { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    internal sealed class McpServerResourceTypeAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class McpServerResourceAttribute : Attribute
    {
        public McpServerResourceAttribute() { }
    }
#endif
}
