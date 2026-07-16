// Assets/Scripts/VoxelEngine/GridSystem/UI/GridScreenConfigUI.cs
//
// Configuration panel for GridScreenBlock.
// v5.44.0-dev — Fixed: never closes on value changes, live update only.

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
        private Label _previewText;
        private bool _open;
        private List<Button> _sourceBtns = new();
        private List<Button> _modeBtns = new();

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
            if (GameSettings.WasPressed(InputAction.Pause)) { Close(); return; }
            if (_previewText != null && _target != null)
                _previewText.text = _target.FormattedDisplay;
        }

        public void Open(GridScreenBlock screen)
        {
            if (screen == null) return;
            if (_open) { Close(); }
            _target = screen;
            _open = true;
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.04f, 0.75f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            UIState.PushBlock();
            _sourceBtns.Clear();
            _modeBtns.Clear();
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
            _root.Clear(); _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
        }

        public void OpenForScreen(GridScreenBlock screen) { Open(screen); }

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

            // ── Header ──
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row; header.style.alignItems = Align.Center;
            header.style.marginBottom = 12;
            _panel.Add(header);

            var title = new Label("SCREEN CONFIG");
            title.style.color = new Color(0.92f, 0.94f, 0.97f); title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.letterSpacing = 2;
            title.style.flexGrow = 1;
            header.Add(title);

            var closeBtn = new Button(Close) { text = "X" };
            closeBtn.style.color = new Color(0.92f, 0.94f, 0.97f); closeBtn.style.fontSize = 14;
            closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeBtn.style.backgroundColor = new StyleColor(new Color(0.45f, 0.18f, 0.18f));
            closeBtn.style.minWidth = 28; closeBtn.style.minHeight = 28;
            UITheme.Radius(closeBtn, 4);
            header.Add(closeBtn);

            // ── Screen info ──
            var info = new VisualElement();
            info.style.flexDirection = FlexDirection.Row; info.style.alignItems = Align.Center;
            info.style.marginBottom = 8;
            _panel.Add(info);

            var nLbl = new Label(_target.blockName + "  [" + _target.screenSize + "]");
            nLbl.style.color = new Color(0.92f, 0.94f, 0.97f); nLbl.style.fontSize = 13;
            nLbl.style.unityFontStyleAndWeight = FontStyle.Bold; nLbl.style.flexGrow = 1;
            info.Add(nLbl);

            string pwr = _target.IsPowered ? "POWERED" : "OFFLINE";
            Color pc = _target.IsPowered ? new Color(0.35f, 0.80f, 0.45f) : new Color(0.80f, 0.20f, 0.20f);
            var pLbl = new Label(pwr);
            pLbl.style.color = pc; pLbl.style.fontSize = 10;
            pLbl.style.unityFontStyleAndWeight = FontStyle.Bold; pLbl.style.letterSpacing = 1;
            info.Add(pLbl);

            // ── Data sources ──
            var sHdr = new Label("DATA SOURCE");
            sHdr.style.color = new Color(0.40f, 0.44f, 0.52f); sHdr.style.fontSize = 9;
            sHdr.style.letterSpacing = 2; sHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            sHdr.style.marginBottom = 4;
            _panel.Add(sHdr);

            var sources = _target.GetAvailableSources();
            var srcScroll = new ScrollView(ScrollViewMode.Vertical);
            srcScroll.style.maxHeight = 120;
            _panel.Add(srcScroll);

            // "None" button to clear the source
            var noneBtn = MakeSourceButton("  None", () =>
            {
                _target.SetDataSource(Vector3Int.zero, 0);
                RefreshSourceHighlights();
            });
            srcScroll.Add(noneBtn);
            _sourceBtns.Add(noneBtn);

            foreach (var (pos, provider) in sources)
            {
                var idx = pos; // capture for closure
                var btn = MakeSourceButton("  " + provider.SourceName + "  [" + provider.DataCategory + "]", () =>
                {
                    _target.SetDataSource(idx, (provider as GridBlock)?.GetInstanceID() ?? 0);
                    RefreshSourceHighlights();
                });
                srcScroll.Add(btn);
                _sourceBtns.Add(btn);
            }
            RefreshSourceHighlights();

            // ── Display mode ──
            var mHdr = new Label("DISPLAY MODE");
            mHdr.style.color = new Color(0.40f, 0.44f, 0.52f); mHdr.style.fontSize = 9;
            mHdr.style.letterSpacing = 2; mHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            mHdr.style.marginTop = 8; mHdr.style.marginBottom = 4;
            _panel.Add(mHdr);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row; modeRow.style.flexWrap = Wrap.Wrap;
            _panel.Add(modeRow);

            foreach (ScreenDataMode mode in System.Enum.GetValues(typeof(ScreenDataMode)))
            {
                var capturedMode = mode;
                var mBtn = new Button(() =>
                {
                    _target.dataMode = capturedMode;
                    RefreshModeHighlights();
                    RefreshCustomTextField();
                }) { text = mode.ToString() };
                mBtn.style.minHeight = 24; mBtn.style.marginRight = 4; mBtn.style.marginBottom = 4;
                mBtn.style.fontSize = 10; mBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                mBtn.style.paddingLeft = 8; mBtn.style.paddingRight = 8;
                mBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                UITheme.Radius(mBtn, 4);
                mBtn.style.borderLeftWidth = mBtn.style.borderRightWidth =
                mBtn.style.borderTopWidth = mBtn.style.borderBottomWidth = 0;
                modeRow.Add(mBtn);
                _modeBtns.Add(mBtn);
            }
            RefreshModeHighlights();

            // ── Custom text field ──
            // This is added lazily — stored as a named element we can remove/re-add
            RefreshCustomTextField();

            // ── Preview ──
            var pHdr = new Label("LIVE PREVIEW");
            pHdr.style.color = new Color(0.40f, 0.44f, 0.52f); pHdr.style.fontSize = 9;
            pHdr.style.letterSpacing = 2; pHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            pHdr.style.marginTop = 8; pHdr.style.marginBottom = 4;
            _panel.Add(pHdr);

            var pBox = new VisualElement();
            pBox.style.backgroundColor = new StyleColor(new Color(0.025f, 0.03f, 0.045f));
            pBox.style.paddingTop = 8; pBox.style.paddingBottom = 8;
            pBox.style.paddingLeft = 10; pBox.style.paddingRight = 10;
            pBox.style.borderLeftWidth = pBox.style.borderRightWidth =
            pBox.style.borderTopWidth = pBox.style.borderBottomWidth = 1;
            pBox.style.borderLeftColor = pBox.style.borderRightColor =
            pBox.style.borderTopColor = pBox.style.borderBottomColor = new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.30f));
            UITheme.Radius(pBox, 4); pBox.style.minHeight = 36;
            _panel.Add(pBox);

            _previewText = new Label(_target.FormattedDisplay);
            _previewText.style.color = new StyleColor(_target.textColor);
            _previewText.style.fontSize = 11; _previewText.style.whiteSpace = WhiteSpace.Normal;
            pBox.Add(_previewText);

            // ── Hint ──
            var hint = new Label("Changes apply live. Close to finish.");
            hint.style.color = new Color(0.40f, 0.44f, 0.52f); hint.style.fontSize = 10;
            hint.style.marginTop = 8; hint.style.whiteSpace = WhiteSpace.Normal;
            _panel.Add(hint);
        }

        private Button MakeSourceButton(string label, System.Action onClick)
        {
            var btn = new Button(onClick) { text = label };
            btn.style.minHeight = 26; btn.style.marginBottom = 2;
            btn.style.fontSize = 11; btn.style.unityFontStyleAndWeight = FontStyle.Bold;
            btn.style.unityTextAlign = TextAnchor.MiddleLeft; btn.style.paddingLeft = 10;
            UITheme.Radius(btn, 4);
            btn.style.borderLeftWidth = btn.style.borderRightWidth =
            btn.style.borderTopWidth = btn.style.borderBottomWidth = 0;
            btn.style.color = new Color(0.92f, 0.94f, 0.97f);
            return btn;
        }

        private void RefreshSourceHighlights()
        {
            bool anySelected = _target.dataSourceGridPos != Vector3Int.zero || _target.dataSourceInstanceId != 0;
            foreach (var btn in _sourceBtns)
            {
                bool isNone = btn.text.Contains("None");
                bool selected = isNone ? !anySelected : false;

                // Check if this button's source matches the target's source
                if (!isNone)
                {
                    string srcName = _target.ResolveProvider()?.SourceName ?? "";
                    if (btn.text.Contains(srcName) && srcName.Length > 0)
                        selected = true;
                }

                btn.style.backgroundColor = new StyleColor(selected ? new Color(0.15f, 0.20f, 0.30f) : new Color(0.12f, 0.14f, 0.18f));
                btn.style.borderLeftWidth = selected ? 3 : 0;
                if (selected) btn.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.55f, 0.95f));
            }
        }

        private void RefreshModeHighlights()
        {
            foreach (var btn in _modeBtns)
            {
                bool active = btn.text == _target.dataMode.ToString();
                btn.style.backgroundColor = new StyleColor(active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }
        }

        private void RefreshCustomTextField()
        {
            if (_panel == null || _target == null) return;

            // Remove old custom text section if present
            var oldSection = _panel.Q("CustomTextSection");
            if (oldSection != null) _panel.Remove(oldSection);

            if (_target.dataMode != ScreenDataMode.Custom) return;

            var ctSection = new VisualElement { name = "CustomTextSection" };

            var ctHdr = new Label("CUSTOM TEXT");
            ctHdr.style.color = new Color(0.40f, 0.44f, 0.52f); ctHdr.style.fontSize = 9;
            ctHdr.style.letterSpacing = 2; ctHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            ctHdr.style.marginTop = 6; ctHdr.style.marginBottom = 3;
            ctSection.Add(ctHdr);

            var ctField = new TextField();
            ctField.value = _target.customText;
            ctField.multiline = true;
            ctField.style.minHeight = 50;
            ctField.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f));
            ctField.style.color = new Color(0.92f, 0.94f, 0.97f);
            ctField.style.borderLeftWidth = ctField.style.borderRightWidth =
            ctField.style.borderTopWidth = ctField.style.borderBottomWidth = 0;
            ctField.style.whiteSpace = WhiteSpace.Normal;
            ctField.RegisterValueChangedCallback(evt => { _target.customText = evt.newValue; });
            ctSection.Add(ctField);

            // Find the preview header and insert before it
            for (int i = 0; i < _panel.childCount; i++)
            {
                if (_panel[i] is Label lbl && lbl.text == "LIVE PREVIEW")
                {
                    _panel.Insert(i, ctSection);
                    return;
                }
            }

            // Fallback: just add at the end
            _panel.Add(ctSection);
        }
    }
}
