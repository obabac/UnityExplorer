#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Search component member names across all objects (fields + properties).")]
        public static async Task<Page<MemberMatchDto>> SearchComponentMembers(string query, string? componentType = null, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query is required", nameof(query));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            var q = query.Trim();
            var typeFilter = string.IsNullOrWhiteSpace(componentType) ? null : componentType!.Trim();

            return await MainThread.Run(() =>
            {
                var results = new List<MemberMatchDto>(lim);
                var typeCache = new Dictionary<Type, (string typeName, (string name, string kind)[] matches)>();
                int total = 0;

                foreach (var root in UnityQuery.EnumerateAllRootGameObjects())
                {
                    foreach (var (go, path) in UnityQuery.Traverse(root))
                    {
                        UnityEngine.Component[] comps;
                        try { comps = go.GetComponents<UnityEngine.Component>(); }
                        catch { continue; }

                        foreach (var comp in comps)
                        {
                            if (comp == null) continue;
                            var t = comp.GetType();

                            if (typeFilter != null)
                            {
                                var tn = t.FullName ?? t.Name ?? string.Empty;
                                if (tn.IndexOf(typeFilter, StringComparison.OrdinalIgnoreCase) < 0)
                                    continue;
                            }

                            if (!typeCache.TryGetValue(t, out var cached))
                            {
                                var tn = t.FullName ?? t.Name ?? "<unknown>";
                                var matches = new List<(string name, string kind)>();

                                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                {
                                    if (fi.IsSpecialName) continue;
                                    if (fi.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                        matches.Add((fi.Name, "field"));
                                }

                                foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                                {
                                    if (pi.IsSpecialName) continue;
                                    if (pi.GetIndexParameters().Length > 0) continue;
                                    if (pi.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                                        matches.Add((pi.Name, "property"));
                                }

                                cached = (tn, matches.ToArray());
                                typeCache[t] = cached;
                            }

                            if (cached.matches.Length == 0)
                                continue;

                            foreach (var m in cached.matches)
                            {
                                if (total >= off && results.Count < lim)
                                {
                                    results.Add(new MemberMatchDto(
                                        ObjectId: $"obj:{go.GetInstanceID()}",
                                        ObjectPath: path,
                                        ComponentType: cached.typeName,
                                        Member: m.name,
                                        Kind: m.kind));
                                }

                                total++;
                                if (results.Count >= lim)
                                    break;
                            }

                            if (results.Count >= lim)
                                break;
                        }

                        if (results.Count >= lim)
                            break;
                    }

                    if (results.Count >= lim)
                        break;
                }

                return new Page<MemberMatchDto>(total, results);
            });
        }
    }
}
#endif

