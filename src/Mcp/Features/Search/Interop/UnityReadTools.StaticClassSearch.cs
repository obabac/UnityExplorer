#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Search static classes by full name.")]
        public static async Task<Page<StaticClassDto>> SearchStaticClasses(string query, int? limit, int? offset, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("query is required", nameof(query));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            var q = query.Trim();

            return await MainThread.Run(() =>
            {
                var results = new List<StaticClassDto>(lim);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int total = 0;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in asm.TryGetTypes().Where(IsStaticClass))
                    {
                        var fullName = type.FullName;
                        if (string.IsNullOrEmpty(fullName) || !fullName.Contains(q, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var id = $"type:{fullName}";
                        if (ids.Contains(id))
                            continue;

                        ids.Add(id);
                        total++;

                        if (total > off && results.Count < lim)
                        {
                            var assemblyName = asm.GetName().Name ?? string.Empty;
                            var memberCount = CountStaticFieldsAndProperties(type);
                            results.Add(new StaticClassDto(id, fullName!, assemblyName, memberCount));
                        }

                        if (results.Count >= lim)
                            break;
                    }

                    if (results.Count >= lim)
                        break;
                }

                return new Page<StaticClassDto>(total, results);
            });
        }

        [McpServerTool, Description("List static members for a static class (fields/properties, methods optional).")]
        public static async Task<Page<InspectorMemberDto>> ListStaticMembers(string typeFullName, bool includeMethods = false, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
                throw new ArgumentException("typeFullName is required", nameof(typeFullName));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var type = ResolveStaticType(typeFullName);
                if (type == null)
                    throw new InvalidOperationException("NotFound");

                var members = new List<InspectorMemberDto>();
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

                foreach (var fi in type.GetFields(flags))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto(fi.Name, "field", SafeTypeName(fi.FieldType), true, canWrite));
                }

                foreach (var pi in type.GetProperties(flags))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto(pi.Name, "property", SafeTypeName(pi.PropertyType), canRead, canWrite));
                }

                if (includeMethods)
                {
                    foreach (var mi in type.GetMethods(flags))
                    {
                        if (mi.IsSpecialName) continue;
                        members.Add(new InspectorMemberDto(mi.Name, "method", FormatMethodType(mi), false, false));
                    }
                }

                var total = members.Count;
                var items = members.Skip(off).Take(lim).ToArray();
                return new Page<InspectorMemberDto>(total, items);
            });
        }

        private static bool IsStaticClass(Type t)
            => t.IsClass && t.IsAbstract && t.IsSealed && !t.IsEnum;

        private static int CountStaticFieldsAndProperties(Type type)
        {
            var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            int count = 0;
            foreach (var fi in type.GetFields(flags))
            {
                if (fi.IsSpecialName) continue;
                count++;
            }
            foreach (var pi in type.GetProperties(flags))
            {
                if (pi.IsSpecialName) continue;
                if (pi.GetIndexParameters().Length > 0) continue;
                count++;
            }
            return count;
        }

        private static Type? ResolveStaticType(string typeFullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in asm.TryGetTypes())
                {
                    if (!IsStaticClass(type))
                        continue;
                    if (string.Equals(type.FullName, typeFullName, StringComparison.Ordinal))
                        return type;
                }
            }
            return null;
        }
    }
}
#endif
