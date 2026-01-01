#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UniverseLib.Utility;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public object InspectRef(string refId)
        {
            if (string.IsNullOrEmpty(refId) || refId.Trim().Length == 0 || !refId.StartsWith("ref:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid refId; expected 'ref:...'");

            return MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                var t = instance.GetActualType() ?? instance.GetType();
                var typeName = SafeTypeName(t);
                var kind = instance is IDictionary
                    ? "dictionary"
                    : instance is IEnumerable && instance is not string ? "enumerable" : "object";

                int? count = null;
                try
                {
                    if (instance is ICollection coll) count = coll.Count;
                }
                catch { }

                string valueText;
                try { valueText = Truncate(instance.ToString() ?? "<null>"); }
                catch { valueText = typeName; }

                return new { ok = true, refId, type = typeName, kind, count, valueText };
            });
        }

        public Page<InspectorMemberDto> ListRefMembers(string refId, bool includeMethods = false, int? limit = null, int? offset = null)
        {
            if (string.IsNullOrEmpty(refId) || refId.Trim().Length == 0 || !refId.StartsWith("ref:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid refId; expected 'ref:...'");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                var t = instance.GetActualType() ?? instance.GetType();
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

        public object ReadRefMember(string refId, string name)
        {
            if (string.IsNullOrEmpty(refId) || refId.Trim().Length == 0 || !refId.StartsWith("ref:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid refId; expected 'ref:...'");
            if (string.IsNullOrEmpty(name) || name.Trim().Length == 0)
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid member name");

            return MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                var t = instance.GetActualType() ?? instance.GetType();
                var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi != null)
                {
                    var val = fi.GetValue(instance);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(fi.FieldType), valueText = summary.Text, valueJson = summary.Json, refId = summary.RefId };
                }

                var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    var getter = pi.GetGetMethod(true);
                    if (getter == null)
                        throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not readable");
                    var val = getter.Invoke(instance, new object[0]);
                    var summary = SummarizeValue(val);
                    return new { ok = true, type = SafeTypeName(pi.PropertyType), valueText = summary.Text, valueJson = summary.Json, refId = summary.RefId };
                }

                throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "Member not found");
            });
        }

        public Page<object> ListRefItems(string refId, int? limit = null, int? offset = null)
        {
            if (string.IsNullOrEmpty(refId) || refId.Trim().Length == 0 || !refId.StartsWith("ref:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid refId; expected 'ref:...'");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                if (!McpObjectRefs.TryGet(refId, out var instance) || instance == null)
                    throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");

                if (instance is IDictionary dict)
                {
                    var total = dict.Count;
                    var items = new List<object>(Math.Min(lim, total));
                    var idx = 0;
                    foreach (DictionaryEntry entry in dict)
                    {
                        if (idx++ < off) continue;
                        if (items.Count >= lim) break;
                        var k = SummarizeValue(entry.Key, 1);
                        var v = SummarizeValue(entry.Value, 1);
                        items.Add(new
                        {
                            key = new { text = k.Text, json = k.Json, refId = k.RefId },
                            value = new { text = v.Text, json = v.Json, refId = v.RefId }
                        });
                    }
                    return new Page<object>(total, items);
                }

                if (instance is IEnumerable enumerable && instance is not string)
                {
                    var coll = instance as ICollection;
                    if (coll == null)
                        throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Enumerable does not provide Count; use preview from Read* tools.");

                    var total = coll.Count;
                    var items = new List<object>(Math.Min(lim, total));
                    var idx = 0;
                    foreach (var item in enumerable)
                    {
                        if (idx++ < off) continue;
                        if (items.Count >= lim) break;
                        var s = SummarizeValue(item, 1);
                        items.Add(new { text = s.Text, json = s.Json, refId = s.RefId });
                    }
                    return new Page<object>(total, items);
                }

                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "NotSupported: ref is not enumerable/dictionary.");
            });
        }

        public object ReleaseRef(string refId)
        {
            if (string.IsNullOrEmpty(refId) || refId.Trim().Length == 0 || !refId.StartsWith("ref:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid refId; expected 'ref:...'");

            var released = McpObjectRefs.Release(refId);
            return new { ok = true, released };
        }
    }
}
#endif

