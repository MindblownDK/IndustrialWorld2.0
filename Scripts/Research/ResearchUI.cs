// Assets/Scripts/VoxelEngine/Research/ResearchUI.cs
//
// INDUSTRIAL RESEARCH WINDOW — Spatial Pan/Zoom Canvas Overhaul (v5.40.0-dev).
//
// Premium dark industrial palette, themed panels, micro-interactions.
//
// Layout:
//   ┌──────────────────────────────────────────────────────────────────────┐
//   │  ◀  ▶  ⬚  [Search___________]  Era: All  [Close]                   │
//   ├──────────┬───────────────────────────────────────────────────────────┤
//   │  Tabs    │  ∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞  │
//   │          │  (pan/zoom canvas with tier labels, node cards, glowing   │
//   │ • All    │   bezier connectors, era banners)                        │
//   │ • Log.   │                                                          │
//   │ • Prod   │    Era 1: Mechanized                                     │
//   │ • Power  │     Tier 1        Tier 2        Tier 3                   │
//   │ • Chem.  │    [Card]─ ─ ─ ─[Card]─ ─ ─ ─[Card]                     │
//   │ • Store  │      │                     │                             │
//   │ • Build  │    [Card]                 [Card]                         │
//   │ • Mil.   │                                                          │
//   │          │  Zoom: [−] [61%] [+] [Reset]                             │
//   ├──────────┴───────────────────────────────────────────────────────────┤
//   │  Details panel at bottom (collapsible): cost, unlocks, progress bar  │
//   └──────────────────────────────────────────────────────────────────────┘

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Research
{
    [RequireComponent(typeof(UIDocument))]
    public class ResearchUI : MonoBehaviour
    {
        public static ResearchUI Instance { get; private set; }

        public Inventory inventoryRef;

        // ── Palette ─────────────────────────────────────────────────────
        private static readonly Color BG_OVERLAY       = new(0.02f, 0.025f, 0.04f, 0.88f);
        private static readonly Color PANEL_BG         = new(0.08f, 0.09f, 0.12f, 0.98f);
        private static readonly Color CANVAS_BG        = new(0.045f, 0.05f, 0.07f, 1.00f);
        private static readonly Color CARD_BG          = new(0.13f, 0.15f, 0.19f, 1.00f);
        private static readonly Color CARD_HOVER       = new(0.18f, 0.21f, 0.26f, 1.00f);
        private static readonly Color CARD_READY       = new(0.16f, 0.22f, 0.28f, 1.00f);
        private static readonly Color CARD_ACTIVE      = new(0.13f, 0.30f, 0.55f, 1.00f);
        private static readonly Color CARD_DONE        = new(0.10f, 0.35f, 0.18f, 1.00f);
        private static readonly Color CARD_LOCKED      = new(0.08f, 0.09f, 0.12f, 1.00f);
        private static readonly Color BORDER_DEFAULT   = new(0.22f, 0.25f, 0.30f, 1.00f);
        private static readonly Color BORDER_SELECT    = new(0.95f, 0.78f, 0.20f, 1.00f);
        private static readonly Color BORDER_DONE      = new(0.40f, 0.85f, 0.50f, 1.00f);
        private static readonly Color BORDER_ACTIVE    = new(0.40f, 0.75f, 1.00f, 1.00f);
        private static readonly Color ACCENT_BLUE      = new(0.20f, 0.55f, 0.95f);
        private static readonly Color ACCENT_AMBER     = new(0.95f, 0.65f, 0.20f);
        private static readonly Color ACCENT_GREEN     = new(0.35f, 0.80f, 0.45f);
        private static readonly Color LINE_BASE        = new(0.30f, 0.34f, 0.40f, 0.60f);
        private static readonly Color LINE_READY       = new(0.50f, 0.85f, 1.00f, 0.80f);
        private static readonly Color LINE_DONE        = new(0.40f, 0.85f, 0.50f, 1.00f);
        private static readonly Color GLOW_READY       = new(0.20f, 0.55f, 0.95f, 0.15f);
        private static Color TextPrimary  => new(0.92f, 0.94f, 0.97f);
        private static Color TextMuted    => new(0.40f, 0.44f, 0.52f);

        // ── State ───────────────────────────────────────────────────────
        private UIDocument    _doc;
        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _canvas;          // the zoomable/pannable tree surface
        private VisualElement _connectors;      // draws bezier prereq lines
        private VisualElement _detailsPanel;
        private ScrollView    _canvasScroll;    // the scroll container

        private Label   _zoomLabel;
        private Label   _eraLabel;
        private Label   _progressLabel;
        private VisualElement _progressFill;

        private bool             _open;
        private bool             _searchHasFocus;
        private ResearchNode     _selected;
        private ResearchSubCategory? _activeSub = null;
        private string           _searchQuery = string.Empty;

        /// <summary>True while the Research search field owns keyboard input.</summary>
        public static bool IsSearchFocused => Instance != null && Instance._open && Instance._searchHasFocus;

        private float _zoom = 1.0f;
        private const float ZOOM_MIN = 0.35f;
        private const float ZOOM_MAX = 2.0f;
        private const float ZOOM_STEP = 0.12f;

        // Node geometry
        private const float NODE_W = 190f;
        private const float NODE_H = 110f;
        private const float NODE_GAP_Y = 14f;
        private const float TIER_GAP_X = 80f;
        private const float TREE_PAD = 40f;

        private readonly Dictionary<ResearchNode, Rect> _nodeRects = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            HideUI();
        }

        private void Update()
        {
            if (GameSettings.WasPressed(InputAction.Research) && !_searchHasFocus)
            {
                if (VoxelEngine.UI.UIState.PauseConsumedThisFrame) return;
                if (_open) Close();
                else if (!VoxelEngine.UI.UIState.IsBlocking) Open();
            }
            if (_open && GameSettings.WasPressed(InputAction.Pause))
            {
                Close();
                VoxelEngine.UI.UIState.PauseConsumedFrame = Time.frameCount;
            }

            // Live progress
            if (_open && _selected != null && ResearchManager.Instance?.ActiveResearch == _selected)
            {
                if (_progressFill != null)
                    _progressFill.style.width = new StyleLength(
                        new Length(Mathf.Clamp01(ResearchManager.Instance.ActiveProgress01) * 100, LengthUnit.Percent));
                if (_progressLabel != null)
                    _progressLabel.text = ResearchManager.Instance.ActiveHasCost
                        ? $"{ResearchManager.Instance.ActiveProgress01 * 100:0}%"
                        : "Awaiting Lab…";
            }

            // Spacebar to research selected node
            if (_open && _selected != null && GameSettings.WasPressed(InputAction.Build))
                TryResearchSelected();
        }

        // ── Open / Close ────────────────────────────────────────────────
        public void Open()
        {
            if (ResearchManager.Instance == null || ResearchManager.Instance.tree == null) return;
            if (inventoryRef == null) inventoryRef = FindAnyObjectByType<Inventory>();
            _open = true;
            _zoom = 1.0f;
            VoxelEngine.UI.UIState.PushBlock();
            Build();
            AnimateOpen();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _searchHasFocus = false;
            VoxelEngine.UI.UIState.TextInputActive = false;
            VoxelEngine.UI.UIState.PopBlock();
            HideUI();
        }

        private void HideUI()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
            _nodeRects.Clear();
            _detailsPanel = _progressFill = _progressLabel = null;
            _canvas = _connectors = _canvasScroll = null;
        }

        private void AnimateOpen()
        {
            if (_panel == null) return;
            _panel.style.opacity = 0f;
            _panel.style.scale = new StyleScale(new Scale(new Vector3(0.97f, 0.97f, 1f)));
            _panel.schedule.Execute(() =>
            {
                _panel.style.transitionProperty = new List<StylePropertyName> { "opacity", "scale" };
                _panel.style.transitionDuration = new List<TimeValue> { new(0.18f, TimeUnit.Second), new(0.18f, TimeUnit.Second) };
                _panel.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic), new(EasingMode.EaseOutCubic) };
                _panel.style.opacity = 1f;
                _panel.style.scale = new StyleScale(new Scale(Vector3.one));
            }).StartingIn(10);
        }

        // ── Build ───────────────────────────────────────────────────────
        private void Build()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(BG_OVERLAY);
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _nodeRects.Clear();

            _panel = new VisualElement();
            _panel.style.width = new StyleLength(new Length(94f, LengthUnit.Percent));
            _panel.style.maxWidth = 1400;
            _panel.style.height = new StyleLength(new Length(92f, LengthUnit.Percent));
            _panel.style.maxHeight = 860;
            _panel.style.paddingTop = 16; _panel.style.paddingBottom = 16;
            _panel.style.paddingLeft = 20; _panel.style.paddingRight = 20;
            _panel.style.backgroundColor = new StyleColor(PANEL_BG);
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth =
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 1;
            var border = new StyleColor(new Color(0.20f, 0.23f, 0.28f));
            _panel.style.borderTopColor = _panel.style.borderBottomColor =
            _panel.style.borderLeftColor = _panel.style.borderRightColor = border;
            SetRadius(_panel, 10);
            _root.Add(_panel);

            var flexCol = new VisualElement();
            flexCol.style.flexDirection = FlexDirection.Column;
            flexCol.style.flexGrow = 1;
            _panel.Add(flexCol);

            BuildHeader(flexCol);
            var bodyRow = new VisualElement();
            bodyRow.style.flexDirection = FlexDirection.Row;
            bodyRow.style.flexGrow = 1;
            bodyRow.style.minHeight = 0;
            flexCol.Add(bodyRow);
            BuildTabsColumn(bodyRow);
            BuildCanvasColumn(bodyRow);
            BuildDetailsPanel(flexCol);
        }

        // ── Header ──────────────────────────────────────────────────────
        private void BuildHeader(VisualElement parent)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 8;
            header.style.flexShrink = 0;
            parent.Add(header);

            // Title
            var accent = new VisualElement();
            accent.style.width = 4; accent.style.height = 24;
            accent.style.backgroundColor = new StyleColor(ACCENT_AMBER);
            accent.style.marginRight = 10;
            SetRadius(accent, 2);
            header.Add(accent);

            var title = new Label("TECH TREE");
            title.style.color = TextPrimary;
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 3;
            title.style.marginRight = 16;
            header.Add(title);

            // Zoom controls
            var zoomMinus = SmallIconButton("−", () => SetZoom(_zoom - ZOOM_STEP));
            header.Add(zoomMinus);

            _zoomLabel = new Label("100%");
            _zoomLabel.style.color = TextPrimary;
            _zoomLabel.style.fontSize = 11;
            _zoomLabel.style.minWidth = 40;
            _zoomLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _zoomLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(_zoomLabel);

            var zoomPlus = SmallIconButton("+", () => SetZoom(_zoom + ZOOM_STEP));
            header.Add(zoomPlus);

            var zoomReset = SmallIconButton("⇱", () => SetZoom(1f));
            zoomReset.style.marginRight = 16;
            header.Add(zoomReset);

            // Era label
            _eraLabel = new Label("");
            _eraLabel.style.color = TextMuted;
            _eraLabel.style.fontSize = 11;
            _eraLabel.style.letterSpacing = 1.5f;
            _eraLabel.style.marginRight = 14;
            _eraLabel.style.flexGrow = 1;
            header.Add(_eraLabel);
            UpdateEraLabel();

            // Search
            _searchField = new TextField();
            _searchField.style.width = 180;
            _searchField.style.marginRight = 10;
            var sfInput = _searchField.Q(TextField.textInputUssName);
            if (sfInput != null)
            {
                sfInput.style.backgroundColor = new StyleColor(new Color(0.05f, 0.055f, 0.075f));
                sfInput.style.color = TextPrimary;
                sfInput.style.unityTextAlign = TextAnchor.MiddleLeft;
                sfInput.style.paddingLeft = 8; sfInput.style.paddingRight = 8;
                SetRadius(sfInput, 4);
                ZeroBorder(sfInput);
            }
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = (evt.newValue ?? "").Trim();
                RebuildCanvas();
            });
            _searchField.RegisterCallback<FocusInEvent>(_ =>
            {
                _searchHasFocus = true;
                VoxelEngine.UI.UIState.TextInputActive = true;
            });
            _searchField.RegisterCallback<FocusOutEvent>(_ =>
            {
                _searchHasFocus = false;
                VoxelEngine.UI.UIState.TextInputActive = false;
            });
            _searchField.label = "";
            var sfLabel = _searchField.Q<Label>();
            if (sfLabel != null) sfLabel.style.display = DisplayStyle.None;
            var searchWrap = new VisualElement();
            searchWrap.style.flexDirection = FlexDirection.Row;
            searchWrap.style.alignItems = Align.Center;
            var searchIcon = new Label("🔍");
            searchIcon.style.fontSize = 12; searchIcon.style.marginRight = 4;
            searchIcon.style.color = TextMuted;
            searchWrap.Add(searchIcon);
            searchWrap.Add(_searchField);
            header.Add(searchWrap);

            // Close
            var close = StyledButton("✕", new Color(0.45f, 0.18f, 0.18f), Close);
            close.style.minHeight = 30; close.style.minWidth = 36;
            close.style.marginLeft = 8;
            header.Add(close);
        }
        private TextField _searchField;

        // ── Left Tabs ───────────────────────────────────────────────────
        private static readonly (ResearchSubCategory? sub, string label, string icon)[] SUB_TABS =
        {
            (null,                            "All",       "✦"),
            (ResearchSubCategory.Logistics,   "Logistics",  "⇄"),
            (ResearchSubCategory.Production,  "Production", "⚙"),
            (ResearchSubCategory.Power,       "Power",      "⚡"),
            (ResearchSubCategory.Chemistry,   "Chemistry",  "⚗"),
            (ResearchSubCategory.Storage,     "Storage",    "▣"),
            (ResearchSubCategory.Building,    "Building",   "▤"),
            (ResearchSubCategory.Military,    "Military",   "✚"),
        };

        private void BuildTabsColumn(VisualElement parent)
        {
            var col = new VisualElement();
            col.style.width = 130;
            col.style.marginRight = 8;
            col.style.flexShrink = 0;
            parent.Add(col);

            var hint = new Label("FILTER");
            hint.style.color = TextMuted;
            hint.style.fontSize = 9;
            hint.style.letterSpacing = 2.5f;
            hint.style.marginBottom = 4;
            hint.style.marginTop = 2;
            col.Add(hint);

            foreach (var t in SUB_TABS)
            {
                bool active = (_activeSub == t.sub);
                var b = new Button(() =>
                {
                    _activeSub = t.sub;
                    _selected = null;
                    RebuildCanvas();
                }) { text = $" {t.icon}  {t.label}" };
                b.style.minHeight = 30;
                b.style.marginBottom = 2;
                b.style.color = active ? TextPrimary : TextMuted;
                b.style.fontSize = 11;
                b.style.unityFontStyleAndWeight = FontStyle.Bold;
                b.style.backgroundColor = new StyleColor(active ? new Color(0.15f, 0.18f, 0.22f) : Color.clear);
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                b.style.paddingLeft = 10;
                SetRadius(b, 5);
                ZeroBorder(b);
                if (active)
                {
                    b.style.borderLeftWidth = 3;
                    b.style.borderLeftColor = new StyleColor(ACCENT_BLUE);
                }
                AddHoverEffect(b, active ? new Color(0.15f, 0.18f, 0.22f) : Color.clear, new Color(0.12f, 0.14f, 0.18f));
                col.Add(b);
            }
        }

        // ── Canvas Column ───────────────────────────────────────────────
        private void BuildCanvasColumn(VisualElement parent)
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.style.minWidth = 0;
            col.style.backgroundColor = new StyleColor(CANVAS_BG);
            SetRadius(col, 6);
            parent.Add(col);

            _canvasScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            VoxelEngine.UI.UITheme.StyleScroller(_canvasScroll);
            _canvasScroll.style.flexGrow = 1;
            col.Add(_canvasScroll);

            _canvas = new VisualElement();
            _canvas.style.transformOrigin = new TransformOrigin(new Length(0, LengthUnit.Percent), new Length(0, LengthUnit.Percent));
            _canvas.style.translate = new StyleTranslate(new Translate(0, 0, 0));
            _canvas.style.scale = new StyleScale(new Scale(new Vector3(_zoom, _zoom, 1f)));
            _canvas.style.paddingTop = TREE_PAD;
            _canvas.style.paddingBottom = TREE_PAD;
            _canvas.style.paddingLeft = TREE_PAD;
            _canvas.style.paddingRight = TREE_PAD;
            _canvasScroll.Add(_canvas);

            // Connectors layer
            _connectors = new VisualElement();
            _connectors.style.position = Position.Absolute;
            _connectors.style.left = 0; _connectors.style.top = 0;
            _connectors.style.right = 0; _connectors.style.bottom = 0;
            _connectors.pickingMode = PickingMode.Ignore;
            _connectors.generateVisualContent += DrawConnectors;
            _canvas.Add(_connectors);

            RebuildCanvas();
        }

        private void UpdateEraLabel()
        {
            if (_eraLabel == null) return;
            var rm = ResearchManager.Instance;
            if (rm == null || rm.tree == null) return;

            // Determine max tier researched
            int maxTier = 0;
            foreach (var n in rm.tree.nodes)
            {
                if (n != null && rm.IsUnlocked(n) && n.tier > maxTier)
                    maxTier = n.tier;
            }

            string era = maxTier switch
            {
                0 => "Era 0: Stranded",
                1 => "Era 1: Mechanized",
                2 => "Era 2: Automated",
                3 => "Era 3: Industrial",
                4 => "Era 4: Orbital",
                5 => "Era 5: Interplanetary",
                6 => "Era 6: Transcendent",
                >= 7 => "Era 7: Architect",
                _ => ""
            };
            _eraLabel.text = era;
        }

        private void RebuildCanvas()
        {
            if (_canvas == null) return;

            // Remove cards (keep connector)
            for (int i = _canvas.childCount - 1; i >= 0; i--)
            {
                if (_canvas[i] == _connectors) continue;
                _canvas.RemoveAt(i);
            }
            _nodeRects.Clear();

            var rm = ResearchManager.Instance;
            var tree = rm?.tree;
            if (tree == null) return;

            // Filter nodes
            var nodes = new List<ResearchNode>();
            foreach (var n in tree.nodes)
            {
                if (n == null) continue;
                if (_activeSub.HasValue && n.subCategory != _activeSub.Value) continue;
                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    string q = _searchQuery.ToLowerInvariant();
                    string blob = ((n.displayName ?? "") + " " + (n.description ?? "")).ToLowerInvariant();
                    if (!blob.Contains(q)) continue;
                }
                nodes.Add(n);
            }

            if (nodes.Count == 0)
            {
                var empty = new Label("No research in this filter.");
                empty.style.position = Position.Absolute;
                empty.style.left = 40; empty.style.top = 40;
                empty.style.color = TextMuted;
                empty.style.fontSize = 13;
                _canvas.Add(empty);
                _canvas.style.width = 400; _canvas.style.height = 200;
                _connectors.MarkDirtyRepaint();
                return;
            }

            // Group by tier
            var byTier = new SortedDictionary<int, List<ResearchNode>>();
            int maxTier = 0;
            foreach (var n in nodes)
            {
                int t = Mathf.Clamp(n.tier, 1, 10);
                if (!byTier.TryGetValue(t, out var list)) byTier[t] = list = new List<ResearchNode>();
                list.Add(n);
                if (t > maxTier) maxTier = t;
            }

            float xCursor = 0f;
            float maxH = 28f;

            foreach (var kv in byTier)
            {
                kv.Value.Sort((a, b) => a.column.CompareTo(b.column));

                // Tier header label
                var tierLbl = new Label("TIER " + kv.Key);
                tierLbl.style.position = Position.Absolute;
                tierLbl.style.left = xCursor + (NODE_W * 0.5f) - 22;
                tierLbl.style.top = 0;
                tierLbl.style.color = TextMuted;
                tierLbl.style.fontSize = 10;
                tierLbl.style.letterSpacing = 2.5f;
                tierLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                _canvas.Add(tierLbl);

                // Add a subtle vertical guide line below the tier header
                var guide = new VisualElement();
                guide.style.position = Position.Absolute;
                guide.style.left = xCursor + NODE_W * 0.5f - 1;
                guide.style.top = 22;
                guide.style.width = 2;
                guide.style.height = 8;
                guide.style.backgroundColor = new StyleColor(new Color(0.25f, 0.28f, 0.32f, 0.30f));
                SetRadius(guide, 1);
                _canvas.Add(guide);

                float yCursor = 32f;
                foreach (var n in kv.Value)
                {
                    var card = BuildNodeCard(n);
                    card.style.position = Position.Absolute;
                    card.style.left = xCursor;
                    card.style.top = yCursor;
                    _canvas.Add(card);
                    _nodeRects[n] = new Rect(xCursor, yCursor, NODE_W, NODE_H);
                    yCursor += NODE_H + NODE_GAP_Y;
                }
                if (yCursor > maxH) maxH = yCursor;
                xCursor += NODE_W + TIER_GAP_X;
            }

            _canvas.style.width = Mathf.Max(xCursor + 40, 600);
            _canvas.style.height = Mathf.Max(maxH + 40, 300);

            // Animate glow on cards that became ready
            foreach (var n in nodes)
            {
                if (rm != null && rm.ArePrerequisitesMet(n) && !rm.IsUnlocked(n) && _nodeRects.ContainsKey(n))
                {
                    // Subtle ready pulse is handled by the card's own breathing glow
                }
            }

            _connectors.MarkDirtyRepaint();
            UpdateEraLabel();
        }

        // ── Connector Lines ─────────────────────────────────────────────
        private void DrawConnectors(MeshGenerationContext mgc)
        {
            if (_nodeRects.Count == 0) return;
            var painter = mgc.painter2D;
            painter.lineWidth = 2f;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            var rm = ResearchManager.Instance;
            float time = Time.realtimeSinceStartup;

            foreach (var kv in _nodeRects)
            {
                var node = kv.Key;
                if (node.prerequisites == null) continue;
                Rect to = kv.Value;
                Vector2 toP = new Vector2(to.xMin, to.center.y);

                foreach (var p in node.prerequisites)
                {
                    if (p == null) continue;
                    if (!_nodeRects.TryGetValue(p, out var from)) continue;
                    Vector2 fromP = new Vector2(from.xMax, from.center.y);

                    bool preqMet = rm != null && rm.IsUnlocked(p);
                    bool nodeReady = rm != null && rm.ArePrerequisitesMet(node) && !rm.IsUnlocked(node);
                    bool nodeActive = rm != null && rm.ActiveResearch == node;

                    Color lineColor;
                    if (preqMet && (nodeReady || nodeActive))
                    {
                        // Pulsing ready glow
                        float pulse = 0.7f + 0.3f * Mathf.Sin(time * 2.5f);
                        lineColor = Color.Lerp(LINE_READY, Color.white, pulse * 0.3f);
                        painter.strokeColor = lineColor;
                        painter.lineWidth = 2.5f;
                    }
                    else if (preqMet)
                    {
                        painter.strokeColor = LINE_DONE;
                        painter.lineWidth = 2f;
                    }
                    else if (rm != null && rm.IsUnlocked(node))
                    {
                        painter.strokeColor = LINE_DONE;
                        painter.lineWidth = 2f;
                    }
                    else
                    {
                        painter.strokeColor = LINE_BASE;
                        painter.lineWidth = 1.5f;
                    }

                    painter.BeginPath();
                    painter.MoveTo(fromP);
                    float midX = (fromP.x + toP.x) * 0.5f;
                    painter.BezierCurveTo(new Vector2(midX, fromP.y),
                                          new Vector2(midX, toP.y), toP);
                    painter.Stroke();

                    // Arrowhead
                    Vector2 dir = (toP - new Vector2(midX, toP.y)).normalized;
                    if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
                    Vector2 left = new Vector2(-dir.y, dir.x);
                    Vector2 tip = toP;
                    Vector2 back = toP - dir * 8f;
                    painter.fillColor = painter.strokeColor;
                    painter.BeginPath();
                    painter.MoveTo(tip);
                    painter.LineTo(back + left * 4f);
                    painter.LineTo(back - left * 4f);
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        // ── Node Card ───────────────────────────────────────────────────
        private VisualElement BuildNodeCard(ResearchNode n)
        {
            var rm = ResearchManager.Instance;
            int rank = rm?.GetRank(n) ?? 0;
            bool maxed = rank >= n.maxRanks;
            bool ready = rm != null && rm.ArePrerequisitesMet(n);
            bool active = rm != null && rm.ActiveResearch == n;
            bool sel = _selected == n;

            Color bg = maxed ? CARD_DONE
                     : active ? CARD_ACTIVE
                     : ready && rank > 0 ? CARD_READY
                     : ready ? CARD_READY
                     : CARD_LOCKED;
            Color borderC = sel ? BORDER_SELECT
                         : maxed ? BORDER_DONE
                         : active ? BORDER_ACTIVE
                         : BORDER_DEFAULT;

            var card = new VisualElement();
            card.style.width = NODE_W;
            card.style.height = NODE_H;
            card.style.paddingTop = 8; card.style.paddingBottom = 8;
            card.style.paddingLeft = 10; card.style.paddingRight = 10;
            card.style.backgroundColor = new StyleColor(bg);
            card.style.borderTopWidth = card.style.borderBottomWidth =
            card.style.borderLeftWidth = card.style.borderRightWidth = 2;
            var sc = new StyleColor(borderC);
            card.style.borderTopColor = card.style.borderBottomColor =
            card.style.borderLeftColor = card.style.borderRightColor = sc;
            SetRadius(card, 6);

            // Glow overlay for ready-but-not-started nodes (breathing effect)
            if (ready && !maxed && !active)
            {
                var glow = new VisualElement();
                glow.style.position = Position.Absolute;
                glow.style.left = -4; glow.style.top = -4;
                glow.style.right = -4; glow.style.bottom = -4;
                glow.style.backgroundColor = new StyleColor(GLOW_READY);
                SetRadius(glow, 10);
                glow.pickingMode = PickingMode.Ignore;
                card.Add(glow);
                glow.SendToBack();

                // Breathing animation via scheduler
                float startTime = Time.realtimeSinceStartup + Random.value * 3f;
                glow.schedule.Execute(() =>
                {
                    if (glow.parent == null) return;
                    float t = Mathf.Sin((Time.realtimeSinceStartup - startTime) * 1.8f) * 0.5f + 0.5f;
                    glow.style.opacity = 0.3f + t * 0.5f;
                    glow.schedule.Execute(() => { }).StartingIn(50);
                }).Every(50);
            }

            // Top row: icon swatch + name
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            topRow.style.marginBottom = 4;
            card.Add(topRow);

            var iconSwatch = new VisualElement();
            iconSwatch.style.width = 24; iconSwatch.style.height = 24;
            iconSwatch.style.backgroundColor = new StyleColor(n.iconTint);
            iconSwatch.style.marginRight = 6;
            SetRadius(iconSwatch, 4);
            topRow.Add(iconSwatch);

            var name = new Label(n.displayName);
            name.style.color = (maxed || active) ? TextPrimary : (ready ? new Color(0.85f, 0.88f, 0.92f) : TextMuted);
            name.style.fontSize = 12;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.flexGrow = 1;
            name.pickingMode = PickingMode.Ignore;
            topRow.Add(name);

            // Cost row (compact)
            if (n.cost != null && n.cost.Length > 0)
            {
                var costRow = new VisualElement();
                costRow.style.flexDirection = FlexDirection.Row;
                costRow.style.flexWrap = Wrap.Wrap;
                costRow.style.marginBottom = 4;
                card.Add(costRow);
                foreach (var c in n.cost)
                {
                    if (c.pack == null || c.count <= 0) continue;
                    int eff = rm != null ? rm.GetEffectiveCount(n, c.count) : c.count;
                    int have = inventoryRef != null ? inventoryRef.container.CountOf(c.pack) : 0;
                    costRow.Add(SciencePackIcon(c.pack, eff, have));
                }
            }

            // Status line
            string statusLine;
            Color statusCol = TextMuted;
            if (n.IsRepeatable) { statusLine = $"Rank {rank}/{n.maxRanks}"; statusCol = rank > 0 ? ACCENT_AMBER : TextMuted; }
            else if (maxed) { statusLine = "✓ Done"; statusCol = ACCENT_GREEN; }
            else if (active) { statusLine = "● Researching"; statusCol = new Color(0.5f, 0.8f, 1.0f); }
            else if (ready) { statusLine = "Available"; statusCol = TextPrimary; }
            else { statusLine = "🔒 Locked"; statusCol = TextMuted; }

            var st = new Label(statusLine);
            st.style.color = statusCol;
            st.style.fontSize = 10;
            st.pickingMode = PickingMode.Ignore;
            card.Add(st);

            // Interactions
            card.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (sel) return;
                card.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
                card.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
                card.style.transitionDuration = new List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
                card.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic), new(EasingMode.EaseOutCubic) };
                card.style.backgroundColor = new StyleColor(Color.Lerp(bg, CARD_HOVER, 0.4f));
            });
            card.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (sel) return;
                card.style.scale = new StyleScale(new Scale(Vector3.one));
                card.style.backgroundColor = new StyleColor(bg);
            });
            card.RegisterCallback<MouseDownEvent>(e =>
            {
                _selected = n;
                RebuildCanvas();
                RebuildDetails();
                e.StopPropagation();
            });
            return card;
        }

        private VisualElement SciencePackIcon(ScienceItem pack, int need, int have)
        {
            var w = new VisualElement();
            w.style.flexDirection = FlexDirection.Row;
            w.style.alignItems = Align.Center;
            w.style.marginRight = 5;
            w.style.marginTop = 1;
            w.pickingMode = PickingMode.Ignore;

            var icon = new VisualElement();
            icon.style.width = 12; icon.style.height = 12;
            icon.style.backgroundColor = new StyleColor(pack.iconTint);
            icon.style.marginRight = 2;
            SetRadius(icon, 6);
            w.Add(icon);

            var lbl = new Label($"{Mathf.Min(have, need)}/{need}");
            lbl.style.color = have >= need ? new Color(0.7f, 0.95f, 0.7f) : TextMuted;
            lbl.style.fontSize = 9;
            w.Add(lbl);
            return w;
        }

        // ── Bottom Details Panel ────────────────────────────────────────
        private void BuildDetailsPanel(VisualElement parent)
        {
            _detailsPanel = new VisualElement();
            _detailsPanel.style.flexShrink = 0;
            _detailsPanel.style.marginTop = 6;
            _detailsPanel.style.paddingTop = 10; _detailsPanel.style.paddingBottom = 10;
            _detailsPanel.style.paddingLeft = 14; _detailsPanel.style.paddingRight = 14;
            _detailsPanel.style.backgroundColor = new StyleColor(new Color(0.06f, 0.065f, 0.09f, 0.98f));
            SetRadius(_detailsPanel, 6);
            _detailsPanel.style.minHeight = 80;
            _detailsPanel.style.maxHeight = 140;
            parent.Add(_detailsPanel);
            RebuildDetails();
        }

        private void RebuildDetails()
        {
            if (_detailsPanel == null) return;
            _detailsPanel.Clear();
            _progressFill = _progressLabel = null;

            if (_selected == null)
            {
                var hintRow = new VisualElement();
                hintRow.style.flexDirection = FlexDirection.Row;
                hintRow.style.alignItems = Align.Center;
                _detailsPanel.Add(hintRow);

                var icon = new Label("🔬");
                icon.style.fontSize = 24;
                icon.style.marginRight = 12;
                hintRow.Add(icon);

                var hint = new Label("Click a tech node to see details. Press SPACE to research available nodes.");
                hint.style.color = TextMuted;
                hint.style.fontSize = 12;
                hint.style.whiteSpace = WhiteSpace.Normal;
                hintRow.Add(hint);
                return;
            }

            var n = _selected;
            var rm = ResearchManager.Instance;
            if (rm == null) return;
            int rank = rm.GetRank(n);
            bool maxed = rank >= n.maxRanks;

            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            _detailsPanel.Add(topRow);

            // Name + tier
            var nameLbl = new Label(n.displayName);
            nameLbl.style.color = TextPrimary;
            nameLbl.style.fontSize = 14;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLbl.style.marginRight = 12;
            topRow.Add(nameLbl);

            var tierLbl = new Label($"T{n.tier}" + (n.IsRepeatable ? $"  R{rank}/{n.maxRanks}" : ""));
            tierLbl.style.color = TextMuted;
            tierLbl.style.fontSize = 11;
            tierLbl.style.marginRight = 14;
            topRow.Add(tierLbl);

            // Progress (if active)
            if (rm.ActiveResearch == n)
            {
                var barBg = new VisualElement();
                barBg.style.width = 120;
                barBg.style.height = 12;
                barBg.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.06f));
                SetRadius(barBg, 6);
                barBg.style.marginRight = 6;
                topRow.Add(barBg);

                _progressFill = new VisualElement();
                _progressFill.style.height = 12;
                _progressFill.style.width = new StyleLength(new Length(rm.ActiveProgress01 * 100, LengthUnit.Percent));
                _progressFill.style.backgroundColor = new StyleColor(ACCENT_BLUE);
                SetRadius(_progressFill, 6);
                barBg.Add(_progressFill);

                _progressLabel = new Label(rm.ActiveHasCost ? $"{rm.ActiveProgress01 * 100:0}%" : "Awaiting…");
                _progressLabel.style.color = TextPrimary;
                _progressLabel.style.fontSize = 10;
                _progressLabel.style.minWidth = 50;
                topRow.Add(_progressLabel);
            }

            topRow.Add(new VisualElement { style = { flexGrow = 1 } });

            // Cost pills
            if (n.cost != null)
            {
                foreach (var c in n.cost)
                {
                    if (c.pack == null) continue;
                    int eff = rm.GetEffectiveCount(n, c.count);
                    int have = inventoryRef != null ? inventoryRef.container.CountOf(c.pack) : 0;

                    var pill = new VisualElement();
                    pill.style.flexDirection = FlexDirection.Row;
                    pill.style.alignItems = Align.Center;
                    pill.style.marginRight = 8;
                    pill.style.paddingLeft = 6; pill.style.paddingRight = 8;
                    pill.style.paddingTop = 2; pill.style.paddingBottom = 2;
                    pill.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.18f));
                    SetRadius(pill, 8);
                    topRow.Add(pill);

                    var pIcon = new VisualElement();
                    pIcon.style.width = 12; pIcon.style.height = 12;
                    pIcon.style.backgroundColor = new StyleColor(c.pack.iconTint);
                    pIcon.style.marginRight = 4;
                    SetRadius(pIcon, 6);
                    pill.Add(pIcon);

                    var ok = have >= eff;
                    var pLbl = new Label($"{have}/{eff}");
                    pLbl.style.color = ok ? ACCENT_GREEN : new Color(0.95f, 0.45f, 0.45f);
                    pLbl.style.fontSize = 10;
                    pLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                    pill.Add(pLbl);
                }
            }

            // Description row
            var descRow = new VisualElement();
            descRow.style.flexDirection = FlexDirection.Row;
            descRow.style.marginTop = 6;
            _detailsPanel.Add(descRow);

            if (!string.IsNullOrEmpty(n.description))
            {
                var desc = new Label(n.description);
                desc.style.color = new Color(0.80f, 0.82f, 0.88f);
                desc.style.fontSize = 11;
                desc.style.whiteSpace = WhiteSpace.Normal;
                desc.style.flexGrow = 1;
                descRow.Add(desc);
            }

            // Action buttons
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.alignItems = Align.Center;
            btnRow.style.marginTop = 6;
            _detailsPanel.Add(btnRow);

            if (maxed)
            {
                var done = new Label("✓ COMPLETED");
                done.style.color = ACCENT_GREEN;
                done.style.fontSize = 12;
                done.style.unityFontStyleAndWeight = FontStyle.Bold;
                btnRow.Add(done);
            }
            else if (rm.ActiveResearch == n)
            {
                var cancelBtn = StyledButton("CANCEL", new Color(0.55f, 0.22f, 0.22f), () =>
                {
                    rm.CancelResearch();
                    RebuildDetails();
                    RebuildCanvas();
                });
                cancelBtn.style.minHeight = 28;
                cancelBtn.style.fontSize = 11;
                btnRow.Add(cancelBtn);
            }
            else if (!rm.ArePrerequisitesMet(n))
            {
                var blocked = new Label("✗ Prerequisites not met");
                blocked.style.color = new Color(0.92f, 0.45f, 0.45f);
                blocked.style.fontSize = 11;
                btnRow.Add(blocked);
            }
            else
            {
                bool canInventory = n.researchSeconds <= 0f;
                bool canPay = inventoryRef != null && AllAffordable(n);

                if (canInventory)
                {
                    var nowBtn = StyledButton("RESEARCH NOW (SPACE)", canPay ? ACCENT_BLUE : new Color(0.30f, 0.30f, 0.34f), () =>
                    {
                        if (rm.TryResearchFromInventory(n, inventoryRef.container))
                        {
                            RebuildCanvas();
                            RebuildDetails();
                            UpdateEraLabel();
                        }
                    });
                    nowBtn.style.minHeight = 28;
                    nowBtn.style.fontSize = 11;
                    nowBtn.SetEnabled(canPay);
                    btnRow.Add(nowBtn);
                }
                else
                {
                    var labBtn = StyledButton("START AT LAB", new Color(0.30f, 0.55f, 0.30f), () =>
                    {
                        rm.StartResearch(n);
                        RebuildDetails();
                        RebuildCanvas();
                    });
                    labBtn.style.minHeight = 28;
                    labBtn.style.fontSize = 11;
                    labBtn.style.marginRight = 8;
                    btnRow.Add(labBtn);
                }

                // Space hint
                var spaceHint = new Label("Press SPACE to research");
                spaceHint.style.color = TextMuted;
                spaceHint.style.fontSize = 10;
                spaceHint.style.marginLeft = 8;
                btnRow.Add(spaceHint);
            }

            // Unlock preview
            if (n.unlocksRecipes != null && n.unlocksRecipes.Length > 0)
            {
                var unlockRow = new VisualElement();
                unlockRow.style.flexDirection = FlexDirection.Row;
                unlockRow.style.marginTop = 4;
                _detailsPanel.Add(unlockRow);

                var unlockIcon = new Label("🔓");
                unlockIcon.style.fontSize = 10;
                unlockIcon.style.marginRight = 4;
                unlockRow.Add(unlockIcon);

                int shown = 0;
                foreach (var r in n.unlocksRecipes)
                {
                    if (r == null || shown >= 4) continue;
                    string name = r.GetName();
                    if (string.IsNullOrEmpty(name)) name = r.displayName ?? r.name;
                    var ul = new Label(name);
                    ul.style.color = ACCENT_AMBER;
                    ul.style.fontSize = 10;
                    ul.style.marginRight = 10;
                    unlockRow.Add(ul);
                    shown++;
                }
                if (n.unlocksRecipes.Length > 4)
                {
                    var more = new Label($"+{n.unlocksRecipes.Length - 4} more");
                    more.style.color = TextMuted;
                    more.style.fontSize = 10;
                    unlockRow.Add(more);
                }
            }
        }

        private void TryResearchSelected()
        {
            if (_selected == null) return;
            var rm = ResearchManager.Instance;
            if (rm == null) return;
            int rank = rm.GetRank(_selected);
            if (rank >= _selected.maxRanks) return;
            if (!rm.ArePrerequisitesMet(_selected)) return;

            if (_selected.researchSeconds <= 0f && inventoryRef != null)
            {
                if (rm.TryResearchFromInventory(_selected, inventoryRef.container))
                {
                    RebuildCanvas();
                    RebuildDetails();
                    UpdateEraLabel();
                }
            }
            else if (rm.ActiveResearch != _selected)
            {
                rm.StartResearch(_selected);
                RebuildDetails();
                RebuildCanvas();
            }
        }

        private void SetZoom(float z)
        {
            _zoom = Mathf.Clamp(z, ZOOM_MIN, ZOOM_MAX);
            if (_canvas != null)
                _canvas.style.scale = new StyleScale(new Scale(new Vector3(_zoom, _zoom, 1f)));
            if (_zoomLabel != null)
                _zoomLabel.text = $"{_zoom * 100:0}%";
        }

        private bool AllAffordable(ResearchNode n)
        {
            if (inventoryRef == null) return false;
            var rm = ResearchManager.Instance;
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                if (inventoryRef.container.CountOf(c.pack) < rm.GetEffectiveCount(n, c.count)) return false;
            }
            return true;
        }

        // ── UI Helpers ──────────────────────────────────────────────────
        private static Button StyledButton(string text, Color baseColor, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.color = new Color(0.96f, 0.96f, 0.98f);
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.fontSize = 12;
            b.style.backgroundColor = new StyleColor(baseColor);
            b.style.minHeight = 30;
            b.style.paddingLeft = 14; b.style.paddingRight = 14;
            SetRadius(b, 5);
            ZeroBorder(b);
            AddHoverEffect(b, baseColor, Color.Lerp(baseColor, Color.white, 0.18f));
            return b;
        }

        private static Button SmallIconButton(string text, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.color = new Color(0.92f, 0.94f, 0.97f);
            b.style.fontSize = 14;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.18f));
            b.style.minWidth = 28;
            b.style.minHeight = 28;
            b.style.marginRight = 2;
            SetRadius(b, 4);
            ZeroBorder(b);
            AddHoverEffect(b, new Color(0.12f, 0.14f, 0.18f), new Color(0.18f, 0.20f, 0.26f));
            return b;
        }

        private static void AddHoverEffect(VisualElement e, Color baseColor, Color hoverColor)
        {
            e.RegisterCallback<MouseEnterEvent>(_ =>
            {
                e.style.transitionProperty = new List<StylePropertyName> { "background-color", "scale" };
                e.style.transitionDuration = new List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
                e.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic), new(EasingMode.EaseOutCubic) };
                e.style.backgroundColor = new StyleColor(hoverColor);
                e.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
            });
            e.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                e.style.backgroundColor = new StyleColor(baseColor);
                e.style.scale = new StyleScale(new Scale(Vector3.one));
            });
        }

        private static void SetRadius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r;
            v.style.borderTopRightRadius = r;
            v.style.borderBottomLeftRadius = r;
            v.style.borderBottomRightRadius = r;
        }

        private static void ZeroBorder(VisualElement v)
        {
            v.style.borderTopWidth = v.style.borderBottomWidth =
            v.style.borderLeftWidth = v.style.borderRightWidth = 0;
        }
    }
}
