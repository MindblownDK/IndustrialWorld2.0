// Assets/Scripts/VoxelEngine/GridSystem/UI/GridScreenConfigUI.cs
//
// Configuration panel for GridScreenBlock.
// v5.57.1-dev — Duplicate-safe singleton so scene-authored component is never stripped.

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
        private VisualElement _root, _panel;
        private GridScreenBlock _target;
        private Label _previewText, _sourceCount;
        private bool _open;
        private readonly List<Button> _sourceBtns = new();
        private readonly List<Button> _modeBtns = new();
        private readonly List<Button> _borderBtns = new();
        private readonly List<Button> _fontBtns = new();

        // ── Auto-create if missing from scene ─────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (Instance != null) return;

            var existing = Object.FindAnyObjectByType<GridScreenConfigUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                Instance = existing;
                return;
            }

            var go = new GameObject("GridScreenConfigUI");
            go.AddComponent<UnityEngine.UIElements.UIDocument>();
            go.AddComponent<GridScreenConfigUI>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // Prefer the scene/player-authored object over a runtime-generated root singleton.
                // Destroying only the component made Unity remove the script from the configured
                // object, so duplicates are handled at GameObject level instead.
                bool thisLooksSceneAuthored = transform.parent != null;
                bool existingLooksRuntimeGenerated = Instance != null && Instance.transform.parent == null;
                if (thisLooksSceneAuthored && existingLooksRuntimeGenerated)
                {
                    Destroy(Instance.gameObject);
                    Instance = this;
                }
                else
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                Instance = this;
            }

            _doc = GetComponent<UIDocument>();
            BringDocumentToFront();
            if (_doc.panelSettings == null)
            {
                var settings = Resources.Load<PanelSettings>("MenuPanelSettings");
                if (settings != null) _doc.panelSettings = settings;
            }
            if (_doc.panelSettings == null)
            {
                var existing = FindAnyObjectByType<UIDocument>();
                if (existing != null) _doc.panelSettings = existing.panelSettings;
            }
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            Hide();
            if (transform.parent == null)
                DontDestroyOnLoad(gameObject);
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
            if (_open) Close();
            _target = screen;
            _open = true;
            BringDocumentToFront();
            _root.Clear();
            _root.style.display = DisplayStyle.Flex;
            _root.style.position = Position.Absolute;
            _root.style.left = 0; _root.style.right = 0; _root.style.top = 0; _root.style.bottom = 0;
            _root.BringToFront();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.04f, 0.75f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            UIState.PushBlock();
            _sourceBtns.Clear(); _modeBtns.Clear(); _borderBtns.Clear(); _fontBtns.Clear();
            Build();
        }

        public void Close()
        {
            if (!_open) return;
            _root.style.display = DisplayStyle.None;
            _open = false; _target = null; UIState.PopBlock(); Hide();
        }

        private void Hide()
        {
            _root.Clear(); _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
        }

        public void OpenForScreen(GridScreenBlock screen) { Open(screen); }

        private void BringDocumentToFront()
        {
            if (_doc == null) return;
            try
            {
                var sortingOrderProperty = typeof(UIDocument).GetProperty("sortingOrder");
                sortingOrderProperty?.SetValue(_doc, 30000, null);
            }
            catch { /* Older UI Toolkit versions can ignore sorting order; root BringToFront still applies inside this document. */ }
        }

        private void Build()
        {
            if (_target == null) return;

            _panel = new VisualElement();
            _panel.style.width = 540;
            _panel.style.maxHeight = new StyleLength(new Length(88f, LengthUnit.Percent));
            _panel.style.backgroundColor = new StyleColor(new Color(0.08f, 0.09f, 0.12f, 0.98f));
            _panel.style.paddingTop = 14; _panel.style.paddingBottom = 14;
            _panel.style.paddingLeft = 16; _panel.style.paddingRight = 16;
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth =
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 1;
            _panel.style.borderTopColor = _panel.style.borderBottomColor =
            _panel.style.borderLeftColor = _panel.style.borderRightColor = new StyleColor(new Color(0.20f, 0.23f, 0.28f));
            UITheme.Radius(_panel, 10);
            _root.Add(_panel);

            // ── Header ──
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row; hdr.style.alignItems = Align.Center;
            hdr.style.marginBottom = 10;
            _panel.Add(hdr);
            var title = new Label("SCREEN CONFIG");
            title.style.color = new Color(0.92f, 0.94f, 0.97f); title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.letterSpacing = 2;
            title.style.flexGrow = 1;
            hdr.Add(title);

            _sourceCount = new Label(_target.SourceCount + " source(s)");
            _sourceCount.style.color = new Color(0.40f, 0.44f, 0.52f);
            _sourceCount.style.fontSize = 10; _sourceCount.style.marginRight = 8;
            hdr.Add(_sourceCount);

            var closeBtn = new Button(Close) { text = "X" };
            closeBtn.style.color = new Color(0.92f, 0.94f, 0.97f); closeBtn.style.fontSize = 14;
            closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeBtn.style.backgroundColor = new StyleColor(new Color(0.45f, 0.18f, 0.18f));
            closeBtn.style.minWidth = 26; closeBtn.style.minHeight = 26;
            UITheme.Radius(closeBtn, 4);
            hdr.Add(closeBtn);

            // ── Screen info ──
            var info = new VisualElement();
            info.style.flexDirection = FlexDirection.Row; info.style.alignItems = Align.Center;
            info.style.marginBottom = 8;
            _panel.Add(info);

            var nLbl = new Label(_target.blockName + "  [" + _target.screenSize + "]");
            nLbl.style.color = new Color(0.92f, 0.94f, 0.97f); nLbl.style.fontSize = 12;
            nLbl.style.unityFontStyleAndWeight = FontStyle.Bold; nLbl.style.flexGrow = 1;
            info.Add(nLbl);

            string pwr = _target.IsPowered ? "POWERED" : "OFFLINE";
            Color pc = _target.IsPowered ? new Color(0.35f, 0.80f, 0.45f) : new Color(0.80f, 0.20f, 0.20f);
            var pLbl = new Label(pwr);
            pLbl.style.color = pc; pLbl.style.fontSize = 10;
            pLbl.style.unityFontStyleAndWeight = FontStyle.Bold; pLbl.style.letterSpacing = 1;
            info.Add(pLbl);

            // ── Data sources ──
            var sHdr = new Label("DATA SOURCES  (click to toggle on/off)");
            sHdr.style.color = new Color(0.40f, 0.44f, 0.52f); sHdr.style.fontSize = 9;
            sHdr.style.letterSpacing = 2; sHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            sHdr.style.marginBottom = 3;
            _panel.Add(sHdr);

            var sources = _target.GetAvailableSources();

            if (sources.Count == 0)
            {
                var noSrc = new Label("No data sources on this grid. Place batteries, cargo, gas tanks, or cameras first.");
                noSrc.style.color = new Color(0.60f, 0.64f, 0.72f);
                noSrc.style.fontSize = 11; noSrc.style.whiteSpace = WhiteSpace.Normal;
                _panel.Add(noSrc);
            }
            else
            {
                var srcScroll = new ScrollView(ScrollViewMode.Vertical);
                srcScroll.style.maxHeight = 130;
                _panel.Add(srcScroll);

                foreach (var (pos, provider) in sources)
                {
                    var idx = pos;
                    int instId = (provider as GridBlock)?.GetInstanceID() ?? 0;
                    bool isOn = _target.HasSource(pos);

                    bool isCameraSource = provider is IGridCameraFeedProvider;
                    var btn = MakeToggleButton(isOn, provider.SourceName, provider.DataCategory, () =>
                    {
                        if (isCameraSource)
                        {
                            _target.SetPrimarySource(idx, instId);
                            _target.dataMode = ScreenDataMode.Camera;
                            RefreshModeHighlights();
                        }
                        else
                        {
                            _target.ToggleSource(idx, instId);
                        }
                        RefreshHighlights();
                        if (_sourceCount != null)
                            _sourceCount.text = _target.SourceCount + " source(s)";
                    });
                    srcScroll.Add(btn);
                    _sourceBtns.Add(btn);
                }

                var clearBtn = new Button(() =>
                {
                    _target.ClearSources();
                    RefreshHighlights();
                    if (_sourceCount != null)
                        _sourceCount.text = _target.SourceCount + " source(s)";
                })
                { text = "  Clear All" };
                clearBtn.style.minHeight = 22; clearBtn.style.marginTop = 2;
                clearBtn.style.fontSize = 9; clearBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                clearBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.10f, 0.10f));
                clearBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                UITheme.Radius(clearBtn, 4);
                clearBtn.style.borderLeftWidth = clearBtn.style.borderRightWidth =
                clearBtn.style.borderTopWidth = clearBtn.style.borderBottomWidth = 0;
                srcScroll.Add(clearBtn);
            }

            // ── Display mode ──
            var mHdr = new Label("DISPLAY MODE");
            mHdr.style.color = new Color(0.40f, 0.44f, 0.52f); mHdr.style.fontSize = 9;
            mHdr.style.letterSpacing = 2; mHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            mHdr.style.marginTop = 8; mHdr.style.marginBottom = 3;
            _panel.Add(mHdr);

            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row; modeRow.style.flexWrap = Wrap.Wrap;
            _panel.Add(modeRow);

            foreach (ScreenDataMode mode in System.Enum.GetValues(typeof(ScreenDataMode)))
            {
                var captured = mode;
                var mBtn = new Button(() =>
                {
                    _target.dataMode = captured;
                    RefreshModeHighlights();
                    RefreshCustomTextField();
                }) { text = captured == ScreenDataMode.Summary ? "Mixed" : mode.ToString() };
                mBtn.style.minHeight = 22; mBtn.style.marginRight = 4; mBtn.style.marginBottom = 4;
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

            // ── Text color picker ──
            var cHdr = new Label("TEXT COLOR");
            cHdr.style.color = new Color(0.40f, 0.44f, 0.52f); cHdr.style.fontSize = 9;
            cHdr.style.letterSpacing = 2; cHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            cHdr.style.marginTop = 6; cHdr.style.marginBottom = 3;
            _panel.Add(cHdr);

            var colorRow = new VisualElement();
            colorRow.style.flexDirection = FlexDirection.Row;
            colorRow.style.alignItems = Align.Center;
            _panel.Add(colorRow);

            // Pre-set color chips
            Color[] presetColors = {
                new Color(0.18f, 0.72f, 0.88f), // Cyan
                new Color(0.20f, 0.55f, 0.95f), // Blue
                new Color(0.22f, 0.78f, 0.42f), // Green
                new Color(0.95f, 0.65f, 0.20f), // Amber
                new Color(0.92f, 0.45f, 0.12f), // Orange
                new Color(0.95f, 0.18f, 0.14f), // Red
                new Color(0.72f, 0.42f, 0.95f), // Purple
                Color.white,                     // White
            };

            foreach (var c in presetColors)
            {
                var chip = new Button(() =>
                {
                    _target.textColor = c;
                    RefreshAll();
                }) { text = "" };
                chip.style.width = 22; chip.style.height = 22;
                chip.style.marginRight = 3; chip.style.marginBottom = 3;
                chip.style.backgroundColor = new StyleColor(c);
                chip.style.borderLeftWidth = chip.style.borderRightWidth =
                chip.style.borderTopWidth = chip.style.borderBottomWidth = 1;
                chip.style.borderLeftColor = chip.style.borderRightColor =
                chip.style.borderTopColor = chip.style.borderBottomColor = new StyleColor(new Color(0.30f, 0.33f, 0.38f));
                UITheme.Radius(chip, 4);
                colorRow.Add(chip);
            }

            // Border style
            var bHdr = new Label("BORDER");
            bHdr.style.color = new Color(0.40f, 0.44f, 0.52f); bHdr.style.fontSize = 9;
            bHdr.style.letterSpacing = 2; bHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            bHdr.style.marginTop = 6; bHdr.style.marginBottom = 3;
            _panel.Add(bHdr);

            var borderRow = new VisualElement();
            borderRow.style.flexDirection = FlexDirection.Row;
            _panel.Add(borderRow);

            string[] borderNames = { "None", "Thin", "Thick", "Glow" };
            for (int bi = 0; bi < borderNames.Length; bi++)
            {
                int bv = bi;
                var bBtn = new Button(() => { _target.borderStyle = bv; RefreshAppearanceHighlights(); RefreshAll(); }) { text = borderNames[bi] };
                bBtn.style.minHeight = 22; bBtn.style.marginRight = 4; bBtn.style.marginBottom = 4;
                bBtn.style.fontSize = 10; bBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                bBtn.style.paddingLeft = 8; bBtn.style.paddingRight = 8;
                bBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                bBtn.style.backgroundColor = new StyleColor(bv == _target.borderStyle ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
                UITheme.Radius(bBtn, 4);
                bBtn.style.borderLeftWidth = bBtn.style.borderRightWidth = bBtn.style.borderTopWidth = bBtn.style.borderBottomWidth = 0;
                borderRow.Add(bBtn);
                _borderBtns.Add(bBtn);
            }

            // Font style
            var fHdr = new Label("FONT");
            fHdr.style.color = new Color(0.40f, 0.44f, 0.52f); fHdr.style.fontSize = 9;
            fHdr.style.letterSpacing = 2; fHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            fHdr.style.marginTop = 4; fHdr.style.marginBottom = 3;
            _panel.Add(fHdr);

            var fontRow = new VisualElement();
            fontRow.style.flexDirection = FlexDirection.Row;
            _panel.Add(fontRow);

            string[] fontNames = { "Default", "Mono", "LCD", "Terminal" };
            for (int fi = 0; fi < fontNames.Length; fi++)
            {
                int fv = fi;
                var fBtn = new Button(() => { _target.fontStyle = fv; RefreshAppearanceHighlights(); RefreshAll(); }) { text = fontNames[fi] };
                fBtn.style.minHeight = 22; fBtn.style.marginRight = 4; fBtn.style.marginBottom = 4;
                fBtn.style.fontSize = 10; fBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                fBtn.style.paddingLeft = 8; fBtn.style.paddingRight = 8;
                fBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                fBtn.style.backgroundColor = new StyleColor(fv == _target.fontStyle ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
                UITheme.Radius(fBtn, 4);
                fBtn.style.borderLeftWidth = fBtn.style.borderRightWidth = fBtn.style.borderTopWidth = fBtn.style.borderBottomWidth = 0;
                fontRow.Add(fBtn);
                _fontBtns.Add(fBtn);
            }

            // ── Custom text ──
            RefreshCustomTextField();

            // ── Preview ──
            var pHdr = new Label("LIVE PREVIEW");
            pHdr.style.color = new Color(0.40f, 0.44f, 0.52f); pHdr.style.fontSize = 9;
            pHdr.style.letterSpacing = 2; pHdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            pHdr.style.marginTop = 8; pHdr.style.marginBottom = 3;
            _panel.Add(pHdr);

            var pBox = new VisualElement();
            pBox.style.backgroundColor = new StyleColor(new Color(0.025f, 0.03f, 0.045f));
            pBox.style.paddingTop = 6; pBox.style.paddingBottom = 6;
            pBox.style.paddingLeft = 8; pBox.style.paddingRight = 8;
            pBox.style.borderLeftWidth = pBox.style.borderRightWidth =
            pBox.style.borderTopWidth = pBox.style.borderBottomWidth = 1;
            pBox.style.borderLeftColor = pBox.style.borderRightColor =
            pBox.style.borderTopColor = pBox.style.borderBottomColor = new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.30f));
            UITheme.Radius(pBox, 4); pBox.style.minHeight = 30;
            _panel.Add(pBox);

            _previewText = new Label(_target.FormattedDisplay);
            _previewText.style.color = new StyleColor(_target.textColor);
            _previewText.style.fontSize = 11; _previewText.style.whiteSpace = WhiteSpace.Normal;
            pBox.Add(_previewText);

            RefreshAll();
        }

        private Button MakeToggleButton(bool isOn, string sourceName, string category, System.Action onClick)
        {
            var btn = new Button(onClick) { text = "" };
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 6;

            var check = new Label(isOn ? "\u2611" : "\u2610");
            check.name = "ToggleCheck";
            check.style.color = isOn ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.50f, 0.55f, 0.60f);
            check.style.fontSize = 14; check.style.marginRight = 6;
            check.style.minWidth = 18;
            row.Add(check);

            var nameL = new Label(sourceName);
            nameL.name = "SourceName";
            nameL.style.color = new Color(0.92f, 0.94f, 0.97f);
            nameL.style.fontSize = 11; nameL.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameL.style.flexGrow = 1;
            row.Add(nameL);

            var catL = new Label("[" + category + "]");
            catL.style.color = new Color(0.50f, 0.55f, 0.65f);
            catL.style.fontSize = 9; catL.style.marginRight = 4;
            row.Add(catL);

            btn.Add(row);

            btn.style.minHeight = 26; btn.style.marginBottom = 2;
            btn.style.unityTextAlign = TextAnchor.MiddleLeft;
            btn.style.backgroundColor = new StyleColor(isOn ? new Color(0.12f, 0.18f, 0.28f) : new Color(0.10f, 0.11f, 0.14f));
            UITheme.Radius(btn, 4);
            btn.style.borderLeftWidth = isOn ? 3f : 0f;
            if (isOn) btn.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.55f, 0.95f));
            btn.style.borderRightWidth = btn.style.borderTopWidth = btn.style.borderBottomWidth = 0;
            return btn;
        }

        private void RefreshHighlights()
        {
            if (_target == null) return;

            // Rebuild source buttons with current state
            foreach (var btn in _sourceBtns)
            {
                var check = btn.Q("ToggleCheck") as Label;
                var nameL = btn.Q("SourceName") as Label;
                if (check == null || nameL == null) continue;

                bool nowOn = false;
                var sources = _target.GetAvailableSources();
                foreach (var (pos, p) in sources)
                {
                    if (nameL.text == p.SourceName)
                    {
                        nowOn = _target.HasSource(pos);
                        break;
                    }
                }

                check.text = nowOn ? "\u2611" : "\u2610";
                check.style.color = nowOn ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.50f, 0.55f, 0.60f);
                btn.style.backgroundColor = new StyleColor(nowOn ? new Color(0.12f, 0.18f, 0.28f) : new Color(0.10f, 0.11f, 0.14f));
                btn.style.borderLeftWidth = nowOn ? 3f : 0f;
                if (nowOn) btn.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.55f, 0.95f));
            }

            if (_sourceCount != null) _sourceCount.text = _target.SourceCount + " source(s)";
            if (_previewText != null)
            {
                _previewText.text = _target.FormattedDisplay;
                _previewText.style.color = new StyleColor(_target.textColor);
            }
        }

        private void RefreshAll()
        {
            RefreshHighlights();
            RefreshModeHighlights();
            RefreshAppearanceHighlights();
        }

        private void RefreshModeHighlights()
        {
            foreach (var btn in _modeBtns)
            {
                string modeName = btn.text == "Mixed" ? "Summary" : btn.text;
                bool active = modeName == _target.dataMode.ToString();
                btn.style.backgroundColor = new StyleColor(active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }
            if (_previewText != null)
            {
                _previewText.text = _target.FormattedDisplay;
                _previewText.style.color = new StyleColor(_target.textColor);
            }
        }

        private void RefreshAppearanceHighlights()
        {
            if (_target == null) return;

            string[] borderNames = { "None", "Thin", "Thick", "Glow" };
            for (int i = 0; i < _borderBtns.Count; i++)
            {
                bool active = i < borderNames.Length && i == Mathf.Clamp(_target.borderStyle, 0, 3);
                _borderBtns[i].style.backgroundColor = new StyleColor(active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }

            string[] fontNames = { "Default", "Mono", "LCD", "Terminal" };
            for (int i = 0; i < _fontBtns.Count; i++)
            {
                bool active = i < fontNames.Length && i == Mathf.Clamp(_target.fontStyle, 0, 3);
                _fontBtns[i].style.backgroundColor = new StyleColor(active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }
        }

        private void RefreshCustomTextField()
        {
            if (_panel == null || _target == null) return;
            var old = _panel.Q("CustomTextSection");
            if (old != null) _panel.Remove(old);

            if (_target.dataMode != ScreenDataMode.Custom) return;

            var sec = new VisualElement { name = "CustomTextSection" };
            var h = new Label("CUSTOM TEXT");
            h.style.color = new Color(0.40f, 0.44f, 0.52f); h.style.fontSize = 9;
            h.style.letterSpacing = 2; h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.marginTop = 6; h.style.marginBottom = 3;
            sec.Add(h);

            var f = new TextField();
            f.value = _target.customText; f.multiline = true;
            f.style.minHeight = 40;
            f.style.backgroundColor = new StyleColor(new Color(0.025f, 0.030f, 0.045f));
            f.style.color = new Color(0.92f, 0.94f, 0.97f);
            f.style.borderLeftWidth = f.style.borderRightWidth =
            f.style.borderTopWidth = f.style.borderBottomWidth = 1;
            f.style.borderLeftColor = f.style.borderRightColor =
            f.style.borderTopColor = f.style.borderBottomColor = new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.35f));
            f.style.whiteSpace = WhiteSpace.Normal;
            f.RegisterValueChangedCallback(e => { _target.customText = e.newValue; });
            f.RegisterCallback<GeometryChangedEvent>(_ => StyleTextInput(f));
            sec.Add(f);
            f.schedule.Execute(() => StyleTextInput(f));

            for (int i = 0; i < _panel.childCount; i++)
            {
                if (_panel[i] is Label l && l.text == "LIVE PREVIEW")
                { _panel.Insert(i, sec); return; }
            }
            _panel.Add(sec);
        }

        private static void StyleTextInput(TextField field)
        {
            if (field == null) return;
            Color inputBackground = new Color(0.025f, 0.030f, 0.045f, 1f);
            Color inputText = new Color(0.92f, 0.94f, 0.97f, 1f);
            StyleTextInputRecursive(field, inputBackground, inputText);
        }

        private static void StyleTextInputRecursive(VisualElement element, Color background, Color text)
        {
            if (element == null) return;
            element.style.backgroundColor = new StyleColor(background);
            element.style.color = new StyleColor(text);

            foreach (var child in element.Children())
                StyleTextInputRecursive(child, background, text);
        }
    }
}
