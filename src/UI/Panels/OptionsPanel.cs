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
            UIFactory.CreateLabel(this.ContentRoot, "McpHeader", "MCP Server", TextAnchor.MiddleLeft, Color.white, true);
            var row = UIFactory.CreateUIObject("McpRow", this.ContentRoot);
            UIFactory.SetLayoutGroup<HorizontalLayoutGroup>(row, false, false, true, true, 5, 2, 2, 2, 2);

            var cfg = McpConfig.Load();
            // AllowWrites toggle
            var allowObj = UIFactory.CreateToggle(row, "AllowWritesToggle", out Toggle allowToggle, out Text allowText);
            allowText.text = "Allow Writes";
            allowToggle.isOn = cfg.AllowWrites;
            allowToggle.onValueChanged.AddListener((bool v) => { var c = McpConfig.Load(); c.AllowWrites = v; McpConfig.Save(c); });
            UIFactory.SetLayoutElement(allowObj, minHeight: 25, minWidth: 140);

            // RequireConfirm toggle
            var confirmObj = UIFactory.CreateToggle(row, "RequireConfirmToggle", out Toggle confirmToggle, out Text confirmText);
            confirmText.text = "Require Confirm";
            confirmToggle.isOn = cfg.RequireConfirm;
            confirmToggle.onValueChanged.AddListener((bool v) => { var c = McpConfig.Load(); c.RequireConfirm = v; McpConfig.Save(c); });
            UIFactory.SetLayoutElement(confirmObj, minHeight: 25, minWidth: 160);

            // Restart button
            var restartBtn = UIFactory.CreateButton(row, "RestartMcp", "Restart MCP", new Color(0.2f, 0.2f, 0.3f));
            UIFactory.SetLayoutElement(restartBtn.Component.gameObject, minHeight: 25, minWidth: 120);
            restartBtn.OnClick += () => { try { Mcp.McpHost.Stop(); Mcp.McpHost.StartIfEnabled(); } catch { } };

            // Endpoint label
            var info = McpDiscovery.TryLoad();
            string endpoint = info?.BaseUrl ?? "(not running)";
            UIFactory.CreateLabel(this.ContentRoot, "McpEndpoint", $"Endpoint: {endpoint}", TextAnchor.MiddleLeft, Color.gray, false, 12);

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
