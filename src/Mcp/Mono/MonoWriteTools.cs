#if MONO && !INTEROP
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityExplorer.CSConsole;
using UnityExplorer.Hooks;
using UnityExplorer.ObjectExplorer;
using UnityExplorer.UI.Widgets;
using UniverseLib.Utility;

#nullable enable

namespace UnityExplorer.Mcp
{
    internal sealed class MonoWriteTools
    {
        private readonly MonoReadTools _read;
        private static GameObject? _testUiRoot;
        private static GameObject? _testUiLeft;
        private static GameObject? _testUiRight;

        public MonoWriteTools(MonoReadTools read)
        {
            _read = read;
        }

        private static string ObjectId(GameObject go) => $"obj:{go.GetInstanceID()}";

        private static bool IsTestUiObject(GameObject go)
        {
            if (_testUiRoot == null) return false;
            var t = go.transform;
            while (t != null)
            {
                if (t.gameObject == _testUiRoot) return true;
                t = t.parent;
            }
            return false;
        }

        private static object ToolError(string kind, string message, string? hint = null)
            => new { ok = false, error = new { kind, message, hint } };

        private static object ToolErrorFromException(Exception ex)
        {
            if (ex is InvalidOperationException inv)
            {
                switch (inv.Message)
                {
                    case "NotFound": return ToolError("NotFound", "Not found");
                    case "PermissionDenied": return ToolError("PermissionDenied", "Permission denied");
                    case "ConfirmationRequired": return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
                    case "Denied by allowlist": return ToolError("PermissionDenied", "Denied by allowlist");
                    case "Component not found": return ToolError("NotFound", "Component not found");
                    case "Method overload not found": return ToolError("NotFound", "Method overload not found");
                    case "Method not found": return ToolError("NotFound", "Method not found");
                    case "Type not found": return ToolError("NotFound", "Type not found");
                    case "Hook not found": return ToolError("NotFound", "Hook not found");
                    default: return ToolError("InvalidArgument", inv.Message);
                }
            }

            if (ex is ArgumentException arg)
                return ToolError("InvalidArgument", arg.Message);

            return ToolError("Internal", ex.Message);
        }

        private static bool IsAllowed(string typeFullName, string member)
        {
            var cfg = McpConfig.Load();
            if (cfg.ReflectionAllowlistMembers == null || cfg.ReflectionAllowlistMembers.Length == 0) return false;
            var key = typeFullName + "." + member;
            foreach (var entry in cfg.ReflectionAllowlistMembers)
            {
                if (string.Equals(entry, key, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool TryGetComponent(GameObject go, string typeFullName, out UnityEngine.Component comp)
        {
            comp = null;
            var comps = go.GetComponents<UnityEngine.Component>();
            foreach (var c in comps)
            {
                if (c != null && string.Equals(c.GetType().FullName, typeFullName, StringComparison.Ordinal))
                {
                    comp = c;
                    return true;
                }
            }
            return false;
        }

        private static bool IsHookAllowed(string typeFullName)
        {
            var allow = McpConfig.Load().HookAllowlistSignatures;
            if (allow == null || allow.Length == 0) return false;
            foreach (var entry in allow)
            {
                if (string.Equals(entry, typeFullName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static bool LooksLikeHarmonyFullDescription(string methodOrSignature)
        {
            if (string.IsNullOrEmpty(methodOrSignature) || methodOrSignature.Trim().Length == 0) return false;
            return methodOrSignature.Contains("::") && methodOrSignature.Contains("(") && methodOrSignature.Contains(")");
        }

        private static object DeserializeTo(string json, Type type)
        {
            try { return JsonConvert.DeserializeObject(json, type); } catch { }
            try { return Convert.ChangeType(json, type); } catch { }
            return null;
        }

        public object SetConfig(
            bool? allowWrites = null,
            bool? requireConfirm = null,
            bool? enableConsoleEval = null,
            string[]? componentAllowlist = null,
            string[]? reflectionAllowlistMembers = null,
            string[]? hookAllowlistSignatures = null,
            bool restart = false)
        {
            try
            {
                var cfg = McpConfig.Load();
                if (allowWrites.HasValue) cfg.AllowWrites = allowWrites.Value;
                if (requireConfirm.HasValue) cfg.RequireConfirm = requireConfirm.Value;
                if (enableConsoleEval.HasValue) cfg.EnableConsoleEval = enableConsoleEval.Value;
                if (componentAllowlist != null) cfg.ComponentAllowlist = componentAllowlist;
                if (reflectionAllowlistMembers != null) cfg.ReflectionAllowlistMembers = reflectionAllowlistMembers;
                if (hookAllowlistSignatures != null) cfg.HookAllowlistSignatures = hookAllowlistSignatures;
                McpConfig.Save(cfg);
                if (restart)
                {
                    McpHost.Stop();
                    McpHost.StartIfEnabled();
                }
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolError("Internal", ex.Message);
            }
        }

        public object GetConfig()
        {
            try
            {
                var cfg = McpConfig.Load();
                return new
                {
                    ok = true,
                    enabled = cfg.Enabled,
                    bindAddress = cfg.BindAddress,
                    port = cfg.Port,
                    allowWrites = cfg.AllowWrites,
                    requireConfirm = cfg.RequireConfirm,
                    exportRoot = cfg.ExportRoot,
                    logLevel = cfg.LogLevel,
                    componentAllowlist = cfg.ComponentAllowlist,
                    reflectionAllowlistMembers = cfg.ReflectionAllowlistMembers,
                    enableConsoleEval = cfg.EnableConsoleEval,
                    hookAllowlistSignatures = cfg.HookAllowlistSignatures
                };
            }
            catch (Exception ex)
            {
                return ToolError("Internal", ex.Message);
            }
        }

        public object SetActive(string objectId, bool active, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    go.SetActive(active);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SetMember(string objectId, string componentType, string member, string jsonValue, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsAllowed(componentType, member)) return ToolError("PermissionDenied", "Denied by allowlist");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component comp;
                    if (!TryGetComponent(go, componentType, out comp) || comp == null)
                        throw new InvalidOperationException("Component not found");

                    var t = comp.GetType();
                    var fi = t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fi != null)
                    {
                        var val = DeserializeTo(jsonValue, fi.FieldType);
                        fi.SetValue(comp, val);
                        return;
                    }

                    var pi = t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pi == null || !pi.CanWrite)
                        throw new InvalidOperationException("Member not writable");
                    var valProp = DeserializeTo(jsonValue, pi.PropertyType);
                    pi.SetValue(comp, valProp, null);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object ConsoleEval(string code, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (string.IsNullOrEmpty(code) || code.Trim().Length == 0)
                return new { ok = true, result = string.Empty };

            try
            {
                string? result = null;
                MainThread.Run(() =>
                {
                    try
                    {
                        var evaluator = new ConsoleScriptEvaluator();
                        evaluator.Initialize();
                        var compiled = evaluator.Compile(code);
                        if (compiled != null)
                        {
                            object? ret = null;
                            compiled.Invoke(ref ret);
                            result = ret?.ToString();
                        }
                    }
                    catch (Exception ex)
                    {
                        result = "Error: " + ex.Message;
                    }
                });

                return new { ok = true, result };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object AddComponent(string objectId, string type, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (cfg.ComponentAllowlist == null || cfg.ComponentAllowlist.Length == 0)
                return ToolError("PermissionDenied", "No components are allowlisted");
            if (Array.IndexOf(cfg.ComponentAllowlist, type) < 0)
                return ToolError("PermissionDenied", "Denied by allowlist");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                var t = UniverseLib.ReflectionUtility.GetTypeByName(type);
                if (t == null || !typeof(UnityEngine.Component).IsAssignableFrom(t))
                    return ToolError("InvalidArgument", "Type not found or not a Component");

                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    go.AddComponent(t);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object RemoveComponent(string objectId, string typeOrIndex, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    UnityEngine.Component target = null;
                    if (int.TryParse(typeOrIndex, out var idx))
                    {
                        var comps = go.GetComponents<UnityEngine.Component>();
                        if (idx >= 0 && idx < comps.Length) target = comps[idx];
                    }
                    else
                    {
                        var t = UniverseLib.ReflectionUtility.GetTypeByName(typeOrIndex);
                        if (t != null)
                        {
                            if (cfg.ComponentAllowlist == null || Array.IndexOf(cfg.ComponentAllowlist, t.FullName) < 0)
                                throw new InvalidOperationException("Denied by allowlist");
                            var comps = go.GetComponents<UnityEngine.Component>();
                            foreach (var c in comps)
                            {
                                if (c != null && c.GetType().FullName == t.FullName)
                                {
                                    target = c;
                                    break;
                                }
                            }
                        }
                    }

                    if (target == null) throw new InvalidOperationException("Component not found");
                    UnityEngine.Object.Destroy(target);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object HookAdd(string type, string method, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!IsHookAllowed(type)) return ToolError("PermissionDenied", "Denied by hook allowlist");

            try
            {
                object? early = null;
                MainThread.Run(() =>
                {
                    var t = UniverseLib.ReflectionUtility.GetTypeByName(type);
                    if (t == null) throw new InvalidOperationException("Type not found");

                    MethodInfo? mi;
                    if (LooksLikeHarmonyFullDescription(method))
                    {
                        mi = t.GetMethods(UniverseLib.ReflectionUtility.FLAGS)
                            .FirstOrDefault(m => string.Equals(m.FullDescription(), method, StringComparison.Ordinal));
                        if (mi == null) throw new InvalidOperationException("Method overload not found");
                    }
                    else
                    {
                        var candidates = t.GetMethods(UniverseLib.ReflectionUtility.FLAGS)
                            .Where(m => string.Equals(m.Name, method, StringComparison.Ordinal))
                            .ToList();
                        if (candidates.Count == 0) throw new InvalidOperationException("Method not found");
                        if (candidates.Count > 1)
                        {
                            var example = candidates[0].FullDescription();
                            early = ToolError("InvalidArgument", $"Multiple overloads found for '{type}.{method}'.", $"pass the full signature in method (example: {example})");
                            return;
                        }
                        mi = candidates[0];
                    }

                    var sig = mi.FullDescription();
                    if (HookList.hookedSignatures.Contains(sig))
                        throw new InvalidOperationException("Method is already hooked");

                    var hook = new HookInstance(mi);
                    HookList.hookedSignatures.Add(sig);
                    HookList.currentHooks.Add(sig, hook);
                });
                return early ?? new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object HookSetEnabled(string signature, bool enabled, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    var declaringType = hook.TargetMethod?.DeclaringType?.FullName;
                    if (string.IsNullOrEmpty(declaringType) || !IsHookAllowed(declaringType))
                        throw new InvalidOperationException("PermissionDenied");

                    if (enabled) hook.Patch();
                    else hook.Unpatch();
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        public object HookSetSource(string signature, string source, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!cfg.EnableConsoleEval) return ToolError("PermissionDenied", "ConsoleEval disabled by config");

            try
            {
                MainThread.Run(() =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    var declaringType = hook.TargetMethod?.DeclaringType?.FullName;
                    if (string.IsNullOrEmpty(declaringType) || !IsHookAllowed(declaringType))
                        throw new InvalidOperationException("PermissionDenied");

                    var wasEnabled = hook.Enabled;
                    hook.PatchSourceCode = source ?? string.Empty;
                    var ok = hook.CompileAndGenerateProcessor(hook.PatchSourceCode);
                    if (!ok)
                        throw new InvalidOperationException("Compile failed");

                    if (wasEnabled) hook.Patch();
                    else hook.Unpatch();
                });
                return new { ok = true };
            }
            catch (Exception ex) { return ToolErrorFromException(ex); }
        }

        public object HookRemove(string signature, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (!HookList.currentHooks.Contains(signature))
                        throw new InvalidOperationException("Hook not found");

                    var hook = (HookInstance)HookList.currentHooks[signature]!;
                    hook.Unpatch();
                    HookList.currentHooks.Remove(signature);
                    HookList.hookedSignatures.Remove(signature);
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object Reparent(string objectId, string newParentId, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var childId))
                return ToolError("InvalidArgument", "Invalid child id");
            if (!int.TryParse(newParentId.StartsWith("obj:") ? newParentId.Substring(4) : string.Empty, out var parentId))
                return ToolError("InvalidArgument", "Invalid parent id");

            try
            {
                object? response = null;
                MainThread.Run(() =>
                {
                    var child = _read.FindByInstanceId(childId);
                    var parent = _read.FindByInstanceId(parentId);
                    if (child == null || parent == null) throw new InvalidOperationException("NotFound");
                    if (child == parent) throw new InvalidOperationException("InvalidArgument");
                    if (!IsTestUiObject(child) || !IsTestUiObject(parent))
                    {
                        response = ToolError("PermissionDenied", "Only test UI objects may be reparented (SpawnTestUi)");
                        return;
                    }

                    child.transform.SetParent(parent.transform, true);
                });
                if (response != null) return response;
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object DestroyObject(string objectId, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");
            if (!int.TryParse(objectId.StartsWith("obj:") ? objectId.Substring(4) : string.Empty, out var iid))
                return ToolError("InvalidArgument", "Invalid id");

            try
            {
                object? response = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    if (!IsTestUiObject(go))
                    {
                        response = ToolError("PermissionDenied", "Only test UI objects may be destroyed (SpawnTestUi)");
                        return;
                    }

                    UnityEngine.Object.Destroy(go);
                    if (_testUiRoot == go)
                    {
                        _testUiRoot = null;
                        _testUiLeft = null;
                        _testUiRight = null;
                    }
                    else
                    {
                        if (_testUiLeft == go) _testUiLeft = null;
                        if (_testUiRight == go) _testUiRight = null;
                    }
                });
                if (response != null) return response;
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SelectObject(string objectId)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");

            if (string.IsNullOrEmpty(objectId) || objectId.Trim().Length == 0 || !objectId.StartsWith("obj:"))
                return ToolError("InvalidArgument", "Invalid objectId; expected 'obj:<instanceId>'");
            if (!int.TryParse(objectId.Substring(4), out var iid))
                return ToolError("InvalidArgument", "Invalid instance id");

            try
            {
                SelectionDto? selection = null;
                MainThread.Run(() =>
                {
                    var go = _read.FindByInstanceId(iid);
                    if (go == null) throw new InvalidOperationException("NotFound");
                    try { InspectorManager.Inspect(go); } catch { }
                    _read.RecordSelection(objectId);
                });

                selection = _read.GetSelection();
                try
                {
                    var http = McpSimpleHttp.Current;
                    if (http != null) http.BroadcastNotificationAsync("selection", selection);
                }
                catch { }

                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object GetTimeScale()
        {
            bool locked = false;
            float value = Time.timeScale;
            MainThread.Run(() =>
            {
                TryGetTimeScaleState(TimeScaleWidget.Instance, out locked, out value);
            });
            return new { ok = true, value, locked };
        }

        public object SetTimeScale(float value, bool? @lock = null, bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            var clamped = Mathf.Clamp(value, 0f, 4f);
            try
            {
                bool locked = false;
                float applied = clamped;
                MainThread.Run(() =>
                {
                    var widget = TimeScaleWidget.Instance;
                    if (@lock == true && widget != null)
                    {
                        widget.LockTo(clamped);
                    }
                    else
                    {
                        Time.timeScale = clamped;
                        if (@lock == false && widget != null)
                        {
                            UnlockTimeScale(widget);
                        }
                    }

                    TryGetTimeScaleState(widget, out locked, out applied);
                });
                return new { ok = true, value = applied, locked };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object SpawnTestUi(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (_testUiRoot != null)
                    {
                        if (_testUiLeft == null)
                        {
                            var foundLeft = _testUiRoot.transform.Find("McpTestBlock_Left");
                            _testUiLeft = foundLeft != null ? foundLeft.gameObject : AddTestBlock(_testUiRoot, "McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                        }
                        if (_testUiRight == null)
                        {
                            var foundRight = _testUiRoot.transform.Find("McpTestBlock_Right");
                            _testUiRight = foundRight != null ? foundRight.gameObject : AddTestBlock(_testUiRoot, "McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                        }
                        return;
                    }

                    if (EventSystem.current == null)
                    {
                        var es = new GameObject("McpTest_EventSystem");
                        es.AddComponent<EventSystem>();
                        es.AddComponent<StandaloneInputModule>();
                        es.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }

                    var root = new GameObject("McpTestCanvas");
                    var canvas = root.AddComponent<Canvas>();
                    root.AddComponent<CanvasScaler>();
                    root.AddComponent<GraphicRaycaster>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                    var scaler = root.GetComponent<CanvasScaler>();
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1920, 1080);
                    scaler.matchWidthOrHeight = 0.5f;

                    _testUiRoot = root;
                    _testUiLeft = AddTestBlock(root, "McpTestBlock_Left", new Color(0.8f, 0.3f, 0.3f, 0.8f), new Vector2(0.35f, 0.5f), new Vector2(180, 180));
                    _testUiRight = AddTestBlock(root, "McpTestBlock_Right", new Color(0.3f, 0.8f, 0.4f, 0.8f), new Vector2(0.65f, 0.5f), new Vector2(180, 180));
                });

                var blocks = new List<object>();
                if (_testUiLeft != null) blocks.Add(new { name = _testUiLeft.name, id = ObjectId(_testUiLeft) });
                if (_testUiRight != null) blocks.Add(new { name = _testUiRight.name, id = ObjectId(_testUiRight) });

                return new
                {
                    ok = true,
                    rootId = _testUiRoot != null ? ObjectId(_testUiRoot) : null,
                    blocks = blocks.ToArray()
                };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        public object DestroyTestUi(bool confirm = false)
        {
            var cfg = McpConfig.Load();
            if (!cfg.AllowWrites) return ToolError("PermissionDenied", "Writes disabled");
            if (cfg.RequireConfirm && !confirm) return ToolError("PermissionDenied", "Confirmation required", "resend with confirm=true");

            try
            {
                MainThread.Run(() =>
                {
                    if (_testUiRoot != null)
                    {
                        try { GameObject.Destroy(_testUiRoot); } catch { }
                    }
                    _testUiRoot = null;
                    _testUiLeft = null;
                    _testUiRight = null;
                });
                return new { ok = true };
            }
            catch (Exception ex)
            {
                return ToolErrorFromException(ex);
            }
        }

        private static GameObject AddTestBlock(GameObject root, string name, Color color, Vector2 anchor, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var rt = go.AddComponent<RectTransform>();
            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            img.color = color;
            img.raycastTarget = true;
            return go;
        }

        private static void UnlockTimeScale(TimeScaleWidget widget)
        {
            try
            {
                var lockedField = typeof(TimeScaleWidget).GetField("locked", BindingFlags.NonPublic | BindingFlags.Instance);
                lockedField?.SetValue(widget, false);
                var updateUi = typeof(TimeScaleWidget).GetMethod("UpdateUi", BindingFlags.NonPublic | BindingFlags.Instance);
                updateUi?.Invoke(widget, null);
            }
            catch { }
        }

        private static void TryGetTimeScaleState(TimeScaleWidget? widget, out bool locked, out float value)
        {
            locked = false;
            value = Time.timeScale;
            if (widget == null) return;
            try
            {
                var lockedField = typeof(TimeScaleWidget).GetField("locked", BindingFlags.NonPublic | BindingFlags.Instance);
                var lockedVal = lockedField?.GetValue(widget);
                if (lockedVal is bool b) locked = b;
            }
            catch { }
        }
    }
}
#endif
