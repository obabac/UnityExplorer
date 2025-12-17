#if MONO && !INTEROP
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoReadTools
    {
        public Page<ComponentCardDto> GetComponents(string objectId, int? limit, int? offset)
        {
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");

            if (!int.TryParse(objectId.Substring(4), out var iid))
                throw new MonoMcpHandlers.McpError(-32602, 400, "InvalidArgument", "Invalid instance id");

            int lim = Math.Max(1, limit ?? 100);
            int off = Math.Max(0, offset ?? 0);

            return MainThread.Run(() =>
            {
                var go = FindByInstanceId(iid);
                if (go == null) throw new MonoMcpHandlers.McpError(-32004, 404, "NotFound", "NotFound");
                Component[] comps;
                try { comps = go.GetComponents<Component>(); }
                catch { comps = new Component[0]; }
                var total = comps.Length;
                var list = new List<ComponentCardDto>();
                for (int i = off; i < Math.Min(total, off + lim); i++)
                {
                    var c = comps[i];
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
                    list.Add(new ComponentCardDto { Type = typeName, Summary = summary });
                }
                return new Page<ComponentCardDto>(total, list);
            });
        }
    }
}
#endif
