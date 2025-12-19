#if MONO && !INTEROP
#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UnityExplorer.Mcp
{
    internal sealed partial class MonoMcpHandlers
    {
        private static object Vector3Schema()
        {
            return Schema(new Dictionary<string, object>
            {
                { "X", Number() },
                { "Y", Number() },
                { "Z", Number() }
            }, new[] { "X", "Y", "Z" });
        }

        private void AddTools_Freecam(List<object> list)
        {
            list.Add(new { name = "GetFreecam", description = "Get freecam state (enabled/pose/speed).", inputSchema = Schema(new Dictionary<string, object>()) });
            list.Add(new { name = "SetFreecamEnabled", description = "Enable or disable freecam (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "enabled", Bool() }, { "confirm", Bool(false) } }, new[] { "enabled" }) });
            list.Add(new { name = "SetFreecamSpeed", description = "Set freecam move speed (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "speed", Number() }, { "confirm", Bool(false) } }, new[] { "speed" }) });
            list.Add(new { name = "SetFreecamPose", description = "Set freecam position and euler rotation (guarded).", inputSchema = Schema(new Dictionary<string, object> { { "pos", Vector3Schema() }, { "rot", Vector3Schema() }, { "confirm", Bool(false) } }, new[] { "pos", "rot" }) });
        }

        private bool TryCallTool_Freecam(string key, JObject? args, out object result)
        {
            result = null!;
            switch (key)
            {
                case "getfreecam":
                    result = _tools.GetFreecam();
                    return true;
                case "setfreecamenabled":
                {
                    var enabled = GetBool(args, "enabled");
                    if (enabled == null)
                        throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'enabled' is required.");
                    result = _write.SetFreecamEnabled(enabled.Value, GetBool(args, "confirm") ?? false);
                    return true;
                }
                case "setfreecamspeed":
                {
                    var speed = GetFloat(args, "speed");
                    if (speed == null)
                        throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'speed' is required.");
                    result = _write.SetFreecamSpeed(speed.Value, GetBool(args, "confirm") ?? false);
                    return true;
                }
                case "setfreecampose":
                {
                    var posObj = args?["pos"] as JObject;
                    var rotObj = args?["rot"] as JObject;
                    if (posObj == null || rotObj == null)
                        throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: 'pos' and 'rot' are required.");

                    float? px = posObj["X"]?.ToObject<float?>();
                    float? py = posObj["Y"]?.ToObject<float?>();
                    float? pz = posObj["Z"]?.ToObject<float?>();
                    float? rx = rotObj["X"]?.ToObject<float?>();
                    float? ry = rotObj["Y"]?.ToObject<float?>();
                    float? rz = rotObj["Z"]?.ToObject<float?>();

                    if (px == null || py == null || pz == null || rx == null || ry == null || rz == null)
                        throw new McpError(-32602, 400, "InvalidArgument", "Invalid params: pos/rot require X/Y/Z.");

                    var pos = new Vector3Dto { X = px.Value, Y = py.Value, Z = pz.Value };
                    var rot = new Vector3Dto { X = rx.Value, Y = ry.Value, Z = rz.Value };
                    result = _write.SetFreecamPose(pos, rot, GetBool(args, "confirm") ?? false);
                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
#endif
