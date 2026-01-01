#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UniverseLib.Utility;
#if CPP
using Il2CppInterop.Runtime;
#endif

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Search objects across scenes by name/type/path.")]
        public static async Task<Page<ObjectCardDto>> SearchObjects(string? query, string? name, string? type, string? path, bool? activeOnly, int? limit, int? offset, CancellationToken ct)
        {
            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            var typeResolved = ResolveComponentType(type);
#if CPP
            var typeResolvedIl2Cpp = typeResolved != null ? Il2CppType.From(typeResolved) : null;
#endif

            return await MainThread.Run(() =>
            {
                var results = new List<ObjectCardDto>(lim);
                int matched = 0;
                foreach (var root in UnityQuery.EnumerateAllRootGameObjects())
                {
                    foreach (var (go, p) in UnityQuery.Traverse(root))
                    {
                        if (activeOnly == true && !go.activeInHierarchy) continue;
                        var nm = go.name ?? string.Empty;
                        var match = true;
                        if (!string.IsNullOrEmpty(query))
                            match &= nm.Contains(query!, StringComparison.OrdinalIgnoreCase) || p.Contains(query!, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(name))
                            match &= nm.Contains(name!, StringComparison.OrdinalIgnoreCase);
                        if (!string.IsNullOrEmpty(type))
                        {
                            if (typeResolved != null)
                            {
#if CPP
                                match &= typeResolvedIl2Cpp != null
                                    ? go.GetComponent(typeResolvedIl2Cpp) != null
                                    : go.GetComponent(type!) != null;
#else
                                match &= go.GetComponent(typeResolved) != null;
#endif
                            }
                            else
                            {
                                match &= go.GetComponent(type!) != null;
                            }
                        }
                        if (!string.IsNullOrEmpty(path))
                            match &= p.Contains(path!, StringComparison.OrdinalIgnoreCase);
                        if (!match) continue;

                        if (matched >= off && results.Count < lim)
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
                        matched++;
                        if (results.Count >= lim) break;
                    }
                    if (results.Count >= lim) break;
                }

                return new Page<ObjectCardDto>(matched, results);
            });
        }

        private static Type? ResolveComponentType(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            var q = typeName.Trim();

            if (q.IndexOf('.') >= 0)
            {
                try
                {
                    var t = UniverseLib.ReflectionUtility.GetTypeByName(q);
                    return t != null && typeof(UnityEngine.Component).IsAssignableFrom(t) ? t : null;
                }
                catch
                {
                    return null;
                }
            }

            Type? found = null;
            int matches = 0;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var t in asm.TryGetTypes())
                    {
                        if (t == null) continue;
                        if (!typeof(UnityEngine.Component).IsAssignableFrom(t)) continue;
                        if (!string.Equals(t.Name, q, StringComparison.OrdinalIgnoreCase)) continue;
                        matches++;
                        if (matches == 1) found = t;
                        else return null; // ambiguous
                    }
                }
            }
            catch
            {
                return null;
            }

            return found;
        }
    }
}
#endif
