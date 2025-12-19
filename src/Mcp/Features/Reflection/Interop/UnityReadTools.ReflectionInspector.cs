#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityExplorer.ObjectExplorer;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Read a static field or property value (safe, bounded summary).")]
        public static async Task<object> ReadStaticMember(string typeFullName, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
                throw new ArgumentException("typeFullName is required", nameof(typeFullName));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("name is required", nameof(name));

            return await MainThread.Run(() =>
            {
                var type = ReflectionUtility.GetTypeByName(typeFullName);
                if (type == null)
                    throw new InvalidOperationException("NotFound");

                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
                var fi = type.GetField(name, flags);
                if (fi != null)
                {
                    var val = fi.GetValue(null);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.text, valueJson = summary.json };
                }

                var pi = type.GetProperty(name, flags);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null || !getter.IsStatic)
                        throw new InvalidOperationException("NotFound");
                    var val = getter.Invoke(null, Array.Empty<object>());
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.text, valueJson = summary.json };
                }

                throw new InvalidOperationException("NotFound");
            });
        }

        [McpServerTool, Description("List members for a singleton instance (fields/properties, methods optional).")]
        public static async Task<Page<InspectorMemberDto>> ListSingletonMembers(string singletonId, bool includeMethods = false, int? limit = null, int? offset = null, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(singletonId) || !singletonId.StartsWith("singleton:"))
                throw new ArgumentException("Invalid singletonId; expected 'singleton:<declaringTypeFullName>'", nameof(singletonId));

            var declaringTypeName = singletonId.Substring("singleton:".Length);
            if (string.IsNullOrWhiteSpace(declaringTypeName))
                throw new ArgumentException("Invalid singletonId; declaring type missing", nameof(singletonId));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var instance = ResolveSingletonInstance(declaringTypeName);
                if (instance == null)
                    throw new InvalidOperationException("NotFound");

                var t = instance.GetType();
                var members = new List<InspectorMemberDto>();

                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto(fi.Name, "field", SafeTypeName(fi.FieldType), true, canWrite));
                }

                foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto(pi.Name, "property", SafeTypeName(pi.PropertyType), canRead, canWrite));
                }

                if (includeMethods)
                {
                    foreach (var mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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

        [McpServerTool, Description("Read a singleton member value (safe, bounded).")]
        public static async Task<object> ReadSingletonMember(string singletonId, string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(singletonId) || !singletonId.StartsWith("singleton:"))
                throw new ArgumentException("Invalid singletonId; expected 'singleton:<declaringTypeFullName>'", nameof(singletonId));
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Invalid member name", nameof(name));

            var declaringTypeName = singletonId.Substring("singleton:".Length);
            if (string.IsNullOrWhiteSpace(declaringTypeName))
                throw new ArgumentException("Invalid singletonId; declaring type missing", nameof(singletonId));

            return await MainThread.Run(() =>
            {
                var instance = ResolveSingletonInstance(declaringTypeName);
                if (instance == null)
                    throw new InvalidOperationException("NotFound");

                var t = instance.GetType();
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(instance);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.text, valueJson = summary.json };
                }

                var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        throw new InvalidOperationException("NotFound");
                    var val = getter.Invoke(instance, Array.Empty<object>());
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.text, valueJson = summary.json };
                }

                throw new InvalidOperationException("NotFound");
            });
        }

        private static object? ResolveSingletonInstance(string declaringTypeName)
        {
            var declaringType = ReflectionUtility.GetTypeByName(declaringTypeName);
            if (declaringType == null)
                return null;

            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var tmpList = new List<object>();
            ReflectionUtility.FindSingleton(SearchProvider.instanceNames, declaringType, flags, tmpList);
            return tmpList.Count > 0 ? tmpList[0] : null;
        }
    }
}
#endif
