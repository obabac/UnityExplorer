using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace UnityExplorer.Mcp.ContractTests
{
    public class DtoSchemaContractTests
    {
        [Fact]
        public void Dto_File_Uses_UnityExplorer_Mcp_Namespace()
        {
            var repoRoot = FindRepoRoot();
            var dtoPath = Path.Combine(repoRoot, "src", "Mcp", "Dto.cs");

            File.Exists(dtoPath).Should().BeTrue("Dto.cs should exist in src/Mcp");

            var text = File.ReadAllText(dtoPath);
            text.Should().Contain("namespace UnityExplorer.Mcp");
        }

        private static string FindRepoRoot()
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, ".git")))
                    return dir;

                var parent = Directory.GetParent(dir)?.FullName;
                if (string.IsNullOrEmpty(parent) || parent == dir)
                    break;

                dir = parent;
            }

            throw new InvalidOperationException("Repo root could not be located from test directory.");
        }
    }
}
