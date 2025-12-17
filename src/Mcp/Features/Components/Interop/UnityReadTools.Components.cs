#if INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UnityExplorer.Mcp
{
    public static partial class UnityReadTools
    {
        [McpServerTool, Description("Get component cards for object id (paged).")]
        public static async Task<Page<ComponentCardDto>> GetComponents(string objectId, int? limit, int? offset, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(objectId) || !objectId.StartsWith("obj:"))
                throw new ArgumentException("Invalid objectId; expected 'obj:<instanceId>'", nameof(objectId));

            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new ArgumentException("Invalid instance id", nameof(objectId));

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return await MainThread.Run(() =>
            {
                var go = UnityQuery.FindByInstanceId(iid);
                if (go == null) throw new InvalidOperationException("NotFound");
                UnityEngine.Component[] comps;
                try { comps = go.GetComponents<UnityEngine.Component>(); }
                catch { comps = Array.Empty<UnityEngine.Component>(); }
                var total = comps.Length;
                var slice = comps.Skip(off).Take(lim);
                var list = new List<ComponentCardDto>();
                foreach (var c in slice)
                {
                    string typeName;
                    string summary;
                    if (c == null)
                    {
                        typeName = "<null>";
                        summary = "<null>";
                    }
                    else
                    {
                        var t = c.GetType();
                        typeName = t != null ? (t.FullName ?? "<null>") : "<null>";
                        var s = c.ToString();
                        summary = string.IsNullOrEmpty(s) ? "<null>" : s;
                    }
                    list.Add(new ComponentCardDto(typeName, summary));
                }
                var items = list.ToArray();
                return new Page<ComponentCardDto>(total, items);
            });
        }
    }
}
#endif
