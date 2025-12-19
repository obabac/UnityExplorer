#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public Page<StaticClassDto> SearchStaticClasses(string query, int? limit, int? offset)
        {
            if (string.IsNullOrEmpty(query) || query.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "query is required");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);
            var q = query.Trim();

            return MainThread.Run(() =>
            {
                var results = new List<StaticClassDto>(lim);
                var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                int total = 0;

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in asm.TryGetTypes().Where(IsStaticClass))
                    {
                        var fullName = type.FullName;
                        if (string.IsNullOrEmpty(fullName) || fullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        var id = "type:" + fullName;
                        if (ids.Contains(id))
                            continue;

                        ids.Add(id);
                        total++;

                        if (total > off && results.Count < lim)
                        {
                            var assemblyName = asm.GetName().Name ?? string.Empty;
                            var memberCount = CountStaticFieldsAndProperties(type);
                            results.Add(new StaticClassDto { Id = id, Type = fullName!, Assembly = assemblyName, MemberCount = memberCount });
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

        public Page<InspectorMemberDto> ListStaticMembers(string typeFullName, bool includeMethods = false, int? limit = null, int? offset = null)
        {
            if (string.IsNullOrEmpty(typeFullName) || typeFullName.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "typeFullName is required");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var type = ResolveStaticType(typeFullName);
                if (type == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Type not found");

                var members = new List<InspectorMemberDto>();
                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;

                foreach (var fi in type.GetFields(flags))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto { Name = fi.Name, Kind = "field", Type = SafeTypeName(fi.FieldType), CanRead = true, CanWrite = canWrite });
                }

                foreach (var pi in type.GetProperties(flags))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto { Name = pi.Name, Kind = "property", Type = SafeTypeName(pi.PropertyType), CanRead = canRead, CanWrite = canWrite });
                }

                if (includeMethods)
                {
                    foreach (var mi in type.GetMethods(flags))
                    {
                        if (mi.IsSpecialName) continue;
                        members.Add(new InspectorMemberDto { Name = mi.Name, Kind = "method", Type = FormatMethodType(mi), CanRead = false, CanWrite = false });
                    }
                }

                var total = members.Count;
                var items = members.Skip(off).Take(lim).ToList();
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
