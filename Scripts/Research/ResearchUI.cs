// Assets/Scripts/VoxelEngine/Research/ResearchUI.cs
//
// FACTORIO-STYLE RESEARCH WINDOW.
//
// Design philosophy (per IndustrialWorld README "AI Agent System Prompt"):
//   * UI Toolkit only — no Canvas.
//   * Dark, premium-industrial palette with a single accent colour per state.
//   * Generous breathing room, large hit targets, smooth transitions.
//   * Every interactive element has Normal / Hovered / Pressed / Disabled
//     states with a 0.10s eased colour interpolation and 1.03x hover scale.
//   * Prerequisite arrows are drawn as bezier connectors with
//     generateVisualContent (no external sprites required).
//
// Layout:
//
//   ┌────────────────────────────────────────────────────────────────┐
//   │  RESEARCH                                       [ Close (Esc) ]│
//   ├──────────┬───────────────────────────────────────┬─────────────┤
//   │  Tabs    │  Tier columns w/ node cards + lines   │  Details    │
//   │          │                                       │             │
//   │ • All    │   T1  │  T2  │  T3  │  T4  │  T5      │  Cost,      │
//   │ • Log.   │  [n]──┐                                │  prereqs,   │
//   │ • Prod   │  [n]  └──[n]──[n]                     │  unlocks,   │
//   │ • Power  │                                       │  buttons    │
//   │ • Chem   │                                       │             │
//   │ • Store  │                                       │             │
//   │ • Build  │                                       │             │
//   │ • Player │                                       │             │
//   └──────────┴───────────────────────────────────────┴─────────────┘

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

        public Inventory inventoryRef;   // auto-found if null

        // ============================================================
        //                    PALETTE (premium industrial)
        // ============================================================
        private static readonly Color BG_OVERLAY    = new Color(0.03f, 0.04f, 0.06f, 0.88f);
        private static readonly Color PANEL_BG      = new Color(0.10f, 0.11f, 0.14f, 0.98f);
        private static readonly Color SUB_PANEL_BG  = new Color(0.07f, 0.08f, 0.10f, 1.00f);
        private static readonly Color CARD_BG       = new Color(0.13f, 0.15f, 0.19f, 1.00f);
        private static readonly Color CARD_BG_HOVER = new Color(0.18f, 0.21f, 0.26f, 1.00f);
        private static readonly Color CARD_BG_READY = new Color(0.16f, 0.22f, 0.28f, 1.00f);
        private static readonly Color CARD_BG_ACTIVE= new Color(0.13f, 0.30f, 0.55f, 1.00f);
        private static readonly Color CARD_BG_DONE  = new Color(0.12f, 0.34f, 0.18f, 1.00f);
        private static readonly Color CARD_BG_LOCK  = new Color(0.09f, 0.10f, 0.12f, 1.00f);
        private static readonly Color BORDER_DEFAULT= new Color(0.22f, 0.25f, 0.30f, 1.00f);
        private static readonly Color BORDER_SELECT = new Color(0.95f, 0.78f, 0.20f, 1.00f);
        private static readonly Color BORDER_DONE   = new Color(0.40f, 0.85f, 0.50f, 1.00f);
        private static readonly Color BORDER_ACTIVE = new Color(0.40f, 0.75f, 1.00f, 1.00f);
        private static readonly Color TEXT_PRIMARY  = new Color(0.96f, 0.96f, 0.98f);
        private static readonly Color TEXT_MUTED    = new Color(0.65f, 0.68f, 0.74f);
        private static readonly Color ACCENT_BLUE   = new Color(0.20f, 0.55f, 0.95f);
        private static readonly Color ACCENT_AMBER  = new Color(0.95f, 0.65f, 0.20f);
        private static readonly Color LINE_BASE     = new Color(0.30f, 0.34f, 0.40f, 1.00f);
        private static readonly Color LINE_DONE     = new Color(0.40f, 0.85f, 0.50f, 1.00f);

        // ============================================================
        //                          STATE
        // ============================================================
        private UIDocument    _doc;
        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _tabsBar;
        private VisualElement _treeArea;     // host for node cards + prereq lines
        private VisualElement _connectors;   // draws prereq arrows
        private VisualElement _details;
        private TextField     _searchField;

        // For live progress updates while researching.
        private VisualElement _progressFill;
        private Label         _progressLabel;

        private bool             _open;
        private ResearchNode     _selected;
        private ResearchCategory _activeTab    = ResearchCategory.Environment;
        private ResearchSubCategory? _activeSub = null;   // null = "All"
        private string           _searchQuery  = string.Empty;

        // Card geometry, must match BuildNodeCard().
        private const float NODE_W       = 200f;
        private const float NODE_H       = 120f;
        private const float NODE_GAP_Y   = 18f;
        private const float TIER_GAP_X   = 90f;
        private const float TREE_PAD     = 28f;

        // Maps node -> its position inside the tree area (used by connector drawing).
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
            if (GameSettings.WasPressed(InputAction.Research))
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
            if (_open && _selected != null && ResearchManager.Instance != null
                && ResearchManager.Instance.ActiveResearch == _selected)
            {
                if (_progressFill != null)
                    _progressFill.style.width = new StyleLength(
                        new Length(Mathf.Clamp01(ResearchManager.Instance.ActiveProgress01) * 100, LengthUnit.Percent));
                if (_progressLabel != null)
                    _progressLabel.text = ResearchManager.Instance.ActiveHasCost
                        ? $"{ResearchManager.Instance.ActiveProgress01 * 100:0}%  researched"
                        : "Waiting for science packs at a Research Lab…";
            }
        }

        // ============================================================
        //                       OPEN / CLOSE
        // ============================================================
        public void Open()
        {
            if (ResearchManager.Instance == null || ResearchManager.Instance.tree == null) return;
            if (inventoryRef == null) inventoryRef = FindAnyObjectByType<Inventory>();
            _open = true;
            VoxelEngine.UI.UIState.PushBlock();
            Build();
            AnimateOpen();
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            VoxelEngine.UI.UIState.PopBlock();
            HideUI();
        }

        private void HideUI()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
            _panel = null; _treeArea = null; _connectors = null; _details = null;
            _progressFill = null; _progressLabel = null;
            _nodeRects.Clear();
        }

        private void AnimateOpen()
        {
            if (_panel == null) return;
            // Fade-in & scale-up using UI Toolkit transitions.
            _panel.style.opacity = 0f;
            _panel.style.scale   = new StyleScale(new Scale(new Vector3(0.97f, 0.97f, 1f)));
            _panel.schedule.Execute(() =>
            {
                _panel.style.transitionProperty   = new List<StylePropertyName> { "opacity", "scale" };
                _panel.style.transitionDuration   = new List<TimeValue> { new TimeValue(0.18f, TimeUnit.Second), new TimeValue(0.18f, TimeUnit.Second) };
                _panel.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic), new EasingFunction(EasingMode.EaseOutCubic) };
                _panel.style.opacity = 1f;
                _panel.style.scale   = new StyleScale(new Scale(Vector3.one));
            }).StartingIn(10);
        }

        // ============================================================
        //                       BUILD UI
        // ============================================================
        private void Build()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(BG_OVERLAY);
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;
            _nodeRects.Clear();

            _panel = new VisualElement();
            _panel.style.width  = 1320;
            _panel.style.height = 800;
            _panel.style.paddingTop = 20; _panel.style.paddingBottom = 20;
            _panel.style.paddingLeft = 24; _panel.style.paddingRight = 24;
            _panel.style.backgroundColor = new StyleColor(PANEL_BG);
            _panel.style.borderTopWidth = _panel.style.borderBottomWidth =
            _panel.style.borderLeftWidth = _panel.style.borderRightWidth = 1;
            var border = new StyleColor(new Color(0.20f, 0.23f, 0.28f));
            _panel.style.borderTopColor = _panel.style.borderBottomColor =
            _panel.style.borderLeftColor = _panel.style.borderRightColor = border;
            SetRadius(_panel, 10);
            _root.Add(_panel);

            BuildHeader(_panel);
            BuildBody(_panel);
        }

        // ─────────── HEADER ───────────
        private void BuildHeader(VisualElement parent)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems    = Align.Center;
            header.style.marginBottom  = 14;
            parent.Add(header);

            // Title with accent bar.
            var accent = new VisualElement();
            accent.style.width = 4; accent.style.height = 28;
            accent.style.backgroundColor = new StyleColor(ACCENT_AMBER);
            accent.style.marginRight = 12;
            SetRadius(accent, 2);
            header.Add(accent);

            var title = new Label("RESEARCH");
            title.style.color = TEXT_PRIMARY;
            title.style.fontSize = 22;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 4;
            header.Add(title);

            // Spacer.
            var spacer = new VisualElement(); spacer.style.flexGrow = 1; header.Add(spacer);

            // Search field.
            _searchField = new TextField();
            _searchField.style.width = 260;
            _searchField.style.marginRight = 10;
            var sfInput = _searchField.Q(TextField.textInputUssName);
            if (sfInput != null)
            {
                sfInput.style.backgroundColor = new StyleColor(SUB_PANEL_BG);
                sfInput.style.color = TEXT_PRIMARY;
                sfInput.style.unityTextAlign = TextAnchor.MiddleLeft;
                sfInput.style.paddingLeft = 10; sfInput.style.paddingRight = 10;
                SetRadius(sfInput, 4);
                sfInput.style.borderLeftWidth = sfInput.style.borderRightWidth =
                sfInput.style.borderTopWidth  = sfInput.style.borderBottomWidth = 0;
            }
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchQuery = (evt.newValue ?? string.Empty).Trim();
                RebuildTree();
            });
            _searchField.label = string.Empty;
            // Use placeholder text via watermark on focus loss.
            var sfLabel = _searchField.Q<Label>();
            if (sfLabel != null) sfLabel.style.display = DisplayStyle.None;
            header.Add(WrapWithLabel(_searchField, "Search"));

            // Close button.
            var close = StyledButton("Close (Esc)", new Color(0.55f, 0.22f, 0.22f), Close);
            close.style.minHeight = 34; close.style.minWidth = 130;
            header.Add(close);
        }

        private static VisualElement WrapWithLabel(VisualElement inner, string placeholder)
        {
            var w = new VisualElement();
            w.style.flexDirection = FlexDirection.Row;
            w.style.alignItems = Align.Center;
            var lbl = new Label("🔍  " + placeholder);
            lbl.style.color = TEXT_MUTED;
            lbl.style.fontSize = 11;
            lbl.style.marginRight = 6;
            w.Add(lbl);
            w.Add(inner);
            return w;
        }

        // ─────────── BODY ───────────
        private void BuildBody(VisualElement parent)
        {
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.flexGrow = 1;
            parent.Add(body);

            BuildTabsColumn(body);
            BuildTreeColumn(body);
            BuildDetailsColumn(body);
        }

        // ─────────── LEFT TABS ───────────
        private static readonly (ResearchSubCategory? sub, string label, string emoji, Color tint)[] SUB_TABS =
        {
            (null,                              "All",          "✦",   new Color(0.85f, 0.85f, 0.90f)),
            (ResearchSubCategory.Logistics,     "Logistics",    "⇄",   new Color(0.40f, 0.85f, 0.95f)),
            (ResearchSubCategory.Production,    "Production",   "⚙",   new Color(0.95f, 0.65f, 0.25f)),
            (ResearchSubCategory.Power,         "Power",        "⚡",  new Color(0.95f, 0.85f, 0.30f)),
            (ResearchSubCategory.Chemistry,     "Chemistry",    "⚗",   new Color(0.50f, 0.85f, 0.45f)),
            (ResearchSubCategory.Storage,       "Storage",      "▣",   new Color(0.55f, 0.65f, 0.95f)),
            (ResearchSubCategory.Building,      "Building",     "▤",   new Color(0.85f, 0.70f, 0.45f)),
            (ResearchSubCategory.Military,      "Military",     "✚",   new Color(0.90f, 0.40f, 0.40f)),
        };

        private void BuildTabsColumn(VisualElement parent)
        {
            var col = new VisualElement();
            col.style.width = 170;
            col.style.marginRight = 12;
            col.style.flexDirection = FlexDirection.Column;
            parent.Add(col);

            // Top: top-level category switch (Environment / Player Upgrades).
            col.Add(TopTab("Environment",    ResearchCategory.Environment));
            col.Add(TopTab("Player Upgrades",ResearchCategory.PlayerUpgrades));

            // Divider.
            var div = new VisualElement();
            div.style.height = 1;
            div.style.backgroundColor = new StyleColor(new Color(0.18f, 0.20f, 0.24f));
            div.style.marginTop = 10; div.style.marginBottom = 10;
            col.Add(div);

            // Sub-category quick filters (only meaningful for Environment).
            if (_activeTab == ResearchCategory.Environment)
            {
                var hint = new Label("FILTER");
                hint.style.color = TEXT_MUTED;
                hint.style.fontSize = 10;
                hint.style.letterSpacing = 3;
                hint.style.marginBottom = 6;
                col.Add(hint);

                foreach (var t in SUB_TABS)
                    col.Add(SubTab(t.label, t.emoji, t.tint, t.sub));
            }
        }

        private Button TopTab(string label, ResearchCategory cat)
        {
            bool active = (_activeTab == cat);
            var b = new Button(() =>
            {
                if (_activeTab == cat) return;
                _activeTab = cat;
                _activeSub = null;
                _selected  = null;
                Build();
            }) { text = label };
            b.style.minHeight = 36;
            b.style.marginTop = 4; b.style.marginBottom = 0;
            b.style.color = active ? TEXT_PRIMARY : TEXT_MUTED;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.fontSize = 13;
            b.style.backgroundColor = new StyleColor(active ? ACCENT_BLUE : CARD_BG);
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingLeft = 12;
            SetRadius(b, 5);
            ZeroBorder(b);
            AddHoverEffect(b, active ? ACCENT_BLUE : CARD_BG, active ? ACCENT_BLUE : CARD_BG_HOVER);
            return b;
        }

        private Button SubTab(string label, string emoji, Color tint, ResearchSubCategory? sub)
        {
            bool active = (_activeSub == sub);
            var b = new Button(() =>
            {
                _activeSub = sub;
                _selected  = null;
                RebuildTree();
                RebuildTabsColumn();
            }) { text = $"  {emoji}   {label}" };
            b.style.minHeight = 32;
            b.style.marginTop = 3; b.style.marginBottom = 0;
            b.style.color = active ? TEXT_PRIMARY : TEXT_MUTED;
            b.style.fontSize = 12;
            b.style.backgroundColor = new StyleColor(active ? CARD_BG_HOVER : CARD_BG);
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.paddingLeft = 10;
            SetRadius(b, 4);
            ZeroBorder(b);
            if (active)
            {
                b.style.borderLeftWidth = 3;
                b.style.borderLeftColor = new StyleColor(tint);
            }
            AddHoverEffect(b, active ? CARD_BG_HOVER : CARD_BG, CARD_BG_HOVER);
            return b;
        }

        private void RebuildTabsColumn()
        {
            // Cheap full rebuild — kept simple, layout is tiny.
            Build();
        }

        // ─────────── CENTER TREE ───────────
        private void BuildTreeColumn(VisualElement parent)
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.marginRight = 12;
            col.style.backgroundColor = new StyleColor(SUB_PANEL_BG);
            SetRadius(col, 6);
            parent.Add(col);

            var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            scroll.style.flexGrow = 1;
            col.Add(scroll);

            _treeArea = new VisualElement();
            _treeArea.style.paddingTop = TREE_PAD;
            _treeArea.style.paddingBottom = TREE_PAD;
            _treeArea.style.paddingLeft = TREE_PAD;
            _treeArea.style.paddingRight = TREE_PAD;
            // We use absolute positioning for cards so we can draw connector lines under them.
            scroll.Add(_treeArea);

            // Connector layer (drawn under cards). Same parent so it shares the coord system.
            _connectors = new VisualElement();
            _connectors.style.position = Position.Absolute;
            _connectors.style.left = 0; _connectors.style.top = 0;
            _connectors.style.right = 0; _connectors.style.bottom = 0;
            _connectors.pickingMode = PickingMode.Ignore;
            _connectors.generateVisualContent += DrawConnectors;
            _treeArea.Add(_connectors);

            RebuildTree();
        }

        private void RebuildTree()
        {
            if (_treeArea == null) return;

            // Remove old cards (keep connector overlay).
            for (int i = _treeArea.childCount - 1; i >= 0; i--)
            {
                var c = _treeArea[i];
                if (c == _connectors) continue;
                _treeArea.RemoveAt(i);
            }
            _nodeRects.Clear();

            // Filter nodes.
            var nodes = new List<ResearchNode>();
            foreach (var n in ResearchManager.Instance.tree.nodes)
            {
                if (n == null) continue;
                if (n.category != _activeTab) continue;
                if (_activeTab == ResearchCategory.Environment && _activeSub.HasValue
                    && n.subCategory != _activeSub.Value) continue;
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
                var empty = new Label("No research in this filter yet.");
                empty.style.position = Position.Absolute;
                empty.style.left = 40; empty.style.top = 40;
                empty.style.color = TEXT_MUTED;
                empty.style.fontSize = 13;
                _treeArea.Add(empty);
                _treeArea.style.width = 600;
                _treeArea.style.height = 200;
                _connectors.MarkDirtyRepaint();
                return;
            }

            // Group by tier then sort by column.
            var byTier = new SortedDictionary<int, List<ResearchNode>>();
            foreach (var n in nodes)
            {
                int t = Mathf.Clamp(n.tier, 1, 6);
                if (!byTier.TryGetValue(t, out var list)) byTier[t] = list = new List<ResearchNode>();
                list.Add(n);
            }

            // Tier labels at the top.
            float xCursor = 0f;
            float maxH = 0f;
            foreach (var kv in byTier)
            {
                kv.Value.Sort((a, b) => a.column.CompareTo(b.column));

                var tierLbl = new Label("TIER " + kv.Key);
                tierLbl.style.position = Position.Absolute;
                tierLbl.style.left = xCursor + (NODE_W * 0.5f) - 24;
                tierLbl.style.top  = 0;
                tierLbl.style.color = TEXT_MUTED;
                tierLbl.style.fontSize = 11;
                tierLbl.style.letterSpacing = 3;
                tierLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                _treeArea.Add(tierLbl);

                float yCursor = 28f;
                foreach (var n in kv.Value)
                {
                    var card = BuildNodeCard(n);
                    card.style.position = Position.Absolute;
                    card.style.left = xCursor;
                    card.style.top  = yCursor;
                    _treeArea.Add(card);
                    _nodeRects[n] = new Rect(xCursor, yCursor, NODE_W, NODE_H);
                    yCursor += NODE_H + NODE_GAP_Y;
                }
                if (yCursor > maxH) maxH = yCursor;
                xCursor += NODE_W + TIER_GAP_X;
            }

            _treeArea.style.width  = xCursor + 20;
            _treeArea.style.height = maxH + 20;

            _connectors.MarkDirtyRepaint();
        }

        // Draws bezier prereq lines between node cards.
        private void DrawConnectors(MeshGenerationContext mgc)
        {
            if (_nodeRects.Count == 0) return;
            var painter = mgc.painter2D;
            painter.lineWidth = 2f;
            painter.lineCap   = LineCap.Round;
            painter.lineJoin  = LineJoin.Round;

            var rm = ResearchManager.Instance;

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
                    bool unlocked = rm != null && rm.IsUnlocked(p);

                    painter.strokeColor = unlocked ? LINE_DONE : LINE_BASE;
                    painter.BeginPath();
                    painter.MoveTo(fromP);
                    // Bezier with horizontal control points for a smooth tech-tree curve.
                    float midX = (fromP.x + toP.x) * 0.5f;
                    painter.BezierCurveTo(new Vector2(midX, fromP.y),
                                          new Vector2(midX, toP.y),
                                          toP);
                    painter.Stroke();

                    // Arrowhead.
                    Vector2 dir = (toP - new Vector2(midX, toP.y)).normalized;
                    if (dir.sqrMagnitude < 0.001f) dir = Vector2.right;
                    Vector2 left  = new Vector2(-dir.y, dir.x);
                    Vector2 tip   = toP;
                    Vector2 back  = toP - dir * 8f;
                    painter.fillColor = unlocked ? LINE_DONE : LINE_BASE;
                    painter.BeginPath();
                    painter.MoveTo(tip);
                    painter.LineTo(back + left * 4f);
                    painter.LineTo(back - left * 4f);
                    painter.ClosePath();
                    painter.Fill();
                }
            }
        }

        // ─────────── NODE CARD ───────────
        private VisualElement BuildNodeCard(ResearchNode n)
        {
            var rm = ResearchManager.Instance;
            int  rank   = rm.GetRank(n);
            bool maxed  = rank >= n.maxRanks;
            bool ready  = rm.ArePrerequisitesMet(n);
            bool active = rm.ActiveResearch == n;
            bool sel    = _selected == n;

            Color bg = maxed ? CARD_BG_DONE
                     : active ? CARD_BG_ACTIVE
                     : ready  ? (rank > 0 ? CARD_BG_READY : CARD_BG)
                     :          CARD_BG_LOCK;
            Color borderC = sel    ? BORDER_SELECT
                         : maxed   ? BORDER_DONE
                         : active  ? BORDER_ACTIVE
                         :          BORDER_DEFAULT;

            var card = new VisualElement();
            card.style.width  = NODE_W;
            card.style.height = NODE_H;
            card.style.paddingTop = 10; card.style.paddingBottom = 10;
            card.style.paddingLeft = 12; card.style.paddingRight = 12;
            card.style.backgroundColor = new StyleColor(bg);
            card.style.borderTopWidth = card.style.borderBottomWidth =
            card.style.borderLeftWidth = card.style.borderRightWidth = 2;
            var sc = new StyleColor(borderC);
            card.style.borderTopColor = card.style.borderBottomColor =
            card.style.borderLeftColor = card.style.borderRightColor = sc;
            SetRadius(card, 7);

            // Top row: tinted icon swatch + title.
            var topRow = new VisualElement();
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.alignItems = Align.Center;
            card.Add(topRow);

            var iconSwatch = new VisualElement();
            iconSwatch.style.width = 28; iconSwatch.style.height = 28;
            iconSwatch.style.backgroundColor = new StyleColor(n.iconTint);
            iconSwatch.style.marginRight = 8;
            SetRadius(iconSwatch, 4);
            iconSwatch.style.borderLeftWidth = iconSwatch.style.borderRightWidth =
            iconSwatch.style.borderTopWidth  = iconSwatch.style.borderBottomWidth = 1;
            var swBorder = new StyleColor(new Color(0, 0, 0, 0.35f));
            iconSwatch.style.borderLeftColor = iconSwatch.style.borderRightColor =
            iconSwatch.style.borderTopColor  = iconSwatch.style.borderBottomColor = swBorder;
            iconSwatch.pickingMode = PickingMode.Ignore;
            topRow.Add(iconSwatch);

            var name = new Label(n.displayName);
            name.style.color = TEXT_PRIMARY;
            name.style.fontSize = 13;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.flexGrow = 1;
            name.pickingMode = PickingMode.Ignore;
            topRow.Add(name);

            // Middle: science-pack cost row.
            if (n.cost != null && n.cost.Length > 0)
            {
                var costRow = new VisualElement();
                costRow.style.flexDirection = FlexDirection.Row;
                costRow.style.marginTop = 8;
                costRow.style.flexWrap = Wrap.Wrap;
                card.Add(costRow);
                foreach (var c in n.cost)
                {
                    if (c.pack == null || c.count <= 0) continue;
                    int eff = rm.GetEffectiveCount(n, c.count);
                    costRow.Add(SciencePackIcon(c.pack, eff));
                }
            }

            // Bottom status strip.
            var spacer = new VisualElement(); spacer.style.flexGrow = 1;
            spacer.pickingMode = PickingMode.Ignore;
            card.Add(spacer);

            string statusLine;
            Color  statusCol = TEXT_MUTED;
            if (n.IsRepeatable)
            { statusLine = $"Rank {rank} / {n.maxRanks}"; statusCol = rank > 0 ? ACCENT_AMBER : TEXT_MUTED; }
            else if (maxed)  { statusLine = "✓ Researched";       statusCol = new Color(0.6f, 0.9f, 0.6f); }
            else if (active) { statusLine = "● Researching…";     statusCol = new Color(0.5f, 0.8f, 1.0f); }
            else if (ready)  { statusLine = "Available";          statusCol = TEXT_PRIMARY; }
            else             { statusLine = "🔒 Locked";          statusCol = TEXT_MUTED; }

            var st = new Label(statusLine);
            st.style.color = statusCol;
            st.style.fontSize = 11;
            st.pickingMode = PickingMode.Ignore;
            card.Add(st);

            // Interaction.
            card.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (sel) return;
                card.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
                card.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
                card.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
                card.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic), new EasingFunction(EasingMode.EaseOutCubic) };
                card.style.backgroundColor = new StyleColor(Color.Lerp(bg, CARD_BG_HOVER, 0.4f));
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
                RebuildTree();
                RebuildDetails();
                e.StopPropagation();
            });
            return card;
        }

        private VisualElement SciencePackIcon(ScienceItem pack, int count)
        {
            var w = new VisualElement();
            w.style.flexDirection = FlexDirection.Row;
            w.style.alignItems = Align.Center;
            w.style.marginRight = 6;
            w.style.marginTop = 2;
            w.pickingMode = PickingMode.Ignore;

            var icon = new VisualElement();
            icon.style.width = 14; icon.style.height = 14;
            icon.style.backgroundColor = new StyleColor(pack.iconTint);
            icon.style.marginRight = 3;
            SetRadius(icon, 7);
            icon.style.borderLeftWidth = icon.style.borderRightWidth =
            icon.style.borderTopWidth  = icon.style.borderBottomWidth = 1;
            var swBorder = new StyleColor(new Color(0, 0, 0, 0.4f));
            icon.style.borderLeftColor = icon.style.borderRightColor =
            icon.style.borderTopColor  = icon.style.borderBottomColor = swBorder;
            w.Add(icon);

            var have = inventoryRef != null ? inventoryRef.container.CountOf(pack) : 0;
            var lbl = new Label($"{Mathf.Min(have, count)}/{count}");
            lbl.style.color = have >= count ? new Color(0.7f, 0.95f, 0.7f) : TEXT_MUTED;
            lbl.style.fontSize = 10;
            w.Add(lbl);
            return w;
        }

        // ─────────── RIGHT DETAILS ───────────
        private void BuildDetailsColumn(VisualElement parent)
        {
            _details = new VisualElement();
            _details.style.width = 380;
            _details.style.paddingTop = 18; _details.style.paddingBottom = 18;
            _details.style.paddingLeft = 18; _details.style.paddingRight = 18;
            _details.style.backgroundColor = new StyleColor(SUB_PANEL_BG);
            SetRadius(_details, 6);
            parent.Add(_details);
            RebuildDetails();
        }

        private void RebuildDetails()
        {
            if (_details == null) return;
            _details.Clear();
            _progressFill = null; _progressLabel = null;

            if (_selected == null)
            {
                var hint = new Label("Click a tech node to see its details.");
                hint.style.color = TEXT_MUTED;
                hint.style.fontSize = 12;
                hint.style.whiteSpace = WhiteSpace.Normal;
                _details.Add(hint);
                return;
            }
            var n  = _selected;
            var rm = ResearchManager.Instance;
            int  rank  = rm.GetRank(n);
            bool maxed = rank >= n.maxRanks;

            // Header swatch + name.
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row;
            head.style.alignItems = Align.Center;
            _details.Add(head);

            var swatch = new VisualElement();
            swatch.style.width = 40; swatch.style.height = 40;
            swatch.style.backgroundColor = new StyleColor(n.iconTint);
            SetRadius(swatch, 5);
            swatch.style.marginRight = 12;
            head.Add(swatch);

            var nameCol = new VisualElement();
            nameCol.style.flexGrow = 1;
            head.Add(nameCol);
            var nameLbl = new Label(n.displayName);
            nameLbl.style.color = TEXT_PRIMARY;
            nameLbl.style.fontSize = 18;
            nameLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameCol.Add(nameLbl);

            var tierLbl = new Label($"Tier {n.tier}" +
                (n.IsRepeatable ? $"  •  Rank {rank}/{n.maxRanks}" : "") +
                (n.category == ResearchCategory.Environment ? $"  •  {n.subCategory}" : ""));
            tierLbl.style.color = TEXT_MUTED;
            tierLbl.style.fontSize = 11;
            nameCol.Add(tierLbl);

            // Divider.
            DetailDivider();

            // Description.
            var desc = new Label(string.IsNullOrEmpty(n.description) ? "(no description)" : n.description);
            desc.style.color = new Color(0.85f, 0.87f, 0.92f);
            desc.style.fontSize = 12;
            desc.style.whiteSpace = WhiteSpace.Normal;
            _details.Add(desc);

            // Prereqs.
            if (n.prerequisites != null && n.prerequisites.Length > 0)
            {
                DetailHeader("PREREQUISITES");
                foreach (var p in n.prerequisites)
                {
                    if (p == null) continue;
                    string mark = rm.IsUnlocked(p) ? "<color=#7bd57b>✓</color>" : "<color=#d57b7b>✗</color>";
                    var l = new Label($"  {mark}  {p.displayName}");
                    l.enableRichText = true;
                    l.style.color = new Color(0.85f, 0.88f, 0.92f);
                    l.style.fontSize = 11;
                    _details.Add(l);
                }
            }

            // Cost.
            if (n.cost != null && n.cost.Length > 0)
            {
                DetailHeader("COST");
                foreach (var c in n.cost)
                {
                    if (c.pack == null) continue;
                    int eff = rm.GetEffectiveCount(n, c.count);
                    int have = inventoryRef != null ? inventoryRef.container.CountOf(c.pack) : 0;

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 3;

                    var icon = new VisualElement();
                    icon.style.width = 16; icon.style.height = 16;
                    icon.style.backgroundColor = new StyleColor(c.pack.iconTint);
                    SetRadius(icon, 8);
                    icon.style.marginRight = 8;
                    row.Add(icon);

                    var col = have >= eff ? new Color(0.55f, 0.92f, 0.55f) : new Color(0.95f, 0.55f, 0.55f);
                    var l = new Label($"{have} / {eff}   {c.pack.displayName}");
                    l.style.color = col;
                    l.style.fontSize = 12;
                    row.Add(l);
                    _details.Add(row);
                }
                var tl = new Label(n.researchSeconds > 0
                    ? $"  Lab time: {n.researchSeconds:0}s"
                    : "  Instant (paid from inventory)");
                tl.style.color = TEXT_MUTED;
                tl.style.fontSize = 10;
                tl.style.marginTop = 4;
                _details.Add(tl);
            }

            // Player upgrade effect.
            if (n.category == ResearchCategory.PlayerUpgrades && n.upgradeKind != PlayerUpgradeKind.None)
            {
                DetailHeader("EFFECT (PER RANK)");
                var l = new Label("  " + DescribeEffect(n));
                l.style.color = ACCENT_AMBER;
                l.style.fontSize = 12;
                l.style.unityFontStyleAndWeight = FontStyle.Bold;
                _details.Add(l);
            }

            // Unlocks.
            if (n.unlocksRecipes != null && n.unlocksRecipes.Length > 0)
            {
                DetailHeader("UNLOCKS");
                foreach (var r in n.unlocksRecipes)
                {
                    if (r == null) continue;
                    var l = new Label($"  • {r.GetName()}");
                    l.style.color = new Color(0.85f, 0.88f, 0.92f);
                    l.style.fontSize = 11;
                    _details.Add(l);
                }
            }

            // Active progress bar.
            if (rm.ActiveResearch == n)
            {
                var barBg = new VisualElement();
                barBg.style.marginTop = 16;
                barBg.style.height = 14;
                barBg.style.backgroundColor = new StyleColor(new Color(0.06f, 0.07f, 0.09f));
                SetRadius(barBg, 7);
                barBg.style.borderLeftWidth = barBg.style.borderRightWidth =
                barBg.style.borderTopWidth  = barBg.style.borderBottomWidth = 1;
                var bbc = new StyleColor(new Color(0.20f, 0.23f, 0.28f));
                barBg.style.borderLeftColor = barBg.style.borderRightColor =
                barBg.style.borderTopColor  = barBg.style.borderBottomColor = bbc;
                _details.Add(barBg);
                var fill = new VisualElement();
                fill.style.height = 12;
                fill.style.width = new StyleLength(new Length(rm.ActiveProgress01 * 100, LengthUnit.Percent));
                fill.style.backgroundColor = new StyleColor(ACCENT_BLUE);
                SetRadius(fill, 6);
                barBg.Add(fill);
                _progressFill = fill;

                var pl = new Label(rm.ActiveHasCost
                    ? $"{rm.ActiveProgress01 * 100:0}%  researched"
                    : "Waiting for science packs at a Research Lab…");
                pl.style.color = new Color(0.85f, 0.88f, 0.92f);
                pl.style.fontSize = 11;
                pl.style.marginTop = 4;
                _details.Add(pl);
                _progressLabel = pl;
            }

            // Action button row.
            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.marginTop = 18;
            _details.Add(btnRow);

            if (maxed)
            {
                var done = new Label("✓ Maxed out");
                done.style.color = new Color(0.55f, 0.92f, 0.55f);
                done.style.fontSize = 13;
                done.style.unityFontStyleAndWeight = FontStyle.Bold;
                btnRow.Add(done);
            }
            else if (rm.ActiveResearch == n)
            {
                var cancelBtn = StyledButton("CANCEL", new Color(0.55f, 0.22f, 0.22f),
                    () => { rm.CancelResearch(); RebuildDetails(); RebuildTree(); });
                cancelBtn.style.flexGrow = 1; cancelBtn.style.minHeight = 36;
                btnRow.Add(cancelBtn);
            }
            else if (!rm.ArePrerequisitesMet(n))
            {
                var blocked = new Label("✗ Prerequisites not met");
                blocked.style.color = new Color(0.92f, 0.45f, 0.45f);
                blocked.style.fontSize = 12;
                btnRow.Add(blocked);
            }
            else
            {
                bool canInventory = n.researchSeconds <= 0f;
                bool canPay = inventoryRef != null && AllAffordable(n);
                if (canInventory)
                {
                    var nowBtn = StyledButton("RESEARCH NOW",
                        canPay ? ACCENT_BLUE : new Color(0.30f, 0.30f, 0.34f),
                        () => { if (rm.TryResearchFromInventory(n, inventoryRef.container)) { RebuildTree(); RebuildDetails(); } });
                    nowBtn.style.flexGrow = 1; nowBtn.style.minHeight = 36;
                    nowBtn.SetEnabled(canPay);
                    btnRow.Add(nowBtn);
                }
                else
                {
                    var labBtn = StyledButton("START AT RESEARCH LAB",
                        new Color(0.30f, 0.55f, 0.30f),
                        () => { rm.StartResearch(n); RebuildDetails(); RebuildTree(); });
                    labBtn.style.flexGrow = 1; labBtn.style.minHeight = 36;
                    btnRow.Add(labBtn);

                    var hint = new Label("Requires a Research Lab. Drop science packs into the lab's input slots.");
                    hint.style.color = TEXT_MUTED;
                    hint.style.fontSize = 10;
                    hint.style.marginTop = 8;
                    hint.style.whiteSpace = WhiteSpace.Normal;
                    _details.Add(hint);
                }
            }
        }

        private void DetailHeader(string text)
        {
            var hdr = new Label(text);
            hdr.style.color = TEXT_MUTED;
            hdr.style.fontSize = 10;
            hdr.style.letterSpacing = 3;
            hdr.style.marginTop = 14; hdr.style.marginBottom = 6;
            hdr.style.unityFontStyleAndWeight = FontStyle.Bold;
            _details.Add(hdr);
        }

        private void DetailDivider()
        {
            var d = new VisualElement();
            d.style.height = 1;
            d.style.marginTop = 12; d.style.marginBottom = 12;
            d.style.backgroundColor = new StyleColor(new Color(0.18f, 0.20f, 0.24f));
            _details.Add(d);
        }

        // ============================================================
        //                          HELPERS
        // ============================================================
        private bool AllAffordable(ResearchNode n)
        {
            if (inventoryRef == null) return false;
            var rm = ResearchManager.Instance;
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = rm.GetEffectiveCount(n, c.count);
                if (inventoryRef.container.CountOf(c.pack) < need) return false;
            }
            return true;
        }

        private static string DescribeEffect(ResearchNode n)
        {
            switch (n.upgradeKind)
            {
                case PlayerUpgradeKind.BonusMaxHealth:        return $"+{n.upgradePerRankAmount:0} max HP";
                case PlayerUpgradeKind.BonusInventorySlots:   return $"+{n.upgradePerRankAmount:0} backpack slots";
                case PlayerUpgradeKind.BonusDamage:           return $"+{n.upgradePerRankAmount:0} damage";
                case PlayerUpgradeKind.BonusMaxStamina:       return $"+{n.upgradePerRankAmount:0} max stamina";
                case PlayerUpgradeKind.BonusSprintMultiplier: return $"+{n.upgradePerRankAmount:0.00} sprint speed (cap 5x)";
                case PlayerUpgradeKind.UnlockFlight:          return "Unlocks permanent flight";
                default: return "(no effect)";
            }
        }

        private static Button StyledButton(string text, Color baseColor, System.Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.style.color = TEXT_PRIMARY;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.fontSize = 13;
            b.style.backgroundColor = new StyleColor(baseColor);
            b.style.minHeight = 34;
            b.style.marginLeft = 0; b.style.marginRight = 0;
            b.style.marginTop = 0; b.style.marginBottom = 0;
            SetRadius(b, 5);
            ZeroBorder(b);
            AddHoverEffect(b, baseColor, Color.Lerp(baseColor, Color.white, 0.18f));
            return b;
        }

        private static void AddHoverEffect(VisualElement b, Color baseColor, Color hoverColor)
        {
            b.RegisterCallback<MouseEnterEvent>(_ =>
            {
                b.style.transitionProperty = new List<StylePropertyName> { "background-color", "scale" };
                b.style.transitionDuration = new List<TimeValue> { new TimeValue(0.10f, TimeUnit.Second), new TimeValue(0.10f, TimeUnit.Second) };
                b.style.transitionTimingFunction = new List<EasingFunction> { new EasingFunction(EasingMode.EaseOutCubic), new EasingFunction(EasingMode.EaseOutCubic) };
                b.style.backgroundColor = new StyleColor(hoverColor);
                b.style.scale = new StyleScale(new Scale(new Vector3(1.03f, 1.03f, 1f)));
            });
            b.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                b.style.backgroundColor = new StyleColor(baseColor);
                b.style.scale = new StyleScale(new Scale(Vector3.one));
            });
        }

        private static void SetRadius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius     = r;
            v.style.borderTopRightRadius    = r;
            v.style.borderBottomLeftRadius  = r;
            v.style.borderBottomRightRadius = r;
        }
        private static void ZeroBorder(VisualElement v)
        {
            v.style.borderTopWidth = v.style.borderBottomWidth =
            v.style.borderLeftWidth = v.style.borderRightWidth = 0;
        }
    }
}
