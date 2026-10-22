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
        private static GridScreenConfigUI _instance;
        public static GridScreenConfigUI Instance
        {
            get
            {
                EnsureInstance();
                return _instance;
            }
            private set => _instance = value;
        }

        /// <summary>
        /// 17.42.0-dev — ESC belongs to whoever is open. While the screen editor is up,
        /// Escape must close it before it reaches the pause menu, so the menu asks this
        /// before reacting; script order stops mattering.
        /// </summary>
        public static bool IsOpen => _instance != null && _instance._open;

        private UIDocument _doc;
        private VisualElement _root, _panel;
        private GridScreenBlock _target;
        private Label _previewText, _sourceCount;
        // 17.35.1-dev — the rebuilt editor's live preview.
        private VisualElement _previewBox;
        private Label _previewTitle, _previewStatus;
        private Label _pageInfoTitle, _pageInfoBlurb, _pageInfoHint;
        private VisualElement _pageOptionsSlot;
        private TextField _focusedTextField;
        private string _lastPreviewText;
        private float _lastPreviewWidth = -1f, _lastPreviewHeight = -1f;
        private bool _open;
        private readonly List<Button> _sourceBtns = new();
        /// <summary>The data-source rows and their searchable text, kept together so the
        /// search field can hide the ones that do not match.</summary>
        private readonly List<Button> _sourceButtons = new();
        private readonly List<string> _sourceLabels = new();
        private string _sourceQuery = string.Empty;
        // 17.36.0-dev: the address AND the instance id of the block each button links.
        // Addresses collide (a chest bolted to a wall reports the wall's cell), which is
        // why ticking one container used to tick every container on the construct.
        private readonly List<Vector3Int> _sourcePositions = new();
        private readonly List<EntityId> _sourceIds = new();
        private readonly List<Button> _modeBtns = new();
        // 17.35.0-dev: the page each mode button selects, so the highlight never has to
        // guess a mode from a button's text again (the old "Mixed" special case).
        private readonly List<ScreenDataMode> _modeBtnModes = new();
        private readonly List<Button> _borderBtns = new();
        private readonly List<Button> _fontBtns = new();

        // 17.36.0-dev: layout and per-pane pages. A split panel is edited here, not on the
        // wall: four dropdowns over one glass would be four keypresses in Build mode.
        private readonly List<Button> _layoutBtns = new();
        private readonly List<StyledDropdownField> _paneFields = new();
        private readonly List<ScreenDataMode> _drawablePages = new();
        private readonly List<Button> _paneLabels = new();
        private VisualElement _paneSection;
        // Which pane the page list writes into while the screen is split. Set by clicking
        // a pane's own label; 1 for a single page, where there is nothing to choose.
        private int _activePane;

        // ── Auto-create if missing from scene ─────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
            if (_instance != null) return;

            var existing = Object.FindAnyObjectByType<GridScreenConfigUI>(FindObjectsInactive.Include);
            if (existing != null)
            {
                _instance = existing;
                return;
            }

            var go = new GameObject("GridScreenConfigUI");
            go.AddComponent<UnityEngine.UIElements.UIDocument>();
            go.AddComponent<GridScreenConfigUI>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                // Prefer the scene/player-authored object over a runtime-generated root singleton.
                // Destroying only the component made Unity remove the script from the configured
                // object, so duplicates are handled at GameObject level instead.
                bool thisLooksSceneAuthored = transform.parent != null;
                bool existingLooksRuntimeGenerated = _instance.transform.parent == null;
                if (thisLooksSceneAuthored && existingLooksRuntimeGenerated)
                {
                    Destroy(_instance.gameObject);
                    _instance = this;
                }
                else
                {
                    Destroy(gameObject);
                    return;
                }
            }
            else
            {
                _instance = this;
            }

            _doc = GetComponent<UIDocument>();
            if (_doc == null) _doc = gameObject.AddComponent<UIDocument>();
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
            if (GameSettings.WasPressed(InputAction.Pause))
            {
                UIState.PauseConsumedFrame = Time.frameCount;
                if (_focusedTextField != null)
                {
                    ReleaseTextFieldFocus();
                    return;
                }
                Close();
                return;
            }
            // The preview costs a FormattedDisplay call per frame; the call itself is cached
            // by the block, and this early-outs unless the characters actually changed.
            RefreshPreview();
        }

        public void Open(GridScreenBlock screen)
        {
            if (screen == null) return;
            if (_open) Close();
            ReleaseTextFieldFocus();
            _pageOptionsSlot = null;
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
            _sourceBtns.Clear(); _sourcePositions.Clear(); _sourceIds.Clear();
            _sourceButtons.Clear(); _sourceLabels.Clear();
            _modeBtns.Clear(); _modeBtnModes.Clear();
            _borderBtns.Clear(); _fontBtns.Clear();
            _layoutBtns.Clear(); _paneFields.Clear(); _paneLabels.Clear(); _drawablePages.Clear();
            _activePane = 0;
            Build();
        }

        public void Close()
        {
            if (!_open) return;
            ReleaseTextFieldFocus();
            _pageOptionsSlot = null;
            _root.style.display = DisplayStyle.None;
            _open = false; _target = null; UIState.PopBlock(); Hide();
        }

        private void Hide()
        {
            CloseOpenDropdown();
            _root.Clear(); _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
        }

        public void OpenForScreen(GridScreenBlock screen) { Open(screen); }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

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

            // 17.35.1-dev — THE EDITOR, REBUILT.
            //
            // It used to be a 540-pixel card in the middle of the screen with everything
            // stacked down one column: eleven pages in a wrap, then colours, then borders,
            // then fonts, then a two-line preview at the bottom. On any monitor made this
            // decade ninety percent of the window was empty black and the one thing a
            // player is actually configuring - what the screen LOOKS like - was a stamp.
            //
            // It is now a full-window editor in three columns: pages on the left, a live
            // preview of the actual panel in the middle at the panel's own proportions, and
            // sources plus appearance on the right. The preview is the point of the layout.
            _panel = new VisualElement { name = "ScreenConfigPanel" };
            _panel.style.width = new StyleLength(new Length(96f, LengthUnit.Percent));
            _panel.style.height = new StyleLength(new Length(92f, LengthUnit.Percent));
            _panel.style.maxWidth = 1600;
            _panel.style.maxHeight = 1000;
            _panel.style.backgroundColor = new StyleColor(UITheme.BgPanel);
            _panel.style.paddingTop = 16; _panel.style.paddingBottom = 16;
            _panel.style.paddingLeft = 18; _panel.style.paddingRight = 18;
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth =
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 1;
            _panel.style.borderTopColor = _panel.style.borderBottomColor =
            _panel.style.borderLeftColor = _panel.style.borderRightColor = new StyleColor(UITheme.BorderDim);
            UITheme.Radius(_panel, 12);
            _root.Add(_panel);

            // ── Header ──────────────────────────────────────────────────────────
            var hdr = new VisualElement();
            hdr.style.flexDirection = FlexDirection.Row;
            hdr.style.alignItems = Align.Center;
            hdr.style.marginBottom = 10;
            _panel.Add(hdr);

            var title = new Label("SCREEN CONFIG");
            title.style.color = new Color(0.92f, 0.94f, 0.97f);
            title.style.fontSize = 17;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 3;
            hdr.Add(title);

            var subtitle = new Label(_target.blockName + "   \u00b7   "
                + ScreenPageCatalog.SizeLabel(_target.screenSize).ToUpperInvariant()
                + "   \u00b7   " + (_target.IsPowered ? "POWERED" : "OFFLINE"));
            subtitle.style.color = new Color(0.45f, 0.50f, 0.58f);
            subtitle.style.fontSize = 10;
            subtitle.style.letterSpacing = 1;
            subtitle.style.marginLeft = 12;
            subtitle.style.flexGrow = 1;
            hdr.Add(subtitle);

            _sourceCount = new Label(_target.SourceCount + " source(s)");
            _sourceCount.style.color = new Color(0.45f, 0.50f, 0.58f);
            _sourceCount.style.fontSize = 10;
            _sourceCount.style.marginRight = 10;
            hdr.Add(_sourceCount);

            var closeBtn = new Button(Close) { text = "X" };
            closeBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
            closeBtn.style.fontSize = 14;
            closeBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
            closeBtn.style.backgroundColor = new StyleColor(new Color(0.45f, 0.18f, 0.18f));
            closeBtn.style.minWidth = 28; closeBtn.style.minHeight = 28;
            UITheme.Radius(closeBtn, 4);
            hdr.Add(closeBtn);

            _panel.Add(HighTechTheme.ScanDivider(UITheme.AccentCyan));

            // ── Body: pages | preview | sources + appearance ─────────────────────
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            body.style.marginTop = 10;
            _panel.Add(body);

            var left = new VisualElement();
            left.style.width = 300;
            left.style.marginRight = 16;
            body.Add(left);

            var center = new VisualElement();
            center.style.flexGrow = 1;
            center.style.marginRight = 16;
            body.Add(center);

            // The right column is the tallest one (sources, colour, border, font, custom
            // text), so it scrolls inside itself rather than clipping on a short window.
            var rightScroll = new ScrollView(ScrollViewMode.Vertical);
            rightScroll.style.width = 320;
            rightScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(rightScroll);

            var right = new VisualElement();
            rightScroll.Add(right);

            // ── Left: the page list ─────────────────────────────────────────────
            left.Add(SectionHeader("PAGES"));

            var search = new TextField { name = "ScreenPageSearch" };
            search.style.height = 26;
            search.style.marginBottom = 6;
            search.label = string.Empty;
            search.tooltip = "Filter the pages by name or by what they show.";
            TrackTextField(search);
            left.Add(search);

            var pageList = new ScrollView(ScrollViewMode.Vertical) { name = "ScreenPageList" };
            pageList.style.flexGrow = 1;
            pageList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            left.Add(pageList);

            // ── Centre: the live preview ────────────────────────────────────────
            center.Add(SectionHeader("LIVE PREVIEW"));

            var previewWrap = new VisualElement();
            previewWrap.style.flexGrow = 1;
            previewWrap.style.alignItems = Align.Center;
            previewWrap.style.justifyContent = Justify.Center;
            previewWrap.style.minHeight = 240;
            center.Add(previewWrap);

            _previewBox = new VisualElement { name = "ScreenPreviewBox" };
            _previewBox.style.backgroundColor = new StyleColor(new Color(0.015f, 0.018f, 0.028f));
            _previewBox.style.paddingTop = 10; _previewBox.style.paddingBottom = 10;
            _previewBox.style.paddingLeft = 12; _previewBox.style.paddingRight = 12;
            _previewBox.style.borderLeftWidth = _previewBox.style.borderRightWidth =
            _previewBox.style.borderTopWidth = _previewBox.style.borderBottomWidth = 1;
            UITheme.Radius(_previewBox, 6);
            previewWrap.Add(_previewBox);

            _previewTitle = new Label(string.Empty);
            _previewTitle.style.fontSize = 9;
            _previewTitle.style.letterSpacing = 2;
            _previewTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _previewTitle.style.color = new Color(0.50f, 0.55f, 0.62f);
            _previewTitle.style.position = Position.Absolute;
            _previewTitle.style.top = 6; _previewTitle.style.left = 0; _previewTitle.style.right = 0;
            _previewTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _previewBox.Add(_previewTitle);

            _previewText = new Label(string.Empty);
            _previewText.style.flexGrow = 1;
            // A page is a character grid, and the preview has to be honest about it: the
            // same monospace face the wall draws in, no wrapping (a wrapped block glyph
            // turns the whole panel into white bars), and no letter tracking (tracking
            // slides each row off its own columns). This is the difference between a
            // preview and a smear.
            var previewFace = GridScreenBlock.DisplayFont;
            if (previewFace != null)
                _previewText.style.unityFontDefinition = new StyleFontDefinition(previewFace);
            _previewText.style.whiteSpace = WhiteSpace.NoWrap;
            _previewText.style.overflow = Overflow.Hidden;
            _previewText.style.flexShrink = 0;
            // Left-aligned, exactly like the glass: the preview has to be honest about
            // where each line starts.
            // The wall centres the printed block on the glass; so does the preview, or the
            // two disagree about where the page sits.
            _previewText.style.unityTextAlign = TextAnchor.MiddleLeft;
            _previewText.style.marginTop = 6; _previewText.style.marginBottom = 6;
            _previewText.style.marginLeft = 0; _previewText.style.marginRight = 0;
            _previewBox.Add(_previewText);

            _previewStatus = new Label("LIVE");
            _previewStatus.style.fontSize = 9;
            _previewStatus.style.letterSpacing = 1;
            _previewStatus.style.position = Position.Absolute;
            _previewStatus.style.bottom = 6; _previewStatus.style.right = 10;
            _previewBox.Add(_previewStatus);

            // The preview keeps the panel's own shape, whatever room the window has.
            previewWrap.RegisterCallback<GeometryChangedEvent>(_ => LayoutPreview(previewWrap));

            // The description of the highlighted page, under the preview.
            var pageInfo = new VisualElement { name = "ScreenPageInfo" };
            pageInfo.style.paddingLeft = 10; pageInfo.style.paddingRight = 10;
            pageInfo.style.paddingTop = 8; pageInfo.style.paddingBottom = 8;
            pageInfo.style.marginTop = 10;
            pageInfo.style.backgroundColor = new StyleColor(UITheme.BgCard);
            UITheme.Radius(pageInfo, 6);
            center.Add(pageInfo);

            _pageInfoTitle = new Label(string.Empty);
            _pageInfoTitle.style.fontSize = 12;
            _pageInfoTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _pageInfoTitle.style.color = new Color(0.92f, 0.94f, 0.97f);
            _pageInfoTitle.style.letterSpacing = 1;
            pageInfo.Add(_pageInfoTitle);

            _pageInfoBlurb = new Label(string.Empty);
            _pageInfoBlurb.style.fontSize = 10;
            _pageInfoBlurb.style.whiteSpace = WhiteSpace.Normal;
            _pageInfoBlurb.style.color = new Color(0.66f, 0.70f, 0.76f);
            _pageInfoBlurb.style.marginTop = 3;
            pageInfo.Add(_pageInfoBlurb);

            _pageInfoHint = new Label(string.Empty);
            _pageInfoHint.style.fontSize = 10;
            _pageInfoHint.style.whiteSpace = WhiteSpace.Normal;
            _pageInfoHint.style.marginTop = 5;
            _pageInfoHint.style.color = new Color(0.96f, 0.74f, 0.36f);
            pageInfo.Add(_pageInfoHint);

            // ── Right: layout, sources, appearance, custom text ───────────────
            right.Add(SectionHeader("LAYOUT", 12));

            string[] layoutNames = { "Single", "Two across", "Two tall", "Quarters" };
            var layoutRow = new VisualElement();
            layoutRow.style.flexDirection = FlexDirection.Row;
            layoutRow.style.flexWrap = Wrap.Wrap;
            layoutRow.style.marginBottom = 4;
            right.Add(layoutRow);

            for (int li = 0; li < layoutNames.Length; li++)
            {
                int chosenLayout = li;
                var layoutBtn = new Button(() =>
                {
                    _target.splitMode = chosenLayout;
                    RefreshAppearanceHighlights();
                    RefreshAll();
                    RefreshPageInfo();
                })
                {
                    text = layoutNames[li],
                    tooltip = chosenLayout == 0
                        ? "One page on the whole glass."
                        : chosenLayout == 1
                            ? "Two pages side by side, split down the middle."
                            : chosenLayout == 2
                                ? "Two pages stacked, split across the middle."
                                : "Four pages in equal quarters."
                };
                layoutBtn.style.minHeight = 24;
                layoutBtn.style.marginRight = 4;
                layoutBtn.style.marginBottom = 4;
                layoutBtn.style.fontSize = 10;
                layoutBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                layoutBtn.style.paddingLeft = 10; layoutBtn.style.paddingRight = 10;
                layoutBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                UITheme.Radius(layoutBtn, 4);
                layoutBtn.style.borderLeftWidth = layoutBtn.style.borderRightWidth =
                layoutBtn.style.borderTopWidth = layoutBtn.style.borderBottomWidth = 0;
                layoutRow.Add(layoutBtn);
                _layoutBtns.Add(layoutBtn);
            }

            // Pane pickers, built once and shown only while a layout is not Single.
            _paneSection = new VisualElement { name = "ScreenPanePickers" };
            right.Add(_paneSection);

            BuildDrawablePageList();
            for (int pi = 0; pi < 4; pi++) BuildPanePicker(pi);

            var paneHint = new Label("Click a page on the left to put it in the highlighted pane.");
            paneHint.name = "ScreenPaneHint";
            paneHint.style.fontSize = 9;
            paneHint.style.whiteSpace = WhiteSpace.Normal;
            paneHint.style.color = new Color(0.52f, 0.57f, 0.65f);
            paneHint.style.marginBottom = 6;
            _paneSection.Add(paneHint);

            right.Add(SectionHeader("DATA SOURCES"));

            var sources = _target.GetAvailableSources();
            if (sources.Count == 0)
            {
                var noSrc = new Label("Nothing on this grid reports data yet. Place a battery, cargo container, gas tank or camera first.");
                noSrc.style.color = new Color(0.60f, 0.64f, 0.72f);
                noSrc.style.fontSize = 10;
                noSrc.style.whiteSpace = WhiteSpace.Normal;
                noSrc.style.marginBottom = 8;
                right.Add(noSrc);
            }
            else
            {
                // SEARCH. A base with forty machines has forty rows under this heading, and
                // the one the player wants is never the one at the top - so the list filters
                // as they type, on the source's name and on its category.
                var sourceSearch = new TextField();
                sourceSearch.name = "ScreenSourceSearch";
                sourceSearch.SetValueWithoutNotify(_sourceQuery ?? string.Empty);
                sourceSearch.style.marginBottom = 6;
                StyleTextInput(sourceSearch);
                TrackTextField(sourceSearch);
                right.Add(sourceSearch);

                _sourceButtons.Clear();
                _sourceLabels.Clear();
                var srcScroll = new ScrollView(ScrollViewMode.Vertical);
                srcScroll.style.maxHeight = 180;
                srcScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                right.Add(srcScroll);

                foreach (var (pos, provider) in sources)
                {
                    var idx = pos;
                    EntityId instId = (provider as GridBlock)?.GetEntityId() ?? EntityId.None;
                    bool isOn = _target.HasSource(pos, instId);
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
                        RefreshPageInfo();
                    });
                    srcScroll.Add(btn);
                    _sourceBtns.Add(btn);
                    _sourcePositions.Add(idx);
                    _sourceIds.Add(instId);
                    _sourceButtons.Add(btn);
                    _sourceLabels.Add((provider.SourceName ?? string.Empty)
                        + " " + (provider.DataCategory ?? string.Empty));
                }

                sourceSearch.RegisterValueChangedCallback(evt =>
                {
                    _sourceQuery = evt.newValue;
                    ApplySourceFilter();
                });
                sourceSearch.schedule.Execute(() => StyleTextInput(sourceSearch));
                ApplySourceFilter();

                var clearBtn = new Button(() =>
                {
                    _target.ClearSources();
                    RefreshHighlights();
                    RefreshPageInfo();
                })
                { text = "Clear All" };
                clearBtn.style.minHeight = 24; clearBtn.style.marginTop = 4;
                clearBtn.style.fontSize = 10; clearBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                clearBtn.style.backgroundColor = new StyleColor(new Color(0.15f, 0.10f, 0.10f));
                clearBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                UITheme.Radius(clearBtn, 4);
                clearBtn.style.borderLeftWidth = clearBtn.style.borderRightWidth =
                clearBtn.style.borderTopWidth = clearBtn.style.borderBottomWidth = 0;
                right.Add(clearBtn);
            }

            _pageOptionsSlot = new VisualElement { name = "ScreenPageOptions" };
            right.Add(_pageOptionsSlot);

            // Text colour
            right.Add(SectionHeader("TEXT COLOR", 12));

            var colorRow = new VisualElement();
            colorRow.style.flexDirection = FlexDirection.Row;
            colorRow.style.flexWrap = Wrap.Wrap;
            right.Add(colorRow);

            Color[] presetColors = {
                new Color(0.18f, 0.72f, 0.88f), new Color(0.20f, 0.55f, 0.95f),
                new Color(0.22f, 0.78f, 0.42f), new Color(0.95f, 0.65f, 0.20f),
                new Color(0.92f, 0.45f, 0.12f), new Color(0.95f, 0.18f, 0.14f),
                new Color(0.72f, 0.42f, 0.95f), Color.white,
            };
            foreach (var preset in presetColors)
            {
                var captured = preset;
                var chip = new Button(() =>
                {
                    _target.textColor = captured;
                    RefreshAppearanceHighlights();
                    RefreshPreview(true);
                }) { text = string.Empty };
                chip.style.width = 26; chip.style.height = 26;
                chip.style.marginRight = 4; chip.style.marginBottom = 4;
                chip.style.backgroundColor = new StyleColor(captured);
                chip.style.borderLeftWidth = chip.style.borderRightWidth =
                chip.style.borderTopWidth = chip.style.borderBottomWidth = 2;
                chip.style.borderLeftColor = chip.style.borderRightColor =
                chip.style.borderTopColor = chip.style.borderBottomColor = new StyleColor(new Color(0.30f, 0.33f, 0.38f));
                UITheme.Radius(chip, 4);
                colorRow.Add(chip);
            }

            // Border
            right.Add(SectionHeader("BORDER", 12));
            var borderRow = new VisualElement();
            borderRow.style.flexDirection = FlexDirection.Row;
            borderRow.style.flexWrap = Wrap.Wrap;
            right.Add(borderRow);

            string[] borderNames = { "None", "Thin", "Thick", "Glow" };
            for (int bi = 0; bi < borderNames.Length; bi++)
            {
                int bv = bi;
                var bBtn = new Button(() =>
                {
                    _target.borderStyle = bv;
                    RefreshAppearanceHighlights();
                    RefreshAll();
                }) { text = borderNames[bi] };
                bBtn.style.minHeight = 24; bBtn.style.marginRight = 4; bBtn.style.marginBottom = 4;
                bBtn.style.fontSize = 10; bBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                bBtn.style.paddingLeft = 10; bBtn.style.paddingRight = 10;
                bBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                bBtn.style.backgroundColor = new StyleColor(bv == _target.borderStyle
                    ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
                UITheme.Radius(bBtn, 4);
                bBtn.style.borderLeftWidth = bBtn.style.borderRightWidth =
                bBtn.style.borderTopWidth = bBtn.style.borderBottomWidth = 0;
                borderRow.Add(bBtn);
                _borderBtns.Add(bBtn);
            }

            // Font
            right.Add(SectionHeader("FONT", 12));
            var fontRow = new VisualElement();
            fontRow.style.flexDirection = FlexDirection.Row;
            fontRow.style.flexWrap = Wrap.Wrap;
            right.Add(fontRow);

            string[] fontNames = { "Default", "Bold", "Wide", "Compact" };
            for (int fi = 0; fi < fontNames.Length; fi++)
            {
                int fv = fi;
                var fBtn = new Button(() =>
                {
                    _target.fontStyle = fv;
                    RefreshAppearanceHighlights();
                    RefreshAll();
                }) { text = fontNames[fi] };
                fBtn.style.minHeight = 24; fBtn.style.marginRight = 4; fBtn.style.marginBottom = 4;
                fBtn.style.fontSize = 10; fBtn.style.unityFontStyleAndWeight = FontStyle.Bold;
                fBtn.style.paddingLeft = 10; fBtn.style.paddingRight = 10;
                fBtn.style.color = new Color(0.92f, 0.94f, 0.97f);
                fBtn.style.backgroundColor = new StyleColor(fv == _target.fontStyle
                    ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
                UITheme.Radius(fBtn, 4);
                fBtn.style.borderLeftWidth = fBtn.style.borderRightWidth =
                fBtn.style.borderTopWidth = fBtn.style.borderBottomWidth = 0;
                fontRow.Add(fBtn);
                _fontBtns.Add(fBtn);
            }

            // Custom text lives here now, and only when it is the page being edited.
            var customSlot = new VisualElement { name = "CustomTextSlot" };
            right.Add(customSlot);

            // ── Behaviour ───────────────────────────────────────────────────────
            void Select(ScreenDataMode mode)
            {
                _target.dataMode = mode;
                // 17.36.0-dev: while the glass is split, choosing a page means choosing it
                // FOR A PANE. Writing it into dataMode alone looked like the click did
                // nothing, because a split panel never reads dataMode.
                if (_target.splitMode > 0 && GridScreenBlock.DrawsItsOwnPage(mode))
                {
                    _target.SetPanePage(Mathf.Clamp(_activePane, 0, 3), mode);
                    RefreshPanePickers();
                }
                // Team Banner (14.37.0): the screen mirrors the chooser's OWN team banner.
                // The banner itself is edited in PAUSE > TEAMS - this is a display.
                if (mode == ScreenDataMode.TeamBanner)
                {
                    var myTeam = VoxelEngine.Networking.TeamRegistry.TeamOf(
                        VoxelEngine.Networking.NetworkSession.LocalPlayerId);
                    _target.bannerTeamId = myTeam != null ? myTeam.teamId : "";
                }
                RefreshModeHighlights();
                RefreshCustomTextField();
                RefreshPageInfo();
            }

            void RebuildPageList(string query)
            {
                pageList.Clear();
                _modeBtns.Clear();
                _modeBtnModes.Clear();

                foreach (var category in ScreenPageCatalog.Categories)
                {
                    var pages = ScreenPageCatalog.InCategory(category);
                    var matching = new List<ScreenPageInfo>();
                    for (int i = 0; i < pages.Count; i++)
                        if (pages[i].Matches(query)) matching.Add(pages[i]);
                    if (matching.Count == 0) continue;

                    var header = new Label(ScreenPageCatalog.CategoryLabel(category));
                    header.style.fontSize = 8;
                    header.style.letterSpacing = 2;
                    header.style.unityFontStyleAndWeight = FontStyle.Bold;
                    header.style.color = new Color(0.40f, 0.44f, 0.52f);
                    header.style.marginTop = 8;
                    header.style.marginBottom = 3;
                    header.style.marginLeft = 2;
                    pageList.Add(header);

                    for (int i = 0; i < matching.Count; i++)
                    {
                        var page = matching[i];
                        var captured = page.Mode;
                        var button = new Button(() => Select(captured))
                        {
                            text = page.Label,
                            tooltip = page.Blurb,
                        };
                        button.style.height = 26;
                        button.style.marginBottom = 2;
                        button.style.fontSize = 11;
                        button.style.unityFontStyleAndWeight = FontStyle.Bold;
                        button.style.unityTextAlign = TextAnchor.MiddleLeft;
                        button.style.paddingLeft = 10;
                        button.style.color = new Color(0.88f, 0.91f, 0.95f);
                        button.style.backgroundColor = new StyleColor(new Color(0.10f, 0.115f, 0.15f));
                        UITheme.Radius(button, 4);
                        button.style.borderLeftWidth = 3f;
                        button.style.borderLeftColor = new StyleColor(new Color(0f, 0f, 0f, 0f));
                        button.style.borderRightWidth = button.style.borderTopWidth =
                        button.style.borderBottomWidth = 0;
                        pageList.Add(button);
                        _modeBtns.Add(button);
                        _modeBtnModes.Add(page.Mode);
                    }
                }
                RefreshModeHighlights();
            }

            search.RegisterValueChangedCallback(evt => RebuildPageList(evt.newValue));
            RebuildPageList(string.Empty);
            RefreshCustomTextField();
            RefreshPageInfo();
            RefreshPreview(true);
            RefreshAll();
        }

        /// <summary>A column heading. Every column gets one so the three panes read as
        /// separate tools rather than one long form.</summary>
        private static Label SectionHeader(string text, float topMargin = 0f)
        {
            var label = new Label(text);
            label.style.color = new Color(0.42f, 0.46f, 0.54f);
            label.style.fontSize = 9;
            label.style.letterSpacing = 2;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = topMargin;
            label.style.marginBottom = 5;
            return label;
        }

        // ── The panel's own dropdown ──────────────────────────────────────────
        //
        // The stock DropdownField brings a light theme into a dark panel - white
        // boxes with dark text and a default menu that matches nothing. This is the
        // same choice control drawn in the panel's own colours: a labelled button
        // with a caret, and a dark list that opens under it, closes on any click
        // outside, and never drags a foreign theme into the editor.

        private VisualElement _dropdownCatcher;
        private VisualElement _openDropdownList;

        private void CloseOpenDropdown()
        {
            if (_openDropdownList != null)
            {
                _openDropdownList.RemoveFromHierarchy();
                _openDropdownList = null;
            }
            if (_dropdownCatcher != null)
            {
                _dropdownCatcher.RemoveFromHierarchy();
                _dropdownCatcher = null;
            }
        }

        private void ShowDropdownList(StyledDropdownField field, VisualElement anchor)
        {
            CloseOpenDropdown();

            var list = new VisualElement();
            list.style.position = Position.Absolute;
            list.style.backgroundColor = new StyleColor(new Color(0.045f, 0.055f, 0.075f, 0.98f));
            list.style.borderLeftWidth = list.style.borderRightWidth =
                list.style.borderTopWidth = list.style.borderBottomWidth = 1;
            list.style.borderLeftColor = list.style.borderRightColor =
                list.style.borderTopColor = list.style.borderBottomColor =
                    new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.45f));
            list.style.paddingTop = 2;
            list.style.paddingBottom = 2;

            var scroll = new ScrollView();
            scroll.style.maxHeight = 220;
            list.Add(scroll);

            for (int i = 0; i < field.Options.Count; i++)
            {
                int pick = i;
                var row = new Label(field.Options[i]);
                row.style.color = new Color(0.90f, 0.93f, 0.97f);
                row.style.fontSize = 11;
                row.style.paddingLeft = 8;
                row.style.paddingRight = 8;
                row.style.paddingTop = 4;
                row.style.paddingBottom = 4;
                row.style.unityTextAlign = TextAnchor.MiddleLeft;
                bool chosen = i == field.index;
                if (chosen)
                    row.style.backgroundColor = new StyleColor(new Color(0.12f, 0.32f, 0.45f, 0.55f));
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    CloseOpenDropdown();
                    field.SetPicked(pick);
                });
                row.RegisterCallback<MouseEnterEvent>(_ =>
                {
                    row.style.backgroundColor = new StyleColor(new Color(0.16f, 0.42f, 0.58f, 0.65f));
                });
                row.RegisterCallback<MouseLeaveEvent>(_ =>
                {
                    row.style.backgroundColor = chosen
                        ? new StyleColor(new Color(0.12f, 0.32f, 0.45f, 0.55f))
                        : new StyleColor(Color.clear);
                });
                scroll.Add(row);
            }

            var catcher = new VisualElement();
            catcher.style.position = Position.Absolute;
            catcher.style.left = 0; catcher.style.right = 0;
            catcher.style.top = 0; catcher.style.bottom = 0;
            catcher.style.backgroundColor = new StyleColor(Color.clear);
            catcher.pickingMode = PickingMode.Position;
            catcher.RegisterCallback<ClickEvent>(_ => CloseOpenDropdown());

            // The catcher spans the whole window so a click anywhere else closes the
            // list. Both live on the window root, catcher first, list last: painting
            // order decides the stacking (this Unity has no style.zIndex). The list is
            // placed under its button in panel coordinates.
            if (_root == null || anchor == null) { CloseOpenDropdown(); return; }
            _root.Add(catcher);
            _root.Add(list);
            Rect anchorBox = anchor.worldBound;
            Rect rootBox = _root.worldBound;
            list.style.left = anchorBox.x - rootBox.x;
            list.style.top = anchorBox.y - rootBox.y + anchorBox.height;
            list.style.width = Mathf.Max(80f, anchorBox.width);
            list.BringToFront();
            _dropdownCatcher = catcher;
            _openDropdownList = list;
        }

        private sealed class StyledDropdownField
        {
            public readonly VisualElement Root;
            public readonly List<string> Options;
            public System.Action<int> OnPicked;
            public int index { get; private set; }

            private readonly Label _value;
            private readonly VisualElement _button;
            private readonly GridScreenConfigUI _ui;

            public VisualElement parent => Root.parent;

            public StyledDropdownField(GridScreenConfigUI ui, string label, List<string> options, int startIndex)
            {
                _ui = ui;
                Options = options ?? new List<string>();
                index = Mathf.Clamp(startIndex, 0, Mathf.Max(0, Options.Count - 1));

                Root = new VisualElement();
                Root.style.position = Position.Relative;
                Root.style.marginBottom = 6;

                if (!string.IsNullOrEmpty(label))
                {
                    var caption = new Label(label);
                    caption.style.color = new Color(0.62f, 0.66f, 0.74f);
                    caption.style.fontSize = 10;
                    caption.style.marginBottom = 2;
                    Root.Add(caption);
                }

                _button = new VisualElement();
                _button.style.flexDirection = FlexDirection.Row;
                _button.style.alignItems = Align.Center;
                _button.style.backgroundColor = new StyleColor(new Color(0.025f, 0.030f, 0.045f, 1f));
                _button.style.borderLeftWidth = _button.style.borderRightWidth =
                    _button.style.borderTopWidth = _button.style.borderBottomWidth = 1;
                _button.style.borderLeftColor = _button.style.borderRightColor =
                    _button.style.borderTopColor = _button.style.borderBottomColor =
                        new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.35f));
                _button.style.paddingLeft = 8;
                _button.style.paddingRight = 6;
                _button.style.paddingTop = 4;
                _button.style.paddingBottom = 4;

                _value = new Label(CurrentText());
                _value.style.color = new Color(0.92f, 0.94f, 0.97f);
                _value.style.fontSize = 11;
                _value.style.flexGrow = 1;
                _value.style.unityTextAlign = TextAnchor.MiddleLeft;
                _button.Add(_value);

                var caret = new Label("▾");
                caret.style.color = new Color(0.55f, 0.72f, 0.82f);
                caret.style.fontSize = 10;
                caret.style.paddingLeft = 4;
                _button.Add(caret);

                _button.RegisterCallback<ClickEvent>(_ => _ui.ShowDropdownList(this, Root));
                _button.RegisterCallback<MouseEnterEvent>(_ =>
                    _button.style.backgroundColor = new StyleColor(new Color(0.045f, 0.055f, 0.080f, 1f)));
                _button.RegisterCallback<MouseLeaveEvent>(_ =>
                    _button.style.backgroundColor = new StyleColor(new Color(0.025f, 0.030f, 0.045f, 1f)));

                Root.Add(_button);
            }

            private string CurrentText()
                => index >= 0 && index < Options.Count ? Options[index] : string.Empty;

            public void SetValueWithoutNotify(string value)
            {
                int found = Options.IndexOf(value);
                if (found >= 0) index = found;
                _value.text = CurrentText();
            }

            public void SetPicked(int pick)
            {
                if (pick < 0 || pick >= Options.Count) return;
                index = pick;
                _value.text = CurrentText();
                OnPicked?.Invoke(pick);
            }
        }

        /// <summary>The panel's own shape, so the preview is the same rectangle the player
        /// will look at on the wall. Width over height, from the sizes the prefabs use.</summary>
        private static float ScreenAspect(ScreenSize size) => size switch
        {
            ScreenSize.Small => 1.12f,
            ScreenSize.Wide => 2.18f,
            _ => 1f,
        };

        /// <summary>Fit the preview box into the room the centre column actually has, at the
        /// panel's aspect ratio. Called on every resize, so a maximised window gets a
        /// maximised preview.</summary>
        private void LayoutPreview(VisualElement host)
        {
            if (_previewBox == null) return;

            float availableW = Mathf.Max(80f, host.contentRect.width - 8f);
            float availableH = Mathf.Max(80f, host.contentRect.height - 8f);
            float aspect = ScreenAspect(_target != null ? _target.screenSize : ScreenSize.Medium);

            float width = Mathf.Min(availableW, availableH * aspect);
            float height = width / aspect;

            _previewBox.style.width = width;
            _previewBox.style.height = height;
            RefreshPreview(true);
        }

        /// <summary>
        /// Fill the preview with the panel's current text and dress the frame the way the
        /// block dresses the real one: the player's colour on the border, the border weight
        /// they chose, and type sized so the longest line still fits the box.
        /// </summary>
        private void RefreshPreview(bool force = false)
        {
            if (_previewBox == null || _previewText == null || _target == null) return;

            string body = _target.FormattedDisplay;
            string styled = GridScreenBlock.StyleText(body, _target.fontStyle);

            // 17.36.0-dev: the box size is part of the cache key. The old check only looked
            // at the text, so the first refresh - made before the layout had a size - set the
            // type to its 7-point floor and nothing ever corrected it. That is why every
            // preview looked like a caption on a billboard.
            Rect box = _previewBox.contentRect;
            bool sizeChanged = !Mathf.Approximately(box.width, _lastPreviewWidth)
                            || !Mathf.Approximately(box.height, _lastPreviewHeight);
            if (!force && !sizeChanged && styled == _lastPreviewText) return;

            _lastPreviewText = styled;
            _lastPreviewWidth = box.width;
            _lastPreviewHeight = box.height;

            var info = ScreenPageCatalog.For(_target.dataMode);
            // A split names its layout on the title bar; each pane names itself inside it.
            _previewTitle.text = _target.splitMode > 0
                ? "SPLIT x" + (_target.splitMode == 3 ? 4 : 2)
                : info.Label.ToUpperInvariant();
            _previewTitle.style.color = new StyleColor(new Color(
                _target.textColor.r * 0.7f, _target.textColor.g * 0.7f, _target.textColor.b * 0.7f, 0.85f));

            // Team banner: the preview shows the cloth, because the cloth is the page.
            bool banner = _target.dataMode == ScreenDataMode.TeamBanner;
            _previewBox.style.backgroundColor = new StyleColor(banner
                ? new Color(0.62f, 0.52f, 0.32f, 1f)
                : new Color(0.015f, 0.018f, 0.028f));

            _previewText.text = styled;
            _previewText.style.color = new StyleColor(banner ? new Color(0.12f, 0.09f, 0.06f) : _target.textColor);
            _previewText.style.unityFontStyleAndWeight =
                (_target.fontStyle == 1 || _target.fontStyle == 3) ? FontStyle.Bold : FontStyle.Normal;
            // No tracking, ever: the wall's stretch is a scale on the glyph mesh, while
            // editor letter spacing moves every character off the column the ink layers
            // and the pictures are aligned to. Tracking here is what made the preview a
            // different page from the one on the glass.
            _previewText.style.letterSpacing = 0f;

            _previewStatus.text = _target.IsPowered ? "LIVE" : "NO PWR";
            _previewStatus.style.color = new StyleColor(new Color(
                _target.textColor.r * 0.55f, _target.textColor.g * 0.55f, _target.textColor.b * 0.55f));

            // Type size by the same rule the wall uses: the longest line and the line count
            // decide, so what fits in the editor is what fits on the glass.
            // One pass over the text measures all three: the longest line, how many lines
            // there are, and how many of those actually print something (a page ends with
            // blank rows, and the wall sizes its type to the PRINTED block).
            int longest = 1, lineLength = 0, printedLines = 0, printedLength = 0;
            for (int i = 0; i <= styled.Length; i++)
            {
                char c = i < styled.Length ? styled[i] : '\n';
                if (c == '\n')
                {
                    if (printedLength > 0) printedLines++;
                    lineLength = 0;
                    printedLength = 0;
                    continue;
                }
                lineLength++;
                if (c != ' ' && c != '\r') printedLength = lineLength;
                if (lineLength > longest) longest = lineLength;
            }
            if (printedLines <= 0) printedLines = 1;

            // The box the wall gives the page: 96 by 90 percent of the glass.
            float boxW = Mathf.Max(60f, box.width * 0.96f - 8f);
            float boxH = Mathf.Max(40f, box.height * 0.90f - 8f);

            // The wall sizes type to the PRINTED block, on a cell grid whose columns come
            // from the panel's own shape. The preview does the same two things, so the page
            // in the editor is the page on the glass: type big enough to fill the box, on a
            // character grid - and the pictures and gauges land in the same cells.
            float stretch = GridScreenBlock.StyleStretch(_target.fontStyle);
            // THE SAME RULE THE WALL USES, in the editor's units.
            //
            // On the glass a cell is measured from the baked mesh and the scale is solved
            // so the PRINTED block fills the panel, capped so a two-line page cannot become
            // two letters tall. Here a monospace glyph advances ~0.60 em and a line sits
            // ~1.12 em apart, so the same solve is:
            float byWidth = boxW / (Mathf.Max(1, longest) * 0.60f * stretch);
            float byHeight = boxH / (Mathf.Max(1, printedLines) * 1.12f);
            float byLineCap = (boxH / 4f) / 1.12f;
            _previewText.style.fontSize =
                Mathf.Clamp(Mathf.Min(byWidth, Mathf.Min(byHeight, byLineCap)), 4f, 96f);

            // Border: the block's own borderStyle, drawn in the player's own colour.
            float weight = _target.borderStyle <= 0 ? 1f : _target.borderStyle == 1 ? 2f : _target.borderStyle == 2 ? 3f : 4f;
            var accent = _target.borderStyle <= 0
                ? new Color(0.22f, 0.25f, 0.30f, 0.85f)
                : new Color(_target.textColor.r, _target.textColor.g, _target.textColor.b,
                            _target.borderStyle >= 3 ? 0.95f : 0.60f);
            _previewBox.style.borderLeftWidth = _previewBox.style.borderRightWidth =
            _previewBox.style.borderTopWidth = _previewBox.style.borderBottomWidth = weight;
            _previewBox.style.borderLeftColor = _previewBox.style.borderRightColor =
            _previewBox.style.borderTopColor = _previewBox.style.borderBottomColor = new StyleColor(accent);
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

        /// <summary>Show only the sources whose name or category contains the query.
        /// Case-insensitive, and a blank query shows everything.</summary>
        private void ApplySourceFilter()
        {
            string query = (_sourceQuery ?? string.Empty).Trim();
            for (int i = 0; i < _sourceButtons.Count; i++)
            {
                var button = _sourceButtons[i];
                if (button == null) continue;
                bool match = query.Length == 0
                    || (i < _sourceLabels.Count
                        && _sourceLabels[i].IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0);
                button.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void RefreshHighlights()
        {
            if (_target == null) return;

            // Rebuild source buttons with current state, by identity. Matching on the
            // printed NAME was the second half of the same bug: six cargo containers with
            // the same name all read as selected when one of them was.
            for (int i = 0; i < _sourceBtns.Count && i < _sourcePositions.Count; i++)
            {
                var btn = _sourceBtns[i];
                var check = btn.Q("ToggleCheck") as Label;
                var nameL = btn.Q("SourceName") as Label;
                if (check == null || nameL == null) continue;

                EntityId id = i < _sourceIds.Count ? _sourceIds[i] : EntityId.None;
                bool nowOn = _target.HasSource(_sourcePositions[i], id);

                check.text = nowOn ? "\u2611" : "\u2610";
                check.style.color = nowOn ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.50f, 0.55f, 0.60f);
                btn.style.backgroundColor = new StyleColor(nowOn ? new Color(0.12f, 0.18f, 0.28f) : new Color(0.10f, 0.11f, 0.14f));
                btn.style.borderLeftWidth = nowOn ? 3f : 0f;
                if (nowOn) btn.style.borderLeftColor = new StyleColor(new Color(0.20f, 0.55f, 0.95f));
            }

            if (_sourceCount != null) _sourceCount.text = _target.SourceCount + " source(s)";
            RefreshPreview(true);
        }

        private void RefreshAll()
        {
            RefreshHighlights();
            RefreshModeHighlights();
            RefreshAppearanceHighlights();
            RefreshPanePickers();
        }

        private void RefreshModeHighlights()
        {
            for (int i = 0; i < _modeBtns.Count; i++)
            {
                // Split: the list highlights the focused pane's page, because that is the
                // page a click would replace.
                ScreenDataMode shown = _target.splitMode > 0
                    ? _target.PanePage(Mathf.Clamp(_activePane, 0, 3))
                    : _target.dataMode;
                bool active = i < _modeBtnModes.Count && _modeBtnModes[i] == shown;
                _modeBtns[i].style.backgroundColor = new StyleColor(
                    active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }
            RefreshPageOptions();
            RefreshPreview(true);
        }

        /// <summary>
        /// The pages that draw themselves on a pane. A pane shares the glass with its
        /// neighbours, so a provider page that dumps another block's raw text has no room
        /// and no columns to lay out in; the catalogue's canvas pages do.
        /// </summary>
        private void BuildDrawablePageList()
        {
            _drawablePages.Clear();
            for (int i = 0; i < ScreenPageCatalog.All.Length; i++)
            {
                var page = ScreenPageCatalog.All[i];
                if (GridScreenBlock.DrawsItsOwnPage(page.Mode)) _drawablePages.Add(page.Mode);
            }
        }

        private void BuildPanePicker(int index)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2;

            // The label is the pane's focus button: with four panes on one glass, "which
            // pane is the page list talking to" has to be answerable at a glance.
            var label = new Button(() =>
            {
                _activePane = index;
                RefreshPanePickers();
                RefreshModeHighlights();
                RefreshPageInfo();
            })
            {
                text = "PANE " + (index + 1),
                tooltip = "Click to send the page list to this pane."
            };
            label.style.width = 58;
            label.style.paddingLeft = 0; label.style.paddingRight = 0;
            label.style.fontSize = 9;
            label.style.letterSpacing = 1;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.backgroundColor = new StyleColor(Color.clear);
            label.style.borderLeftWidth = label.style.borderRightWidth =
            label.style.borderTopWidth = label.style.borderBottomWidth = 0;
            label.style.color = new Color(0.46f, 0.51f, 0.60f);
            row.Add(label);
            _paneLabels.Add(label);

            var names = new List<string>(_drawablePages.Count);
            for (int i = 0; i < _drawablePages.Count; i++)
                names.Add(ScreenPageCatalog.For(_drawablePages[i]).Label);

            var field = new StyledDropdownField(this, null, names, 0);
            field.Root.style.flexGrow = 1;
            int paneIndex = index;
            field.OnPicked = picked =>
            {
                if (picked < 0 || picked >= _drawablePages.Count) return;
                _target.SetPanePage(paneIndex, _drawablePages[picked]);
                RefreshModeHighlights();
                RefreshPageInfo();
            };
            row.Add(field.Root);

            _paneSection.Add(row);
            _paneFields.Add(field);
        }

        /// <summary>Show the pickers a layout actually uses, and point them at its pages.</summary>
        private void RefreshPanePickers()
        {
            if (_target == null || _paneSection == null) return;

            int panes = _target.splitMode <= 0 ? 0 : _target.splitMode == 3 ? 4 : 2;
            _paneSection.style.display = panes == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            for (int i = 0; i < _paneFields.Count; i++)
            {
                var field = _paneFields[i];
                field.parent.style.display = i < panes ? DisplayStyle.Flex : DisplayStyle.None;
                if (i >= panes) continue;

                int found = _drawablePages.IndexOf(_target.PanePage(i));
                if (found >= 0 && found != field.index) field.SetValueWithoutNotify(
                    ScreenPageCatalog.For(_drawablePages[found]).Label);

                if (i < _paneLabels.Count)
                {
                    bool focused = i == Mathf.Clamp(_activePane, 0, panes - 1);
                    _paneLabels[i].style.color = focused
                        ? new Color(0.42f, 0.76f, 1f)
                        : new Color(0.46f, 0.51f, 0.60f);
                }
            }
        }

        /// <summary>
        /// Rebuild the page-option block after a choice that changes what options exist.
        ///
        /// The graph's panel-series pickers only exist for the panels the layout shows, so
        /// changing the panel count has to re-create the pickers; without that the player
        /// is left with the old rows, which either write nowhere or write to a panel that
        /// is no longer drawn - the "I can only change the other graph by leaving the
        /// editor" symptom. Deferred by one scheduled tick so the element that raised the
        /// event is not destroyed inside its own callback.
        /// </summary>
        private void SchedulePageOptionsRefresh()
        {
            if (_root == null) return;
            _root.schedule.Execute(() =>
            {
                RefreshPageOptions();
                RefreshPreview(true);
            }).ExecuteLater(1);
        }

        private void RefreshPageOptions()
        {
            if (_pageOptionsSlot == null || _target == null) return;
            CloseOpenDropdown();
            _pageOptionsSlot.Clear();
            ScreenDataMode activeMode = _target.splitMode > 0
                ? _target.PanePage(Mathf.Clamp(_activePane, 0, 3))
                : _target.dataMode;

            bool hasOptions = activeMode == ScreenDataMode.Manifest
                || activeMode == ScreenDataMode.FillBars
                || activeMode == ScreenDataMode.Inventory
                || activeMode == ScreenDataMode.InventoryGrid
                || activeMode == ScreenDataMode.PowerGraph
                || activeMode == ScreenDataMode.BatteryBank;
            _pageOptionsSlot.style.display = hasOptions ? DisplayStyle.Flex : DisplayStyle.None;
            if (!hasOptions) return;

            _pageOptionsSlot.Add(SectionHeader("PAGE OPTIONS", 12));

            if (activeMode == ScreenDataMode.Manifest || activeMode == ScreenDataMode.FillBars
                || activeMode == ScreenDataMode.Inventory)
            {
                // The heading is shared: manifest, fill bars and the storage monitor all
                // answer to the player's own name before they answer to a container's.
                var field = new TextField("Heading");
                field.value = _target.manifestTitle ?? string.Empty;
                field.tooltip = "Leave blank to use the selected container or construct name.";
                field.style.marginBottom = 6;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (_target == null) return;
                    _target.manifestTitle = evt.newValue;
                    RefreshPreview(true);
                });
                field.RegisterCallback<GeometryChangedEvent>(_ => StyleTextInput(field));
                TrackTextField(field);
                _pageOptionsSlot.Add(field);
                field.schedule.Execute(() => StyleTextInput(field));
            }

            if (activeMode == ScreenDataMode.Inventory)
            {
                var note = new TextField("Bottom-left note");
                note.value = _target.storageNote ?? string.Empty;
                note.tooltip = "A free line printed in the bottom-left corner of the storage monitor.";
                note.style.marginBottom = 6;
                note.RegisterValueChangedCallback(evt =>
                {
                    if (_target == null) return;
                    _target.storageNote = evt.newValue;
                    RefreshPreview(true);
                });
                note.RegisterCallback<GeometryChangedEvent>(_ => StyleTextInput(note));
                TrackTextField(note);
                _pageOptionsSlot.Add(note);
                note.schedule.Execute(() => StyleTextInput(note));

                var weight = new Toggle("Show item weight") { value = _target.showStorageWeight };
                weight.tooltip = "Print each item's mass under its count.";
                weight.RegisterValueChangedCallback(evt =>
                {
                    if (_target == null) return;
                    _target.showStorageWeight = evt.newValue;
                    RefreshPreview(true);
                });
                _pageOptionsSlot.Add(weight);
            }

            if (activeMode == ScreenDataMode.InventoryGrid)
            {
                var fillToggle = new Toggle("Show fill bar at top") { value = _target.showInventoryFill };
                fillToggle.tooltip = "The hold's fill percentage and bar across the top of the grid.";
                fillToggle.RegisterValueChangedCallback(evt =>
                {
                    if (_target == null) return;
                    _target.showInventoryFill = evt.newValue;
                    RefreshPreview(true);
                });
                _pageOptionsSlot.Add(fillToggle);
            }

            if (activeMode == ScreenDataMode.PowerGraph)
            {
                var panels = new StyledDropdownField(this, "Graph panels", new List<string> { "1", "2", "3" },
                    Mathf.Clamp(_target.powerGraphPanels, 1, 3) - 1);
                panels.OnPicked = _ =>
                {
                    if (_target == null) return;
                    _target.powerGraphPanels = Mathf.Clamp(panels.index + 1, 1, 3);
                    SchedulePageOptionsRefresh();
                };
                _pageOptionsSlot.Add(panels.Root);

                var span = new StyledDropdownField(this, "Time span",
                    new List<string> { "5 min", "15 min", "30 min", "1 hour", "12 hours", "24 hours", "All time" },
                    Mathf.Clamp(_target.powerGraphSpan, 0, 6));
                span.OnPicked = _ =>
                {
                    if (_target == null) return;
                    _target.powerGraphSpan = Mathf.Clamp(span.index, 0, 6);
                    RefreshPreview(true);
                };
                _pageOptionsSlot.Add(span.Root);

                // One series picker per visible panel: what each plot asks of the construct.
                var names = new List<string>(ScreenGraphSeriesCatalog.All.Length);
                for (int i = 0; i < ScreenGraphSeriesCatalog.All.Length; i++)
                    names.Add(ScreenGraphSeriesCatalog.All[i].Label);

                int visiblePanels = Mathf.Clamp(_target.powerGraphPanels, 1, 3);
                for (int p = 0; p < visiblePanels; p++)
                {
                    string current = p switch
                    {
                        0 => _target.graphSeriesA,
                        1 => _target.graphSeriesB,
                        _ => _target.graphSeriesC,
                    };
                    int index = 0;
                    var parsed = ScreenGraphSeriesCatalog.Parse(current, ScreenGraphSeries.usage);
                    for (int i = 0; i < ScreenGraphSeriesCatalog.All.Length; i++)
                        if (ScreenGraphSeriesCatalog.All[i].Series == parsed) { index = i; break; }

                    int pane = p; // capture for the closure
                    var picker = new StyledDropdownField(this, "Panel " + (p + 1) + " series", names, index);
                    picker.OnPicked = _ =>
                    {
                        if (_target == null) return;
                        string value = ScreenGraphSeriesCatalog.All[Mathf.Clamp(picker.index, 0, names.Count - 1)].Series.ToString();
                        switch (pane)
                        {
                            case 0: _target.graphSeriesA = value; break;
                            case 1: _target.graphSeriesB = value; break;
                            default: _target.graphSeriesC = value; break;
                        }
                        SchedulePageOptionsRefresh();
                    };
                    _pageOptionsSlot.Add(picker.Root);
                }
            }

            if (activeMode == ScreenDataMode.BatteryBank)
            {
                var rates = new Toggle("Show battery power rates")
                {
                    value = _target.showBatteryPowerRates,
                    tooltip = "List current charge/discharge watts and each battery's maximum rate.",
                };
                rates.RegisterValueChangedCallback(evt =>
                {
                    if (_target == null) return;
                    _target.showBatteryPowerRates = evt.newValue;
                    RefreshPreview(true);
                });
                _pageOptionsSlot.Add(rates);
            }
        }

        private void RefreshPageInfo()
        {
            if (_target == null) return;
            ScreenDataMode mode = _target.splitMode > 0
                ? _target.PanePage(Mathf.Clamp(_activePane, 0, 3))
                : _target.dataMode;
            var info = ScreenPageCatalog.For(mode);
            if (_pageInfoTitle != null) _pageInfoTitle.text = info.Label.ToUpperInvariant();
            if (_pageInfoBlurb != null) _pageInfoBlurb.text = info.Blurb;

            string hint = string.Empty;
            if (_target.splitMode > 0
                && (_target.screenSize == ScreenSize.Small || _target.screenSize == ScreenSize.Wide))
            {
                hint = "This panel has too few rows to split - a pane would be one line. "
                     + "Splits are worth reading on a Large or ExtraLarge screen.";
            }
            else if (!ScreenPageCatalog.FitsOn(_target.screenSize, info.MinSize))
            {
                hint = "Built for a " + ScreenPageCatalog.SizeLabel(info.MinSize).ToLowerInvariant()
                     + " panel; on this " + ScreenPageCatalog.SizeLabel(_target.screenSize).ToLowerInvariant()
                     + " one it will show less.";
            }
            else if (ScreenPageCatalog.NeedsSource(mode) && _target.SourceCount == 0)
            {
                hint = "Needs a linked source - pick one under DATA SOURCES.";
            }
            if (_pageInfoHint != null)
            {
                _pageInfoHint.text = hint;
                _pageInfoHint.style.display = string.IsNullOrEmpty(hint) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            RefreshPageOptions();
            RefreshPreview(true);
        }

        private void RefreshAppearanceHighlights()
        {
            if (_target == null) return;

            string[] layoutNames = { "Single", "Two across", "Two tall", "Quarters" };
            for (int i = 0; i < _layoutBtns.Count; i++)
            {
                bool active = i < layoutNames.Length && i == Mathf.Clamp(_target.splitMode, 0, 3);
                _layoutBtns[i].style.backgroundColor = new StyleColor(active
                    ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }

            string[] borderNames = { "None", "Thin", "Thick", "Glow" };
            for (int i = 0; i < _borderBtns.Count; i++)
            {
                bool active = i < borderNames.Length && i == Mathf.Clamp(_target.borderStyle, 0, 3);
                _borderBtns[i].style.backgroundColor = new StyleColor(active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }

            string[] fontNames = { "Default", "Bold", "Wide", "Compact" };
            for (int i = 0; i < _fontBtns.Count; i++)
            {
                bool active = i < fontNames.Length && i == Mathf.Clamp(_target.fontStyle, 0, 3);
                _fontBtns[i].style.backgroundColor = new StyleColor(active ? new Color(0.20f, 0.55f, 0.95f) : new Color(0.12f, 0.14f, 0.18f));
            }
        }

        private void RefreshCustomTextField()
        {
            if (_panel == null || _target == null) return;

            var slot = _panel.Q("CustomTextSlot");
            if (slot == null) return;
            slot.Clear();

            // 17.35.1-dev: custom text is only offered when Custom is the page being edited.
            // It used to be appended to the bottom of the whole panel, which meant a text
            // box sat under the preview telling the player about a page they had not chosen.
            bool wantsCustom = _target.dataMode == ScreenDataMode.Custom && _target.splitMode <= 0;
            slot.style.display = wantsCustom ? DisplayStyle.Flex : DisplayStyle.None;
            if (!wantsCustom) return;

            var sec = new VisualElement { name = "CustomTextSection" };
            var h = new Label("CUSTOM TEXT");
            h.style.color = new Color(0.40f, 0.44f, 0.52f); h.style.fontSize = 9;
            h.style.letterSpacing = 2; h.style.unityFontStyleAndWeight = FontStyle.Bold;
            h.style.marginTop = 6; h.style.marginBottom = 3;
            sec.Add(h);

            var f = new TextField();
            TrackTextField(f);
            f.value = _target.customText; f.multiline = true;
            f.style.minHeight = 40;
            f.style.backgroundColor = new StyleColor(new Color(0.025f, 0.030f, 0.045f));
            f.style.color = new Color(0.92f, 0.94f, 0.97f);
            f.style.borderLeftWidth = f.style.borderRightWidth =
            f.style.borderTopWidth = f.style.borderBottomWidth = 1;
            f.style.borderLeftColor = f.style.borderRightColor =
            f.style.borderTopColor = f.style.borderBottomColor = new StyleColor(new Color(0.18f, 0.72f, 0.88f, 0.35f));
            f.style.whiteSpace = WhiteSpace.Normal;
            f.RegisterValueChangedCallback(e => { _target.customText = e.newValue; RefreshPreview(true); });
            f.RegisterCallback<GeometryChangedEvent>(_ => StyleTextInput(f));
            sec.Add(f);
            f.schedule.Execute(() => StyleTextInput(f));

            slot.Add(sec);
        }

        private void TrackTextField(TextField field)
        {
            if (field == null) return;
            field.RegisterCallback<FocusInEvent>(_ =>
            {
                _focusedTextField = field;
                UIState.ClaimTextInput();
            });
            field.RegisterCallback<FocusOutEvent>(_ => ClearTrackedTextField(field));
            field.RegisterCallback<DetachFromPanelEvent>(_ => ClearTrackedTextField(field));
        }

        private void ClearTrackedTextField(TextField field)
        {
            if (_focusedTextField != field) return;
            _focusedTextField = null;
            UIState.ReleaseTextInput();
        }

        private void ReleaseTextFieldFocus()
        {
            var field = _focusedTextField;
            _focusedTextField = null;
            if (field != null) field.Blur();
            if (field != null) UIState.ReleaseTextInput();
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
