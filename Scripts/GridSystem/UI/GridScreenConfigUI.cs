// Assets/Scripts/VoxelEngine/GridSystem/UI/GridScreenConfigUI.cs
//
// Configuration panel for GridScreenBlock.
// v5.44.0-dev — Fixed: no multiple modals, no Arial font error, custom text fixed.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.GridSystem.UI
{
    public class GridScreenConfigUI : MonoBehaviour
    {
        public static GridScreenConfigUI Instance { get; private set; }

        private UIDocument _doc;
        private VisualElement _root;
        private VisualElement _panel;
        private GridScreenBlock _target;
        private VisualElement _previewBox;
        private Label _previewText;
        private bool _open;

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

        private void Update()
        {
            if (!_open) return;

            // Escape closes
            if (GameSettings.WasPressed(InputAction.Pause))
            {
                Close();
                return;
            }

            // Live preview update
            if (_previewText != null && _target != null)
                _previewText.text = _target.FormattedDisplay;
        }

        public void Open(GridScreenBlock screen)
        {
            if (screen == null) return;
            if (_open) Close(); // prevent multiple modals
            _target = screen;
            _open = true;
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
            if (!_open) return;
            _open = false;
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

        /// <summary>Called from the ship master terminal. Opens a screen's config from anywhere.</summary>
        public void OpenForScreen(GridScreenBlock screen)
        {
            if (screen == null) return;
            Open(screen);
        }

        private void Build()
        {
            if (_target == null) return;

            _panel = new VisualElement();
            _panel.style.width = 500;
            _panel.style.maxHeight = new StyleLength(new Length(85f, LengthUnit.Percent));
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

            // Screen name + power status
            var infoRow = new VisualElement();
            infoRow.style.flexDirection = FlexDirection.Row;
            infoRow.style.alignItems = Align.Center;
            infoRow.style.marginBottom = 8;
            _panel.Add(infoRow);

            var nameLbl = new Label(_target.blockName);
            nameLbl.style.color = new Color(0.92f, 0.94f, 0.97f);
            nameLbl.style.fontSize = 13;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.flexGrow = 1;
            infoRow.Add(nameLbl);

            string powerText = _target.IsPowered ? "⚡POWERED" : "OFFLINE";
            Color powerColor = _target.IsPowered ? new Color(0.35f, 0.80f, 0.45f) : new Color(0.80f, 0.20f, 0.20f);
            var powerLbl = new Label(powerText);
            powerLbl.style.color = powerColor;
            powerLbl.style.fontSize = 10;
            powerLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            powerLbl.style.letterSpacing = 1;
            infoRow.Add(powerLbl);

            // Data sources
            var srcHeader = new Label("DATA SOURCE");
            srcHeader.style.color = new Color(0.40f, 0.44f, 0.52f);
            srcHeader.style.fontSize = 9;
            srcHeader.style.letterSpacing = 2;
            srcHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            srcHeader.style.marginBottom = 4;
            _panel.Add(srcHeader);

            var sources = _target.GetAvailableSources();
            if (sources.Count == 0)
            {
                var noSrc = new Label("No data sources on this grid. Place batteries, cargo, or gas tanks first.");
                noSrc.style.color = new Color(0.60f, 0.64f, 0.72f);
                noSrc.style.fontSize = 11;
                noSrc.style.whiteSpace = WhiteSpace.Normal;
                _panel.Add(noSrc);
            }
            else
            {
                var srcScroll = new ScrollView(ScrollViewMode.Vertical);
                srcScroll.style.maxHeight = 120;
                _panel.Add(srcScroll);

                foreach (var (pos, provider) in sources)
                {
                    bool isSelected = pos == _target.dataSourceGridPos;
                    var sourceBtn = new Button(() =>
                    {
                        _target.SetDataSource(pos, (provider as GridBlock)?.GetInstanceID() ?? 0);
                        RefreshHighlight();
                    }) { text = "  " + provider.SourceName + "  [" + provider.DataCategory + "]" };
                    sourceBtn.style.minHeight = 26;
                    sourceBtn.style.marginBottom = 2;
                    sourceBtn.style.fontSize = 11;
                    sourceBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                    sourceBtn.style.unityTextAlign = TextAnchor.MiddleLeft;
                    sourceBtn.style.paddingLeft = 10;
                    UITheme.Radius(sourceBtn, 4);
                    sourceBtn.style.borderLeftWidth = sourceBtn.style.borderRightWidth =
                    sourceBtn.style.borderTopWidth = sourceBtn.style.borderBottomWidth = 0;
                    sourceBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                    sourceBtn.style.backgroundColor = new StyleColor(isSelected ? new Color(0.15f, 0.20f, 0.30f) : new Color(0.12f, 0.14f, 0.18f));
                    if (isSelected)
                    {
                        sourceBtn.style.borderLeftWidth = 3;
                        sourceBtn.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.55f, 0.95f));
                    }
                    srcScroll.Add(sourceBtn);
                }
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
                bool isActive = mode == _target.dataMode;
                var modeBtn = new Button(() =>
                {
                    _target.dataMode = mode;
                    // Close and reopen to show/hide custom text field
                    Close();
                    Open(_target);
                }) { text = mode.ToString() };
                modeBtn.style.minHeight = 24;
                modeBtn.style.marginRight = 4;
                modeBtn.style.marginBottom = 4;
                modeBtn.style.fontSize = 10;
                modeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                modeBtn.style.paddingLeft = 8; modeBtn.style.paddingRight = 8;
                modeBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                modeBtn.style.backgroundColor = new StyleColor(isActive ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
                UITheme.Radius(modeBtn, 4);
                modeBtn.style.borderLeftWidth = modeBtn.style.borderRightWidth =
                modeBtn.style.borderTopWidth = modeBtn.style.borderBottomWidth = 0;
                modeRow.Add(modeBtn);
            }

            // Custom text input (only in Custom mode)
            if (_target.dataMode == ScreenDataMode.Custom)
            {
                var ctHeader = new Label("CUSTOM TEXT");
                ctHeader.style.color = new Color(0.40f, 0.44f, 0.52f);
                ctHeader.style.fontSize = 9;
                ctHeader.style.letterSpacing = 2;
                ctHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
                ctHeader.style.marginTop = 6;
                ctHeader.style.marginBottom = 3;
                _panel.Add(ctHeader);

                var ctField = new TextField();
                ctField.value = _target.customText;
                ctField.multiline = true;
                ctField.style.minHeight = 50;
                ctField.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f));
                ctField.style.color = new Color(0.92f, 0.94f, 0.97f);
                ctField.style.borderLeftWidth = ctField.style.borderRightWidth =
                ctField.style.borderTopWidth = ctField.style.borderBottomWidth = 0;
                ctField.style.whiteSpace = WhiteSpace.Normal;
                ctField.RegisterValueChangedCallback(evt =>
                {
                    _target.customText = evt.newValue;
                });
                _panel.Add(ctField);
            }

            // Preview
            var prevHeader = new Label("LIVE PREVIEW");
            prevHeader.style.color = new Color(0.40f, 0.44f, 0.52f);
            prevHeader.style.fontSize = 9;
            prevHeader.style.letterSpacing = 2;
            prevHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            prevHeader.style.marginTop = 8;
            prevHeader.style.marginBottom = 4;
            _panel.Add(prevHeader);

            _previewBox = new VisualElement();
            _previewBox.style.backgroundColor = new StyleColor(new Color(0.025f, 0.03f, 0.045f));
            _previewBox.style.paddingTop = 8; _previewBox.style.paddingBottom = 8;
            _previewBox.style.paddingLeft = 10; _previewBox.style.paddingRight = 10;
            _previewBox.style.borderLeftWidth = _previewBox.style.borderRightWidth =
            _previewBox.style.borderTopWidth = _previewBox.style.borderBottomWidth = 1;
            _previewBox.style.borderLeftColor = _previewBox.style.borderRightColor =
            _previewBox.style.borderTopColor = _previewBox.style.borderBottomColor = new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.30f));
            UITheme.Radius(_previewBox, 4);
            _previewBox.style.minHeight = 36;
            _panel.Add(_previewBox);

            _previewText = new Label(_target.FormattedDisplay);
            _previewText.style.color = new StyleColor(_target.textColor);
            _previewText.style.fontSize = 11;
            _previewText.style.whiteSpace = WhiteSpace.Normal;
            _previewBox.Add(_previewText);

            // Hint
            var hint = new Label("Close to apply. Text updates live on the screen.");
            hint.style.color = new Color(0.40f, 0.44f, 0.52f);
            hint.style.fontSize = 10;
            hint.style.marginTop = 8;
            hint.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(hint);
        }

        private void RefreshHighlight()
        {
            // Close and reopen so the panel rebuilds with new highlights
            if (_target == null) return;
            _open = false;
            UIState.PopBlock();
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
            Open(_target);
        }
    }
}
