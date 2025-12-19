#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityExplorer.ObjectExplorer;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public object ReadStaticMember(string typeFullName, string name)
        {
            if (string.IsNullOrEmpty(typeFullName) || typeFullName.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "typeFullName is required");
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "name is required");

            return MainThread.Run(() =>
            {
                var type = ReflectionUtility.GetTypeByName(typeFullName);
                if (type == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                var flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
                var fi = type.GetField(name, flags);
                if (fi != null)
                {
                    var val = fi.GetValue(null);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.Text, valueJson = summary.Json };
                }

                var pi = type.GetProperty(name, flags);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null || !getter.IsStatic)
                        throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not readable");
                    var val = getter.Invoke(null, new object[0]);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.Text, valueJson = summary.Json };
                }

                throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not found");
            });
        }

        public Page<InspectorMemberDto> ListSingletonMembers(string singletonId, bool includeMethods = false, int? limit = null, int? offset = null)
        {
            if (string.IsNullOrEmpty(singletonId) || singletonId.Trim().Length == 0 || !singletonId.StartsWith("singleton:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid singletonId; expected 'singleton:<declaringTypeFullName>'");
            var declaringTypeName = singletonId.Substring("singleton:".Length);
            if (string.IsNullOrEmpty(declaringTypeName) || declaringTypeName.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid singletonId; declaring type missing");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var instance = ResolveSingletonInstance(declaringTypeName);
                if (instance == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                var t = instance.GetType();
                var members = new List<InspectorMemberDto>();

                foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (fi.IsSpecialName) continue;
                    var canWrite = !(fi.IsInitOnly || fi.IsLiteral);
                    members.Add(new InspectorMemberDto { Name = fi.Name, Kind = "field", Type = SafeTypeName(fi.FieldType), CanRead = true, CanWrite = canWrite });
                }

                foreach (var pi in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (pi.IsSpecialName) continue;
                    if (pi.GetIndexParameters().Length > 0) continue;
                    var canRead = pi.GetGetMethod(true) != null;
                    var canWrite = pi.GetSetMethod(true) != null;
                    members.Add(new InspectorMemberDto { Name = pi.Name, Kind = "property", Type = SafeTypeName(pi.PropertyType), CanRead = canRead, CanWrite = canWrite });
                }

                if (includeMethods)
                {
                    foreach (var mi in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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

        public object ReadSingletonMember(string singletonId, string name)
        {
            if (string.IsNullOrEmpty(singletonId) || singletonId.Trim().Length == 0 || !singletonId.StartsWith("singleton:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid singletonId; expected 'singleton:<declaringTypeFullName>'");
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid member name");
            var declaringTypeName = singletonId.Substring("singleton:".Length);
            if (string.IsNullOrEmpty(declaringTypeName) || declaringTypeName.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid singletonId; declaring type missing");

            return MainThread.Run(() =>
            {
                var instance = ResolveSingletonInstance(declaringTypeName);
                if (instance == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                var t = instance.GetType();
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(instance);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.Text, valueJson = summary.Json };
                }

                var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not readable");
                    var val = getter.Invoke(instance, new object[0]);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.Text, valueJson = summary.Json };
                }

                throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not found");
            });
        }

        private object? ResolveSingletonInstance(string declaringTypeName)
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
