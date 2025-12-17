#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Search objects across scenes by name/type/path.")]
        public static async Task<Page<ObjectCardDto>> SearchObjects(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int total = 0;
                foreach (var root in UnityQuery.EnumerateAllRootGameObjects())
                {
                    foreach (var (go, p) in UnityQuery.Traverse(root))
                    {
                        if (activeOnly == true && !go.activeInHierarchy) { total++; continue; }
                        var nm = go.name ?? string.Empty;
                        var match = true;
                        if (!string.IsNullOrEmpty(query))
                            match &= nm.Contains(query!, StringComparison.OrdinalIgnoreCase) || p.Contains(query!, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(name))
                            match &= nm.Contains(name!, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(type))
                            match &= go.GetComponent(type!) != null;
                        if (!string.IsNullOrEmpty(path))
                            match &= p.Contains(path!, StringComparison.OrdinalIgnoreCase);
                        if (!match) { total++; continue; }

                        if (total >= off && results.Count < lim)
                        {
                            int compCount = 0;
                            try { var comps3 = go.GetComponents<UnityEngine.Component>(); compCount = comps3 != null ? comps3.Length : 0; } catch { }
                            results.Add(new ObjectCardDto(
                                Id: $"obj:{go.GetInstanceID()}",
                                Name: nm,
                                Path: p,
                                Tag: SafeTag(go),
                                Layer: go.layer,
                                Active: go.activeSelf,
                                ComponentCount: compCount
                            ));
                        }
                        total++;
                        if (results.Count >= lim) break;
                    }
                    if (results.Count >= lim) break;
                }

                return new Page<ObjectCardDto>(total, results);
            });
        }
    }
}
#endif
