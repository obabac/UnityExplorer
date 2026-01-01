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
using UniverseLib.Utility;
using UnityExplorer.ObjectExplorer;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        private const int SingletonPreviewLimit = 256;

        [McpServerTool, Description("Search singleton instances by declaring type.")]
        public static async Task<Page<SingletonDto>> SearchSingletons(string query, int? limit, int? offset, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query is required", nameof(query));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            var q = query.Trim();

            return await MainThread.Run(() =>
            {
                var results = new List<SingletonDto>(lim);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int total = 0;
                int errors = 0;
                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                var tmpList = new List<object>();

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in asm.TryGetTypes().Where(t => !(t.IsSealed && t.IsAbstract) && !t.IsEnum))
                    {
                        var fullName = type.FullName;
                        if (string.IsNullOrEmpty(fullName) || !fullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                            continue;

                        tmpList.Clear();
                        try
                        {
                            ReflectionUtility.FindSingleton(SearchProvider.instanceNames, type, flags, tmpList);
                        }
                        catch
                        {
                            errors++;
                            continue;
                        }
                        if (tmpList.Count == 0)
                            continue;

                        var declaringTypeName = fullName!;
                        var id = $"singleton:{declaringTypeName}";
                        if (ids.Contains(id))
                            continue;

                        ids.Add(id);
                        total++;

                        var instance = tmpList[0];
                        var instanceType = instance?.GetActualType() ?? instance?.GetType();
                        var instanceTypeName = instanceType?.FullName ?? instanceType?.Name ?? "<unknown>";
                        var preview = SafeSingletonPreview(instance);

                        string? objectId = null;
                        if (instance is GameObject go && go != null)
                        {
                            objectId = $"obj:{go.GetInstanceID()}";
                        }

                        if (total > off && results.Count < lim)
                        {
                            results.Add(new SingletonDto(
                                Id: id,
                                DeclaringType: declaringTypeName,
                                InstanceType: instanceTypeName,
                                Preview: preview,
                                ObjectId: objectId));
                        }

                        if (results.Count >= lim)
                            break;
                    }

                    if (results.Count >= lim)
                        break;
                }

                if (errors > 0)
                {
                    try { ExplorerCore.LogWarning($"[MCP] SearchSingletons: {errors} errors during scan for '{q}'."); }
                    catch { }
                }

                return new Page<SingletonDto>(total, results);
            });
        }

        private static string SafeSingletonPreview(object? value)
        {
            string text;
            try
            {
                text = value == null ? "<null>" : (ToStringUtility.ToStringWithType(value, typeof(object), false) ?? value.ToString() ?? "<null>");
            }
            catch (Exception ex)
            {
                text = $"<error: {ex.GetType().Name}>";
            }
            if (text.Length > SingletonPreviewLimit)
                return text.Substring(0, SingletonPreviewLimit) + "...";
            return text;
        }
    }
}
#endif
