using FluentAssertions;
using Xunit;

namespace UnityExplorer.Mcp.ContractTests
{
    public class DtoSchemaContractTests
    {
        [Fact]
        public void Dto_Type_References_Are_Internal_To_Mcp_Namespace()
        {
            // Smoke guard to ensure MCP DTOs live under UnityExplorer.Mcp and avoid leaking UnityEngine types.
            typeof(UnityExplorer.Mcp.Dto).Namespace.Should().Be("UnityExplorer.Mcp");
        }
    }
}
