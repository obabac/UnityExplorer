using UnityExplorer.CacheObject;
using UnityExplorer.CacheObject.Views;
using UnityExplorer.Config;
using UniverseLib.UI;
using UnityExplorer.Mcp;
using UniverseLib.UI.Widgets.ScrollView;

namespace UnityExplorer.UI.Panels
{
    public class OptionsPanel : UEPanel, ICacheObjectController, ICellPoolDataSource<ConfigEntryCell>
    {
        public override string Name => "Options";
        public override UIManager.Panels PanelType => UIManager.Panels.Options;

        public override int MinWidth => 600;
        public override int MinHeight => 200;
        public override Vector2 DefaultAnchorMin => new(0.5f, 0.1f);
        public override Vector2 DefaultAnchorMax => new(0.5f, 0.85f);

        public override bool ShouldSaveActiveState => false;
        public override bool ShowByDefault => false;

        // Entry holders
        private readonly List<CacheConfigEntry> configEntries = new();

        // ICacheObjectController
        public CacheObjectBase ParentCacheObject => null;
        public object Target => null;
        public Type TargetType => null;
        public bool CanWrite => true;

        // ICellPoolDataSource
        public int ItemCount => configEntries.Count;

        public OptionsPanel(UIBase owner) : base(owner)
        {
            foreach (KeyValuePair<string, IConfigElement> entry in ConfigManager.ConfigElements)
            {
                CacheConfigEntry cache = new(entry.Value)
                {
                    Owner = this
                };
                configEntries.Add(cache);
            }

            foreach (CacheConfigEntry config in configEntries)
                config.UpdateValueFromSource();
        }

        public void OnCellBorrowed(ConfigEntryCell cell)
        {
        }

        public void SetCell(ConfigEntryCell cell, int index)
        {
            CacheObjectControllerHelper.SetCell(cell, index, this.configEntries, null);
        }

        // UI Construction

        public override void SetDefaultSizeAndPosition()
        {
            base.SetDefaultSizeAndPosition();

            Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 600f);
        }

        protected override void ConstructPanelContent()
        {
            // Save button

            UniverseLib.UI.Models.ButtonRef saveBtn = UIFactory.CreateButton(this.ContentRoot, "Save", "Save Options", new Color(0.2f, 0.3f, 0.2f));
            UIFactory.SetLayoutElement(saveBtn.Component.gameObject, flexibleWidth: 9999, minHeight: 30, flexibleHeight: 0);
            saveBtn.OnClick += ConfigManager.Handler.SaveConfig;

            // MCP section
#if INTEROP
            UIFactory.CreateLabel(this.ContentRoot, "McpHeader", "MCP Server", TextAnchor.MiddleLeft, Color.white, true);

            var cfg = McpConfig.Load();
            var info = McpDiscovery.TryLoad();

            // Row: Enabled toggle + Restart + Copy buttons
            var row = UIFactory.CreateUIObject("McpRow", this.ContentRoot);
            UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(row, false, false, true, true, 5, 2, 2, 2, 2);

            // Enabled toggle
            var enabledObj = UIFactory.CreateToggle(row, "McpEnabledToggle", out Toggle enabledToggle, out Text enabledText);
            enabledText.text = "Enabled";
            enabledToggle.isOn = cfg.Enabled;
            UIFactory.SetLayoutElement(enabledObj, minHeight: 25, minWidth: 110);

            // Restart button
            var restartBtn = UIFactory.CreateButton(row, "RestartMcp", "Restart MCP", new Color(0.2f, 0.2f, 0.3f));
            UIFactory.SetLayoutElement(restartBtn.Component.gameObject, minHeight: 25, minWidth: 120);
            restartBtn.OnClick += () =>
            {
                try
                {
                    Mcp.McpHost.Stop();
                    Mcp.McpHost.StartIfEnabled();
                    Notification.ShowMessage("MCP restarted.");
                }
                catch { }
            };

            // Copy endpoint/token buttons (to UnityExplorer clipboard)
            var copyEndpointBtn = UIFactory.CreateButton(row, "CopyMcpEndpoint", "Copy Endpoint", new Color(0.2f, 0.3f, 0.2f));
            UIFactory.SetLayoutElement(copyEndpointBtn.Component.gameObject, minHeight: 25, minWidth: 120);
            copyEndpointBtn.OnClick += () =>
            {
                var latest = McpDiscovery.TryLoad();
                var endpointValue = latest?.BaseUrl ?? "(not running)";
                ClipboardPanel.Copy(endpointValue);
            };

            var copyTokenBtn = UIFactory.CreateButton(row, "CopyMcpToken", "Copy Token", new Color(0.3f, 0.2f, 0.2f));
            UIFactory.SetLayoutElement(copyTokenBtn.Component.gameObject, minHeight: 25, minWidth: 110);
            copyTokenBtn.OnClick += () =>
            {
                ClipboardPanel.Copy(string.Empty);
            };

            // Second row for write-safety toggles
            var safetyRow = UIFactory.CreateUIObject("McpSafetyRow", this.ContentRoot);
            UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(safetyRow, false, false, true, true, 5, 2, 2, 2, 2);

            // AllowWrites toggle
            var allowObj = UIFactory.CreateToggle(safetyRow, "AllowWritesToggle", out Toggle allowToggle, out Text allowText);
            allowText.text = "Allow Writes";
            allowToggle.isOn = cfg.AllowWrites;
            allowToggle.onValueChanged.AddListener((bool v) =>
            {
                var c = McpConfig.Load();
                c.AllowWrites = v;
                McpConfig.Save(c);
            });
            UIFactory.SetLayoutElement(allowObj, minHeight: 25, minWidth: 140);

            // RequireConfirm toggle
            var confirmObj = UIFactory.CreateToggle(safetyRow, "RequireConfirmToggle", out Toggle confirmToggle, out Text confirmText);
            confirmText.text = "Require Confirm";
            confirmToggle.isOn = cfg.RequireConfirm;
            confirmToggle.onValueChanged.AddListener((bool v) =>
            {
                var c = McpConfig.Load();
                c.RequireConfirm = v;
                McpConfig.Save(c);
            });
            UIFactory.SetLayoutElement(confirmObj, minHeight: 25, minWidth: 160);

            // Component Allowlist editor (semicolon-separated)
            UIFactory.CreateLabel(this.ContentRoot, "CompAllowHeader", "Component Allowlist (; separated FullTypeNames)", TextAnchor.MiddleLeft, Color.white, false, 12);
            var allowRow = UIFactory.CreateUIObject("CompAllowRow", this.ContentRoot);
            UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(allowRow, false, false, true, true, 5, 2, 2, 2, 2);
            var input = UIFactory.CreateInputField(allowRow, "CompAllowInput", string.Join(';', cfg.ComponentAllowlist ?? Array.Empty<string>()));
            UIFactory.SetLayoutElement(input.UIRoot, minHeight: 25, flexibleWidth: 9999);
            var saveAllow = UIFactory.CreateButton(allowRow, "SaveAllow", "Save", new Color(0.2f, 0.3f, 0.2f));
            UIFactory.SetLayoutElement(saveAllow.Component.gameObject, minHeight: 25, minWidth: 80);
            saveAllow.OnClick += () =>
            {
                try
                {
                    var c = McpConfig.Load();
                    var parts = (input.Text ?? "").Split(';');
                    var list = new List<string>();
                    foreach (var p in parts)
                    {
                        var s = p.Trim();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                    c.ComponentAllowlist = list.ToArray();
                    McpConfig.Save(c);
                    ExplorerCore.Log("Saved MCP component allowlist.");
                }
                catch (Exception ex) { ExplorerCore.LogWarning($"Failed to save allowlist: {ex.Message}"); }
            };

            // Reflection allowlist editor
            UIFactory.CreateLabel(this.ContentRoot, "ReflAllowHeader", "Reflection Allowlist (; separated Type.Member)", TextAnchor.MiddleLeft, Color.white, false, 12);
            var reflRow = UIFactory.CreateUIObject("ReflAllowRow", this.ContentRoot);
            UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(reflRow, false, false, true, true, 5, 2, 2, 2, 2);
            var reflInput = UIFactory.CreateInputField(reflRow, "ReflAllowInput", string.Join(';', cfg.ReflectionAllowlistMembers ?? Array.Empty<string>()));
            UIFactory.SetLayoutElement(reflInput.UIRoot, minHeight: 25, flexibleWidth: 9999);
            var saveRefl = UIFactory.CreateButton(reflRow, "SaveReflAllow", "Save", new Color(0.2f, 0.3f, 0.2f));
            UIFactory.SetLayoutElement(saveRefl.Component.gameObject, minHeight: 25, minWidth: 80);
            saveRefl.OnClick += () =>
            {
                try
                {
                    var c = McpConfig.Load();
                    var parts = (reflInput.Text ?? "").Split(';');
                    var list = new List<string>();
                    foreach (var p in parts)
                    {
                        var s = p.Trim();
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                    c.ReflectionAllowlistMembers = list.ToArray();
                    McpConfig.Save(c);
                    ExplorerCore.Log("Saved MCP reflection allowlist.");
                }
                catch (Exception ex) { ExplorerCore.LogWarning($"Failed to save reflection allowlist: {ex.Message}"); }
            };

            // Endpoint label + status
            string endpoint = info?.BaseUrl ?? "(not running)";
            Text endpointLabel = UIFactory.CreateLabel(this.ContentRoot, "McpEndpoint", $"Endpoint: {endpoint}", TextAnchor.MiddleLeft, Color.gray, false, 12);

            string statusText;
            Color statusColor;
            if (!cfg.Enabled)
            {
                statusText = "MCP Status: Disabled";
                statusColor = Color.gray;
            }
            else if (info == null)
            {
                statusText = "MCP Status: Enabled (not running)";
                statusColor = Color.yellow;
            }
            else
            {
                statusText = "MCP Status: Enabled & Running";
                statusColor = Color.green;
            }

            Text statusLabel = UIFactory.CreateLabel(this.ContentRoot, "McpStatus", statusText, TextAnchor.MiddleLeft, statusColor, false, 12);

            // Enabled toggle behavior (updates config, status and endpoint)
            enabledToggle.onValueChanged.AddListener((bool v) =>
            {
                var c = McpConfig.Load();
                c.Enabled = v;
                McpConfig.Save(c);

                try
                {
                    if (v)
                    {
                        Mcp.McpHost.StartIfEnabled();
                        Notification.ShowMessage("MCP enabled.");
                    }
                    else
                    {
                        Mcp.McpHost.Stop();
                        Notification.ShowMessage("MCP disabled.");
                    }
                }
                catch { }

                var latest = McpDiscovery.TryLoad();
                endpointLabel.text = $"Endpoint: {latest?.BaseUrl ?? "(not running)"}";

                if (!v)
                {
                    statusLabel.text = "MCP Status: Disabled";
                    statusLabel.color = Color.gray;
                }
                else if (latest == null)
                {
                    statusLabel.text = "MCP Status: Enabled (not running)";
                    statusLabel.color = Color.yellow;
                }
                else
                {
                    statusLabel.text = "MCP Status: Enabled & Running";
                    statusLabel.color = Color.green;
                }
            });

#else
            UIFactory.CreateLabel(this.ContentRoot, "McpHeader", "MCP Server", TextAnchor.MiddleLeft, Color.white, true);
            UIFactory.CreateLabel(this.ContentRoot, "McpUnavailable", "MCP is disabled in this build (INTEROP not defined).", TextAnchor.MiddleLeft, Color.gray, false, 12);
            UIFactory.CreateLabel(this.ContentRoot, "McpUnavailableHint", "Use a CoreCLR/INTEROP target for MCP hosting.", TextAnchor.MiddleLeft, Color.gray, false, 11);
#endif

            // Config entries

            ScrollPool<ConfigEntryCell> scrollPool = UIFactory.CreateScrollPool<ConfigEntryCell>(
                this.ContentRoot, 
                "ConfigEntries", 
                out GameObject scrollObj,
                out GameObject scrollContent);

            scrollPool.Initialize(this);
        }
    }
}
