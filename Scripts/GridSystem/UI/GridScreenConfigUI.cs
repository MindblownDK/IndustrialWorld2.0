// Assets/Scripts/VoxelEngine/GridSystem/UI/GridScreenConfigUI.cs
//
// Configuration panel for GridScreenBlock.
// Opens when the player interacts (right-clicks) a screen block on a grid.
// Shows available data sources, lets the player pick one, and customizes
// the display mode and colours.
//
// v5.43.0-dev — Grid Screens & Displays.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;

namespace VoxelEngine.GridSystem.UI
{
    public class GridScreenConfigUI : MonoBehaviour
    {
        public static GridScreenConfigUI Instance { get; private set; }

        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _panel;
        private GridScreenBlock _target;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            Hide();
        }

        public void Open(GridScreenBlock screen)
        {
            if (screen == null) return;
            _target = screen;
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.04f, 0.75f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            UIState.PushBlock();
            Build();
        }

        public void Close()
        {
            _target = null;
            UIState.PopBlock();
            Hide();
        }

        private void Hide()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
        }

        private void Build()
        {
            if (_target == null) return;

            _panel = new VisualElement();
            _panel.style.width = 480;
            _panel.style.maxHeight = new StyleLength(new Length(80f, LengthUnit.Percent));
            _panel.style.backgroundColor = new StyleColor(new Color(0.08f, 0.09f, 0.12f, 0.98f));
            _panel.style.paddingTop = 16; _panel.style.paddingBottom = 16;
            _panel.style.paddingLeft = 18; _panel.style.paddingRight = 18;
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth =
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 1;
            _panel.style.borderTopColor = _panel.style.borderBottomColor =
            _panel.style.borderLeftColor = _panel.style.borderRightColor = new StyleColor(new Color(0.20f, 0.23f, 0.28f));
            UITheme.Radius(_panel, 10);
            _root.Add(_panel);

            // Header
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 12;
            _panel.Add(header);

            var title = new Label("SCREEN CONFIG");
            title.style.color = new Color(0.92f, 0.94f, 0.97f);
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 2;
            title.style.flexGrow = 1;
            header.Add(title);

            var closeBtn = new Button(Close) { text = "X" };
            closeBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
            closeBtn.style.fontSize = 14;
            closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeBtn.style.backgroundColor = new StyleColor(new Color(0.45f, 0.18f, 0.18f));
            closeBtn.style.minWidth = 28; closeBtn.style.minHeight = 28;
            UITheme.Radius(closeBtn, 4);
            header.Add(closeBtn);

            // Data sources list
            var sources = _target.GetAvailableSources();
            if (sources.Count == 0)
            {
                var noSrc = new Label("No data sources found on this grid.\nPlace batteries, cargo containers, or other powered blocks first.");
                noSrc.style.color = new Color(0.60f, 0.64f, 0.72f);
                noSrc.style.fontSize = 12;
                noSrc.style.whiteSpace = WhiteSpace.Normal;
                _panel.Add(noSrc);
                return;
            }

            var srcHeader = new Label("DATA SOURCE");
            srcHeader.style.color = new Color(0.40f, 0.44f, 0.52f);
            srcHeader.style.fontSize = 9;
            srcHeader.style.letterSpacing = 2;
            srcHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            srcHeader.style.marginBottom = 4;
            _panel.Add(srcHeader);

            var srcScroll = new ScrollView(ScrollViewMode.Vertical);
            srcScroll.style.maxHeight = 180;
            _panel.Add(srcScroll);

            foreach (var (pos, provider) in sources)
            {
                var sourceBtn = new Button(() =>
                {
                    _target.SetDataSource(pos, (provider as GridBlock)?.GetInstanceID() ?? 0);
                    Rebuild();
                }) { text = $"  {provider.SourceName}  [{provider.DataCategory}]" };
                sourceBtn.style.minHeight = 26;
                sourceBtn.style.marginBottom = 2;
                sourceBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                sourceBtn.style.fontSize = 11;
                sourceBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                sourceBtn.style.backgroundColor = new StyleColor(
                    pos == _target.dataSourceGridPos ? new Color(0.15f, 0.20f, 0.30f) : new Color(0.12f, 0.14f, 0.18f));
                sourceBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
                sourceBtn.style.paddingLeft = 10;
                UITheme.Radius(sourceBtn, 4);
                sourceBtn.style.borderLeftWidth = sourceBtn.style.borderRightWidth =
                sourceBtn.style.borderTopWidth = sourceBtn.style.borderBottomWidth = 0;
                if (pos == _target.dataSourceGridPos)
                {
                    sourceBtn.style.borderLeftWidth = 3;
                    sourceBtn.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.55f, 0.95f));
                }
                srcScroll.Add(sourceBtn);
            }

            // Display mode
            var modeHeader = new Label("DISPLAY MODE");
            modeHeader.style.color = new Color(0.40f, 0.44f, 0.52f);
            modeHeader.style.fontSize = 9;
            modeHeader.style.letterSpacing = 2;
            modeHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            modeHeader.style.marginTop = 8;
            modeHeader.style.marginBottom = 4;
            _panel.Add(modeHeader);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.flexWrap = Wrap.Wrap;
            _panel.Add(modeRow);

            foreach (ScreenDataMode mode in System.Enum.GetValues(typeof(ScreenDataMode)))
            {
                var modeBtn = new Button(() =>
                {
                    _target.dataMode = mode;
                    Rebuild();
                }) { text = mode.ToString() };
                modeBtn.style.minHeight = 24;
                modeBtn.style.marginRight = 4;
                modeBtn.style.marginBottom = 4;
                modeBtn.style.fontSize = 10;
                modeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                bool active = mode == _target.dataMode;
                modeBtn.style.backgroundColor = new StyleColor(
                    active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
                modeBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                modeBtn.style.paddingLeft = 8; modeBtn.style.paddingRight = 8;
                UITheme.Radius(modeBtn, 4);
                modeBtn.style.borderLeftWidth = modeBtn.style.borderRightWidth =
                modeBtn.style.borderTopWidth = modeBtn.style.borderBottomWidth = 0;
                modeRow.Add(modeBtn);
            }

            // Preview
            var prevHeader = new Label("PREVIEW");
            prevHeader.style.color = new Color(0.40f, 0.44f, 0.52f);
            prevHeader.style.fontSize = 9;
            prevHeader.style.letterSpacing = 2;
            prevHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            prevHeader.style.marginTop = 8;
            prevHeader.style.marginBottom = 4;
            _panel.Add(prevHeader);

            var previewBox = new VisualElement();
            previewBox.style.backgroundColor = new StyleColor(_target.backgroundColor);
            previewBox.style.paddingTop = 8; previewBox.style.paddingBottom = 8;
            previewBox.style.paddingLeft = 10; previewBox.style.paddingRight = 10;
            UITheme.Radius(previewBox, 4);
            previewBox.style.minHeight = 40;
            _panel.Add(previewBox);

            var previewText = new Label(_target.FormattedDisplay);
            previewText.style.color = new StyleColor(_target.textColor);
            previewText.style.fontSize = 11;
            previewText.style.whiteSpace = WhiteSpace.Normal;
            previewBox.Add(previewText);

            // Close hint
            var hint = new Label("Close this panel to see the screen update.");
            hint.style.color = new Color(0.40f, 0.44f, 0.52f);
            hint.style.fontSize = 10;
            hint.style.marginTop = 8;
            hint.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(hint);
        }

        private void Rebuild()
        {
            Build();
        }

        private void Update()
        {
            if (_target != null && _panel != null && _root != null && _root.panel != null)
            {
                // Escape to close
                if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                    Close();
            }
        }
    }
}
