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
//   │ Branch   │  ∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞∞  │
//   │ filter   │  (pan/zoom canvas with tier labels, node cards, glowing   │
//   │ • ALL 42 │   bezier connectors, era banners)                        │
//   │ • Log. 9 │                                                          │
//   │ • Prod  6│    Era 1: Mechanized                                     │
//   │ • Power  │     Tier 1        Tier 2        Tier 3                   │
//   │ • Chem.  │    [Card]─ ─ ─ ─[Card]─ ─ ─ ─[Card]                     │
//   │ • Store  │      │                     │                             │
//   │ • Mil.   │    [Card]                 [Card]                         │
//   │ • Player │                                                          │
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
        private static readonly Color BG_OVERLAY       = new(0.015f, 0.025f, 0.045f, 0.90f);
        private static readonly Color PANEL_BG         = new(0.045f, 0.065f, 0.09f, 0.99f);
        private static readonly Color CANVAS_BG        = new(0.025f, 0.045f, 0.065f, 1.00f);
        private static readonly Color CARD_BG          = new(0.075f, 0.105f, 0.135f, 1.00f);
        private static readonly Color CARD_HOVER       = new(0.105f, 0.15f, 0.19f, 1.00f);
        private static readonly Color CARD_READY       = new(0.075f, 0.17f, 0.21f, 1.00f);
        private static readonly Color CARD_ACTIVE      = new(0.065f, 0.20f, 0.28f, 1.00f);
        private static readonly Color CARD_DONE        = new(0.065f, 0.17f, 0.12f, 1.00f);
        private static readonly Color CARD_LOCKED      = new(0.055f, 0.075f, 0.10f, 1.00f);
        private static readonly Color BORDER_DEFAULT   = new(0.16f, 0.23f, 0.28f, 1.00f);
        private static readonly Color BORDER_SELECT    = new(1.00f, 0.70f, 0.22f, 1.00f);
        private static readonly Color BORDER_DONE      = new(0.30f, 0.82f, 0.57f, 1.00f);
        private static readonly Color BORDER_ACTIVE    = new(0.25f, 0.82f, 0.95f, 1.00f);
        private static readonly Color ACCENT_BLUE      = new(0.15f, 0.76f, 0.91f);
        private static readonly Color ACCENT_AMBER     = new(1.00f, 0.66f, 0.22f);
        private static readonly Color ACCENT_GREEN     = new(0.32f, 0.86f, 0.58f);
        private static readonly Color LINE_BASE        = new(0.23f, 0.34f, 0.39f, 0.72f);
        private static readonly Color LINE_READY       = new(0.18f, 0.84f, 0.98f, 0.90f);
        private static readonly Color LINE_DONE        = new(0.30f, 0.82f, 0.57f, 1.00f);
        private static readonly Color GLOW_READY       = new(0.10f, 0.72f, 0.90f, 0.16f);
        private static Color TextPrimary  => new(0.94f, 0.97f, 0.99f);
        private static Color TextMuted    => new(0.58f, 0.68f, 0.73f);

        // ── State ───────────────────────────────────────────────────────
        private UIDocument    _doc;
        private VisualElement _root;
        private VisualElement _panel;
        private VisualElement _canvas;          // the zoomable/pannable tree surface
        private VisualElement _canvasSizer;     // layout sizer that scales with zoom inside ScrollView
        private VisualElement _gridLayer;        // subtle technical graph paper behind the tree
        private VisualElement _connectors;      // draws bezier prereq lines
        private VisualElement _detailsPanel;
        private VisualElement _queueBar;
        private VisualElement _labBar;
        private VisualElement _queueActiveFill;
        private ScrollView    _canvasScroll;    // the scroll container

        private Label   _zoomLabel;
        private Label   _eraLabel;
        private Label   _progressLabel;
        private VisualElement _progressFill;

        private bool             _open;
        private bool             _searchHasFocus;
        private ResearchNode     _selected;
        private ResearchLab      _activeLab;
        private string           _searchQuery = string.Empty;

        // Drag-to-pan state
        private bool    _panCandidate;
        private bool    _isPanning;
        private int     _panPointerId = -1;
        private Vector2 _panStartMouse;
        private Vector2 _panStartScroll;
        private float   _baseCanvasW = 640f;
        private float   _baseCanvasH = 320f;
        private int     _lastSpaceTriggeredFrame = -1;

        public bool IsOpen => _open;

        /// <summary>True while the Research search field owns keyboard input.</summary>
        public static bool IsSearchFocused => Instance != null && Instance._open && Instance._searchHasFocus;

        private float _zoom = 1.0f;
        private const float ZOOM_MIN = 0.35f;
        private const float ZOOM_MAX = 2.0f;
        private const float ZOOM_STEP = 0.12f;

        // Node geometry
        private const float NODE_W = 220f;
        private const float NODE_H = 132f;
        private const float NODE_GAP_Y = 22f;
        private const float TIER_GAP_X = 128f;
        private const float TREE_PAD = 48f;
        private const float GRID_STEP = 48f;

        private readonly Dictionary<ResearchNode, Rect> _nodeRects = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _doc = GetComponent<UIDocument>();
            _doc.sortingOrder = 600;
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            if (_doc.panelSettings != null)
                GameSettings.ApplyUiScaleAndFit(_doc.panelSettings);
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            _root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!_open || _searchHasFocus) return;
                if (evt.keyCode == KeyCode.Space)
                {
                    evt.StopPropagation();
                    TriggerSpaceResearch();
                }
            }, TrickleDown.TrickleDown);
            HideUI();
        }

        private void OnDestroy()
        {
            if (ResearchManager.Instance != null)
                ResearchManager.Instance.OnChanged -= HandleResearchChanged;
            if (Instance == this) Instance = null;
        }

        private void HandleResearchChanged()
        {
            if (!_open) return;
            RebuildQueueBar();
            RebuildLabBar();
            RebuildCanvas();
            RebuildDetails();
            UpdateEraLabel();
        }

        private void TriggerSpaceResearch()
        {
            if (_lastSpaceTriggeredFrame == Time.frameCount) return;
            _lastSpaceTriggeredFrame = Time.frameCount;
            TryResearchSelected();
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
            if (_open && ResearchManager.Instance?.ActiveResearch != null)
            {
                float pct = Mathf.Clamp01(ResearchManager.Instance.ActiveProgress01) * 100f;
                if (_queueActiveFill != null)
                    _queueActiveFill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
                if (_selected != null && ResearchManager.Instance.ActiveResearch == _selected)
                {
                    if (_progressFill != null)
                        _progressFill.style.width = new StyleLength(new Length(pct, LengthUnit.Percent));
                    if (_progressLabel != null)
                        _progressLabel.text = ResearchManager.Instance.ActiveHasCost
                            ? $"{pct:0}%"
                            : "Awaiting Lab…";
                }
            }

            // Spacebar to research or queue selected node
            if (_open && !_searchHasFocus && _selected != null)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if ((kb != null && kb.spaceKey.wasPressedThisFrame)
                    || GameSettings.WasPressed(InputAction.Jump))
                {
                    TriggerSpaceResearch();
                }
            }
        }

        // ── Open / Close ────────────────────────────────────────────────
        public void Open(ResearchLab lab = null)
        {
            if (ResearchManager.Instance == null || ResearchManager.Instance.tree == null) return;
            if (inventoryRef == null) inventoryRef = FindAnyObjectByType<Inventory>();

            // Ensure no other container/inventory modal is stacked underneath or on top.
            if (VoxelEngine.UI.GameUIController.Instance != null
                && VoxelEngine.UI.GameUIController.Instance.IsInventoryOpen)
            {
                VoxelEngine.UI.GameUIController.Instance.CloseAll();
            }

            _activeLab = lab != null ? lab : FindNearestResearchLab();
            if (_activeLab != null) _activeLab.EnsureContainers();

            if (!_open)
            {
                _open = true;
                _zoom = 1.0f;
                VoxelEngine.UI.UIState.PushBlock();
                if (ResearchManager.Instance != null)
                {
                    ResearchManager.Instance.OnChanged -= HandleResearchChanged;
                    ResearchManager.Instance.OnChanged += HandleResearchChanged;
                }
            }

            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            Build();
            AnimateOpen();
        }

        private ResearchLab FindNearestResearchLab()
        {
            if (inventoryRef == null) return null;
            Vector3 pos = inventoryRef.transform.position;
            float bestDistSq = 64f; // 8m interaction range
            ResearchLab best = null;
            var labs = FindObjectsByType<ResearchLab>(FindObjectsInactive.Exclude);
            foreach (var l in labs)
            {
                if (l == null) continue;
                float dSq = (l.transform.position - pos).sqrMagnitude;
                if (dSq <= bestDistSq)
                {
                    bestDistSq = dSq;
                    best = l;
                }
            }
            return best;
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _activeLab = null;
            _searchHasFocus = false;
            _panCandidate = false;
            _isPanning = false;
            if (ResearchManager.Instance != null)
                ResearchManager.Instance.OnChanged -= HandleResearchChanged;
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
            _detailsPanel = _progressFill = _queueBar = _labBar = _queueActiveFill = null;
            _progressLabel = null;
            _canvas = _canvasSizer = _gridLayer = _connectors = null;
            _canvasScroll = null;
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
            BuildQueueAndLabSection(flexCol);
            var bodyRow = new VisualElement();
            bodyRow.style.flexDirection = FlexDirection.Row;
            bodyRow.style.flexGrow = 1;
            bodyRow.style.minHeight = 0;
            flexCol.Add(bodyRow);
            // 17.27.3-dev - the branch filter is back, with the two rules learned the
            // hard way: it never lists a branch the tree has no nodes in (an empty tab
            // read as a broken screen), and it does not offer BUILDING at all.
            BuildBranchColumn(bodyRow);
            BuildCanvasColumn(bodyRow);
            BuildDetailsPanel(flexCol);
        }

        private void BuildQueueAndLabSection(VisualElement parent)
        {
            _queueBar = new VisualElement();
            _queueBar.style.flexDirection = FlexDirection.Row;
            _queueBar.style.alignItems = Align.Center;
            _queueBar.style.marginBottom = 6;
            _queueBar.style.flexShrink = 0;
            parent.Add(_queueBar);

            _labBar = new VisualElement();
            _labBar.style.flexDirection = FlexDirection.Row;
            _labBar.style.alignItems = Align.Center;
            _labBar.style.marginBottom = 6;
            _labBar.style.flexShrink = 0;
            parent.Add(_labBar);

            RebuildQueueBar();
            RebuildLabBar();
        }

        private void RebuildQueueBar()
        {
            if (_queueBar == null) return;
            _queueBar.Clear();
            _queueActiveFill = null;

            var rm = ResearchManager.Instance;
            if (rm == null || !rm.IsResearchQueueEnabled)
            {
                _queueBar.style.display = DisplayStyle.None;
                return;
            }
            _queueBar.style.display = DisplayStyle.Flex;

            var label = new Label($"RESEARCH QUEUE ({rm.TotalQueuedCount}/{ResearchManager.MaxQueueSlots})");
            label.style.color = new Color(0.68f, 0.86f, 0.92f);
            label.style.fontSize = 10;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.letterSpacing = 1.5f;
            label.style.marginRight = 10;
            label.style.flexShrink = 0;
            _queueBar.Add(label);

            for (int slot = 0; slot < ResearchManager.MaxQueueSlots; slot++)
            {
                int capturedSlot = slot;
                var node = rm.GetQueueSlotNode(slot);
                bool isActiveSlot = (slot == 0 && node != null);

                var slotBox = new VisualElement();
                slotBox.style.flexDirection = FlexDirection.Column;
                slotBox.style.flexGrow = 1;
                slotBox.style.flexBasis = 0;
                slotBox.style.minHeight = 32;
                slotBox.style.marginRight = slot < ResearchManager.MaxQueueSlots - 1 ? 6 : 0;
                slotBox.style.paddingLeft = 8;
                slotBox.style.paddingRight = 6;
                slotBox.style.paddingTop = 4;
                slotBox.style.paddingBottom = 4;
                slotBox.style.backgroundColor = new StyleColor(node != null
                    ? (isActiveSlot ? new Color(0.08f, 0.20f, 0.28f) : new Color(0.08f, 0.12f, 0.16f))
                    : new Color(0.04f, 0.055f, 0.075f, 0.75f));
                slotBox.style.borderTopWidth = slotBox.style.borderBottomWidth =
                slotBox.style.borderLeftWidth = slotBox.style.borderRightWidth = 1;
                var borderCol = new StyleColor(node != null
                    ? (isActiveSlot ? BORDER_ACTIVE : new Color(0.22f, 0.36f, 0.44f))
                    : new Color(0.12f, 0.17f, 0.22f));
                slotBox.style.borderTopColor = slotBox.style.borderBottomColor =
                slotBox.style.borderLeftColor = slotBox.style.borderRightColor = borderCol;
                SetRadius(slotBox, 5);

                var top = new VisualElement();
                top.style.flexDirection = FlexDirection.Row;
                top.style.alignItems = Align.Center;
                slotBox.Add(top);

                var numLbl = new Label($"#{slot + 1}");
                numLbl.style.color = isActiveSlot ? ACCENT_BLUE : TextMuted;
                numLbl.style.fontSize = 9;
                numLbl.style.unityFontStyleAndWeight = FontStyle.Bold;
                numLbl.style.marginRight = 5;
                top.Add(numLbl);

                var nameLbl = new Label(node != null ? node.displayName : "Empty Slot");
                nameLbl.style.color = node != null ? TextPrimary : new Color(0.42f, 0.50f, 0.56f);
                nameLbl.style.fontSize = 10;
                nameLbl.style.unityFontStyleAndWeight = node != null ? FontStyle.Bold : FontStyle.Normal;
                nameLbl.style.flexGrow = 1;
                nameLbl.style.overflow = Overflow.Hidden;
                nameLbl.style.whiteSpace = WhiteSpace.NoWrap;
                top.Add(nameLbl);

                if (node != null)
                {
                    slotBox.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button != 0) return;
                        _selected = node;
                        RebuildCanvas();
                        RebuildDetails();
                    });

                    var removeBtn = new Button(() =>
                    {
                        rm.RemoveQueueSlotAt(capturedSlot);
                        RebuildQueueBar();
                        RebuildCanvas();
                        RebuildDetails();
                    }) { text = "✕" };
                    removeBtn.style.fontSize = 9;
                    removeBtn.style.color = new Color(0.95f, 0.55f, 0.55f);
                    removeBtn.style.backgroundColor = new StyleColor(Color.clear);
                    removeBtn.style.minWidth = 18;
                    removeBtn.style.minHeight = 18;
                    removeBtn.style.paddingLeft = 2; removeBtn.style.paddingRight = 2;
                    removeBtn.style.paddingTop = 0; removeBtn.style.paddingBottom = 0;
                    ZeroBorder(removeBtn);
                    top.Add(removeBtn);

                    if (isActiveSlot)
                    {
                        var barBg = new VisualElement();
                        barBg.style.height = 3;
                        barBg.style.marginTop = 3;
                        barBg.style.backgroundColor = new StyleColor(new Color(0.03f, 0.04f, 0.06f));
                        SetRadius(barBg, 2);
                        slotBox.Add(barBg);

                        _queueActiveFill = new VisualElement();
                        _queueActiveFill.style.height = 3;
                        _queueActiveFill.style.width = new StyleLength(new Length(Mathf.Clamp01(rm.ActiveProgress01) * 100f, LengthUnit.Percent));
                        _queueActiveFill.style.backgroundColor = new StyleColor(ACCENT_BLUE);
                        SetRadius(_queueActiveFill, 2);
                        barBg.Add(_queueActiveFill);
                    }
                }

                _queueBar.Add(slotBox);
            }
        }

        private void RebuildLabBar()
        {
            if (_labBar == null) return;
            _labBar.Clear();

            if (_activeLab == null || _activeLab.scienceInput == null)
            {
                _labBar.style.display = DisplayStyle.None;
                return;
            }
            _labBar.style.display = DisplayStyle.Flex;
            _labBar.style.paddingLeft = 10;
            _labBar.style.paddingRight = 10;
            _labBar.style.paddingTop = 5;
            _labBar.style.paddingBottom = 5;
            _labBar.style.backgroundColor = new StyleColor(new Color(0.06f, 0.085f, 0.115f, 0.96f));
            _labBar.style.borderTopWidth = _labBar.style.borderBottomWidth =
            _labBar.style.borderLeftWidth = _labBar.style.borderRightWidth = 1;
            var bCol = new StyleColor(new Color(0.18f, 0.34f, 0.42f));
            _labBar.style.borderTopColor = _labBar.style.borderBottomColor =
            _labBar.style.borderLeftColor = _labBar.style.borderRightColor = bCol;
            SetRadius(_labBar, 5);

            var title = new Label("RESEARCH LAB INPUT:");
            title.style.color = new Color(0.68f, 0.88f, 0.94f);
            title.style.fontSize = 10;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 1.2f;
            title.style.marginRight = 12;
            _labBar.Add(title);

            for (int tier = 1; tier <= 3; tier++)
            {
                string id = $"item_sciencet{tier}";
                int inLab = _activeLab.scienceInput.CountOfId(id);
                int inBag = inventoryRef != null && inventoryRef.container != null
                    ? inventoryRef.container.CountOfId(id)
                    : 0;

                var packGroup = new VisualElement();
                packGroup.style.flexDirection = FlexDirection.Row;
                packGroup.style.alignItems = Align.Center;
                packGroup.style.marginRight = 12;
                _labBar.Add(packGroup);

                var dot = BuildTierDot(ScienceItem.GetTierColor(tier), ScienceItem.GetTierRomanNumeral(tier), 16);
                dot.style.marginRight = 5;
                packGroup.Add(dot);

                var countLbl = new Label($"Lab: {inLab}  (Bag: {inBag})");
                countLbl.style.color = inLab > 0 ? TextPrimary : TextMuted;
                countLbl.style.fontSize = 10;
                countLbl.style.marginRight = 6;
                packGroup.Add(countLbl);

                int capturedTier = tier;
                var loadBtn = StyledButton("+Load", new Color(0.16f, 0.32f, 0.40f), () =>
                {
                    TransferSciencePackToLab(capturedTier, 10);
                    RebuildLabBar();
                    RebuildCanvas();
                    RebuildDetails();
                });
                loadBtn.style.minHeight = 22;
                loadBtn.style.fontSize = 9;
                loadBtn.style.paddingLeft = 7;
                loadBtn.style.paddingRight = 7;
                loadBtn.SetEnabled(inBag > 0);
                packGroup.Add(loadBtn);
            }

            _labBar.Add(new VisualElement { style = { flexGrow = 1 } });

            var loadNeededBtn = StyledButton("LOAD NEEDED FOR SELECTED", new Color(0.20f, 0.46f, 0.34f), () =>
            {
                AutoLoadSelectedNodePacksToLab(_selected ?? ResearchManager.Instance?.ActiveResearch);
                RebuildLabBar();
                RebuildCanvas();
                RebuildDetails();
            });
            loadNeededBtn.style.minHeight = 24;
            loadNeededBtn.style.fontSize = 10;
            loadNeededBtn.style.marginRight = 8;
            _labBar.Add(loadNeededBtn);

            var openSlotsBtn = StyledButton("OPEN LAB CONTAINER", new Color(0.22f, 0.26f, 0.34f), () =>
            {
                var labToOpen = _activeLab;
                Close();
                if (labToOpen != null)
                    VoxelEngine.UI.GameUIController.Instance?.OpenContainer(labToOpen.scienceInput);
            });
            openSlotsBtn.style.minHeight = 24;
            openSlotsBtn.style.fontSize = 10;
            _labBar.Add(openSlotsBtn);
        }

        private void TransferSciencePackToLab(int tier, int maxCount)
        {
            if (_activeLab == null || _activeLab.scienceInput == null || inventoryRef == null || inventoryRef.container == null)
                return;
            string id = $"item_sciencet{tier}";
            var bag = inventoryRef.container;
            for (int i = 0; i < bag.Size && maxCount > 0; i++)
            {
                var st = bag.GetSlot(i);
                if (st.IsEmpty || st.item == null) continue;
                bool match = string.Equals(st.item.itemId, id, System.StringComparison.OrdinalIgnoreCase)
                    || (st.item is ScienceItem sci && sci.EffectiveTier == tier);
                if (!match) continue;
                int move = Mathf.Min(st.count, maxCount);
                var leftover = _activeLab.scienceInput.Insert(new ItemStack(st.item, move));
                int rem = (leftover != null && !leftover.IsEmpty) ? leftover.count : 0;
                int inserted = move - rem;
                if (inserted > 0)
                {
                    bag.Remove(st.item, inserted);
                    maxCount -= inserted;
                }
            }
        }

        private void AutoLoadSelectedNodePacksToLab(ResearchNode node)
        {
            if (node == null || node.cost == null || _activeLab == null || _activeLab.scienceInput == null) return;
            if (inventoryRef == null || inventoryRef.container == null) return;
            var rm = ResearchManager.Instance;
            foreach (var c in node.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                int need = rm != null ? rm.GetEffectiveCount(node, c.count) : c.count;
                int haveInLab = string.IsNullOrEmpty(c.pack.itemId)
                    ? _activeLab.scienceInput.CountOf(c.pack)
                    : _activeLab.scienceInput.CountOfId(c.pack.itemId);
                int deficit = need - haveInLab;
                if (deficit > 0)
                    TransferSciencePackToLab(c.pack.EffectiveTier, deficit);
            }
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
            accent.style.backgroundColor = new StyleColor(ACCENT_BLUE);
            accent.style.marginRight = 10;
            SetRadius(accent, 2);
            header.Add(accent);

            var title = new Label("RESEARCH NETWORK");
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
            var searchIcon = new Label("⌕");
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

        // ── Branch filter (restored 17.27.3-dev) ────────────────────────
        // A left strip of branch buttons filters the map by the node's own
        // ResearchSubCategory. Three rules, all of them corrections of earlier mistakes:
        //
        //   1. A branch is ONLY offered when the tree actually has nodes in it, and each
        //      tab carries its node count. The first version of this strip listed every
        //      category whether it had content or not, so the Military and Building tabs
        //      opened onto an empty canvas and read as a broken window.
        //   2. BUILDING is never offered. It is the one branch that was asked for by name
        //      to be removed, and its nodes are still on the map under ALL - the filter is
        //      the only thing that goes away, never the research.
        //   3. ALL is the first tab and is always present, so nothing can become
        //      unreachable: a branchess view of the tree is one click away.
        //
        // Player Upgrades is a category rather than a sub-category, so it gets its own tab
        // only when such nodes exist, and it is matched on the category.

        private const string PlayerTabKey = "PLAYER UPGRADES";

        /// <summary>One button in the branch strip: what it shows and what it selects.</summary>
        private struct BranchTab
        {
            public string label;
            public bool playersOnly;              // true = filter on ResearchCategory, not sub-category
            public ResearchSubCategory sub;       // valid when playersOnly is false
            public int count;
        }

        private readonly List<BranchTab> _branchTabs = new();
        private readonly List<(BranchTab tab, Button button)> _branchButtons = new();
        private int _branchIndex;                 // 0 is always ALL
        private VisualElement _branchColumn;

        private static string BranchLabelFor(ResearchNode node)
            => node == null ? "GENERAL" : node.subCategory.ToString().ToUpperInvariant();

        private void BuildBranchColumn(VisualElement parent)
        {
            _branchColumn = new VisualElement();
            _branchColumn.style.width = 168;
            _branchColumn.style.flexShrink = 0;
            _branchColumn.style.marginRight = 8;
            _branchColumn.style.backgroundColor = new StyleColor(CANVAS_BG);
            _branchColumn.style.borderTopWidth = _branchColumn.style.borderBottomWidth =
            _branchColumn.style.borderLeftWidth = _branchColumn.style.borderRightWidth = 1;
            var border = new StyleColor(new Color(0.12f, 0.25f, 0.31f, 0.95f));
            _branchColumn.style.borderTopColor = _branchColumn.style.borderBottomColor =
            _branchColumn.style.borderLeftColor = _branchColumn.style.borderRightColor = border;
            SetRadius(_branchColumn, 6);
            parent.Add(_branchColumn);

            CollectBranchTabs();

            var title = new Label("BRANCH");
            title.style.color = new Color(0.70f, 0.88f, 0.92f);
            title.style.fontSize = 9;
            title.style.letterSpacing = 2f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginTop = 8; title.style.marginLeft = 10; title.style.marginBottom = 8;
            _branchColumn.Add(title);

            _branchButtons.Clear();
            for (int i = 0; i < _branchTabs.Count; i++)
            {
                var tab = _branchTabs[i];
                int index = i;
                var btn = new Button(() => SelectBranch(index));
                btn.text = tab.label + "  " + tab.count;
                btn.style.color = new Color(0.86f, 0.92f, 0.95f);
                btn.style.fontSize = 11;
                btn.style.unityTextAlign = TextAnchor.MiddleLeft;
                btn.style.unityFontStyleAndWeight = FontStyle.Normal;
                btn.style.height = 26;
                btn.style.marginLeft = 8; btn.style.marginRight = 8; btn.style.marginBottom = 3;
                btn.style.paddingLeft = 10;
                SetRadius(btn, 4);
                _branchColumn.Add(btn);
                _branchButtons.Add((tab, btn));
            }

            if (_branchIndex >= _branchTabs.Count) _branchIndex = 0;
            RefreshBranchButtons();
        }

        /// <summary>Every branch the tree has nodes in, in a stable order, with ALL first.
        /// Building is deliberately absent (see the block comment above).</summary>
        private void CollectBranchTabs()
        {
            _branchTabs.Clear();
            var tree = ResearchManager.Instance?.tree;

            int total = 0, players = 0;
            var counts = new Dictionary<ResearchSubCategory, int>();
            if (tree != null)
            {
                foreach (var n in tree.nodes)
                {
                    if (n == null) continue;
                    if (n.upgradeKind == PlayerUpgradeKind.UnlockFlight || n.nodeId == "up_flight") continue;
                    total++;
                    if (n.category == ResearchCategory.PlayerUpgrades) { players++; continue; }
                    counts.TryGetValue(n.subCategory, out int c);
                    counts[n.subCategory] = c + 1;
                }
            }

            _branchTabs.Add(new BranchTab { label = "ALL", count = total });

            // Authored order, not enum order: the strip reads the way the factory grows.
            var order = new[]
            {
                ResearchSubCategory.General, ResearchSubCategory.Logistics,
                ResearchSubCategory.Production, ResearchSubCategory.Power,
                ResearchSubCategory.Chemistry, ResearchSubCategory.Storage,
                ResearchSubCategory.Military
            };
            foreach (var sub in order)
            {
                if (sub == ResearchSubCategory.Building) continue;   // asked for by name
                if (!counts.TryGetValue(sub, out int c) || c <= 0) continue;
                _branchTabs.Add(new BranchTab { label = sub.ToString().ToUpperInvariant(), sub = sub, count = c });
            }

            if (players > 0)
                _branchTabs.Add(new BranchTab { label = PlayerTabKey, playersOnly = true, count = players });
        }

        private void SelectBranch(int index)
        {
            _branchIndex = Mathf.Clamp(index, 0, Mathf.Max(0, _branchTabs.Count - 1));
            RefreshBranchButtons();
            RebuildCanvas();
        }

        private void RefreshBranchButtons()
        {
            for (int i = 0; i < _branchButtons.Count; i++)
            {
                var (tab, btn) = _branchButtons[i];
                bool active = i == _branchIndex;
                btn.style.backgroundColor = new StyleColor(active
                    ? new Color(0.10f, 0.34f, 0.44f)
                    : new Color(0.06f, 0.10f, 0.14f));
                btn.style.color = new Color(active ? 1f : 0.80f, active ? 1f : 0.88f, 1f);
                btn.style.unityFontStyleAndWeight = active ? FontStyle.Bold : FontStyle.Normal;
                ZeroBorder(btn);
            }
        }

        /// <summary>Does the active branch accept this node?</summary>
        private bool BranchAccepts(ResearchNode node)
        {
            if (_branchIndex <= 0 || _branchIndex >= _branchTabs.Count) return true;   // ALL
            var tab = _branchTabs[_branchIndex];
            if (tab.playersOnly) return node.category == ResearchCategory.PlayerUpgrades;
            return node.category != ResearchCategory.PlayerUpgrades && node.subCategory == tab.sub;
        }

        // ── Canvas Column ───────────────────────────────────────────────
        private void BuildCanvasColumn(VisualElement parent)
        {
            var col = new VisualElement();
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.style.minWidth = 0;
            col.style.backgroundColor = new StyleColor(CANVAS_BG);
            col.style.borderTopWidth = col.style.borderBottomWidth =
            col.style.borderLeftWidth = col.style.borderRightWidth = 1;
            var canvasBorder = new StyleColor(new Color(0.12f, 0.25f, 0.31f, 0.95f));
            col.style.borderTopColor = col.style.borderBottomColor =
            col.style.borderLeftColor = col.style.borderRightColor = canvasBorder;
            SetRadius(col, 6);
            parent.Add(col);

            var mapHeader = new VisualElement();
            mapHeader.style.height = 28;
            mapHeader.style.flexDirection = FlexDirection.Row;
            mapHeader.style.alignItems = Align.Center;
            mapHeader.style.paddingLeft = 10;
            mapHeader.style.paddingRight = 10;
            mapHeader.style.borderBottomWidth = 1;
            mapHeader.style.borderBottomColor = new StyleColor(new Color(0.14f, 0.25f, 0.30f));
            col.Add(mapHeader);
            var mapTitle = new Label("DEPENDENCY MAP");
            mapTitle.style.color = new Color(0.70f, 0.88f, 0.92f);
            mapTitle.style.fontSize = 9;
            mapTitle.style.letterSpacing = 2f;
            mapTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            mapHeader.Add(mapTitle);
            var mapHint = new Label("  DRAG TO PAN  ·  PREREQUISITES  →  RESEARCH PATH");
            mapHint.style.color = TextMuted;
            mapHint.style.fontSize = 9;
            mapHint.style.flexGrow = 1;
            mapHint.style.unityTextAlign = TextAnchor.MiddleRight;
            mapHeader.Add(mapHint);

            _canvasScroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
            VoxelEngine.UI.UITheme.StyleScroller(_canvasScroll);
            _canvasScroll.style.flexGrow = 1;
            col.Add(_canvasScroll);

            _canvasSizer = new VisualElement();
            _canvasSizer.style.flexShrink = 0;
            _canvasScroll.Add(_canvasSizer);

            _canvas = new VisualElement();
            _canvas.style.flexShrink = 0;
            _canvas.style.transformOrigin = new TransformOrigin(new Length(0, LengthUnit.Percent), new Length(0, LengthUnit.Percent));
            _canvas.style.translate = new StyleTranslate(new Translate(0, 0, 0));
            _canvas.style.scale = new StyleScale(new Scale(new Vector3(_zoom, _zoom, 1f)));
            _canvas.style.paddingTop = TREE_PAD;
            _canvas.style.paddingBottom = TREE_PAD;
            _canvas.style.paddingLeft = TREE_PAD;
            _canvas.style.paddingRight = TREE_PAD;
            _canvasSizer.Add(_canvas);

            RegisterCanvasDragPan(_canvasScroll);

            _gridLayer = new VisualElement();
            _gridLayer.style.position = Position.Absolute;
            _gridLayer.style.left = 0; _gridLayer.style.top = 0;
            _gridLayer.style.right = 0; _gridLayer.style.bottom = 0;
            _gridLayer.pickingMode = PickingMode.Ignore;
            _gridLayer.generateVisualContent += DrawResearchGrid;
            _canvas.Add(_gridLayer);

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
                if (_canvas[i] == _connectors || _canvas[i] == _gridLayer) continue;
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
                // Flight is a jetpack, not a research unlock.
                if (n.upgradeKind == PlayerUpgradeKind.UnlockFlight || n.nodeId == "up_flight") continue;
                // 17.27.3-dev: the strip filters by branch; ALL (index 0) filters nothing.
                if (!BranchAccepts(n)) continue;
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
                var empty = new Label(_branchIndex > 0 && string.IsNullOrEmpty(_searchQuery)
                    ? "Nothing in this branch yet."
                    : "No research in this filter.");
                empty.style.position = Position.Absolute;
                empty.style.left = 40; empty.style.top = 40;
                empty.style.color = TextMuted;
                empty.style.fontSize = 13;
                _canvas.Add(empty);
                _canvas.style.width = 400; _canvas.style.height = 200;
                _gridLayer?.MarkDirtyRepaint();
                _connectors.MarkDirtyRepaint();
                return;
            }

            // Lay the map out by dependency depth, not merely by authored tier. Nodes
            // sharing prerequisites naturally branch into sibling paths, while merge
            // points are centered over their incoming research lines.
            var visibleNodes = new HashSet<ResearchNode>(nodes);
            var depthByNode = new Dictionary<ResearchNode, int>();
            var resolving = new HashSet<ResearchNode>();
            int ResolveDepth(ResearchNode node)
            {
                if (depthByNode.TryGetValue(node, out int knownDepth)) return knownDepth;
                if (!resolving.Add(node)) return Mathf.Clamp(node.tier - 1, 0, 20);

                int depth = 0;
                if (node.prerequisites != null)
                {
                    foreach (var prerequisite in node.prerequisites)
                    {
                        if (prerequisite == null || !visibleNodes.Contains(prerequisite)) continue;
                        depth = Mathf.Max(depth, ResolveDepth(prerequisite) + 1);
                    }
                }

                resolving.Remove(node);
                depth = Mathf.Clamp(depth, 0, 20);
                depthByNode[node] = depth;
                return depth;
            }

            var byDepth = new SortedDictionary<int, List<ResearchNode>>();
            int maxDepth = 0;
            foreach (var node in nodes)
            {
                int depth = ResolveDepth(node);
                if (!byDepth.TryGetValue(depth, out var list))
                    byDepth[depth] = list = new List<ResearchNode>();
                list.Add(node);
                maxDepth = Mathf.Max(maxDepth, depth);
            }

            var rowCenterByNode = new Dictionary<ResearchNode, float>();
            float maxBottom = 48f;
            foreach (var layer in byDepth)
            {
                float ParentCenter(ResearchNode node)
                {
                    if (node.prerequisites == null) return -1f;
                    float total = 0f;
                    int count = 0;
                    foreach (var prerequisite in node.prerequisites)
                    {
                        if (prerequisite == null || !rowCenterByNode.TryGetValue(prerequisite, out float center)) continue;
                        total += center;
                        count++;
                    }
                    return count > 0 ? total / count : -1f;
                }

                layer.Value.Sort((a, b) =>
                {
                    float parentA = ParentCenter(a);
                    float parentB = ParentCenter(b);
                    int compare = parentA.CompareTo(parentB);
                    if (compare != 0) return compare;
                    compare = a.subCategory.CompareTo(b.subCategory);
                    if (compare != 0) return compare;
                    compare = a.column.CompareTo(b.column);
                    if (compare != 0) return compare;
                    compare = a.tier.CompareTo(b.tier);
                    if (compare != 0) return compare;
                    return string.Compare(a.nodeId, b.nodeId, System.StringComparison.OrdinalIgnoreCase);
                });

                float x = layer.Key * (NODE_W + TIER_GAP_X);
                float y = 44f;
                float columnBottom = y + layer.Value.Count * (NODE_H + NODE_GAP_Y);

                var stageLabel = new Label($"SEQUENCE {layer.Key + 1:00}");
                stageLabel.style.position = Position.Absolute;
                stageLabel.style.left = x;
                stageLabel.style.top = 5;
                stageLabel.style.color = new Color(0.55f, 0.82f, 0.88f);
                stageLabel.style.fontSize = 10;
                stageLabel.style.letterSpacing = 2f;
                stageLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                stageLabel.pickingMode = PickingMode.Ignore;
                _canvas.Add(stageLabel);

                var stageCount = new Label($"{layer.Value.Count:00} NODES");
                stageCount.style.position = Position.Absolute;
                stageCount.style.left = x;
                stageCount.style.top = 20;
                stageCount.style.color = TextMuted;
                stageCount.style.fontSize = 9;
                stageCount.style.letterSpacing = 1f;
                stageCount.pickingMode = PickingMode.Ignore;
                _canvas.Add(stageCount);

                var guide = new VisualElement();
                guide.style.position = Position.Absolute;
                guide.style.left = x + NODE_W + TIER_GAP_X * 0.5f;
                guide.style.top = 38;
                guide.style.width = 1;
                guide.style.height = Mathf.Max(16f, columnBottom - 40f);
                guide.style.backgroundColor = new StyleColor(new Color(0.18f, 0.56f, 0.62f, 0.16f));
                guide.pickingMode = PickingMode.Ignore;
                _canvas.Add(guide);

                foreach (var node in layer.Value)
                {
                    var card = BuildNodeCard(node);
                    card.style.position = Position.Absolute;
                    card.style.left = x;
                    card.style.top = y;
                    _canvas.Add(card);
                    _nodeRects[node] = new Rect(x, y, NODE_W, NODE_H);
                    rowCenterByNode[node] = y + NODE_H * 0.5f;
                    y += NODE_H + NODE_GAP_Y;
                }
                maxBottom = Mathf.Max(maxBottom, y);
            }

            float canvasWidth = (maxDepth + 1) * NODE_W + maxDepth * TIER_GAP_X + TREE_PAD * 2f + 260f;
            float canvasHeight = maxBottom + TREE_PAD + 180f;
            _baseCanvasW = Mathf.Max(canvasWidth, 960f);
            _baseCanvasH = Mathf.Max(canvasHeight, 560f);
            _canvas.style.width = _baseCanvasW;
            _canvas.style.height = _baseCanvasH;
            _canvas.style.minWidth = _baseCanvasW;
            _canvas.style.minHeight = _baseCanvasH;
            UpdateCanvasSizerDimensions();

            // Animate glow on cards that became ready
            foreach (var n in nodes)
            {
                if (rm != null && rm.ArePrerequisitesMet(n) && !rm.IsUnlocked(n) && _nodeRects.ContainsKey(n))
                {
                    // Subtle ready pulse is handled by the card's own breathing glow
                }
            }

            _gridLayer?.MarkDirtyRepaint();
            _connectors.MarkDirtyRepaint();
            UpdateEraLabel();
        }

        private void DrawResearchGrid(MeshGenerationContext mgc)
        {
            if (_gridLayer == null || _canvas == null) return;
            var painter = mgc.painter2D;
            Rect rect = _gridLayer.contentRect;
            float width = Mathf.Max(rect.width, _canvas.resolvedStyle.width);
            float height = Mathf.Max(rect.height, _canvas.resolvedStyle.height);
            if (width <= 0f || height <= 0f) return;

            painter.lineWidth = 1f;
            painter.strokeColor = new Color(0.18f, 0.36f, 0.41f, 0.22f);
            for (float x = 0f; x <= width; x += GRID_STEP)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, height));
                painter.Stroke();
            }
            for (float y = 0f; y <= height; y += GRID_STEP)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(width, y));
                painter.Stroke();
            }

            painter.lineWidth = 1.25f;
            painter.strokeColor = new Color(0.12f, 0.42f, 0.48f, 0.18f);
            float majorStep = GRID_STEP * 4f;
            for (float x = 0f; x <= width; x += majorStep)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, height));
                painter.Stroke();
            }
            for (float y = 0f; y <= height; y += majorStep)
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(width, y));
                painter.Stroke();
            }
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
            int queueSlot = rm != null && rm.IsResearchQueueEnabled ? rm.GetQueueSlotNumber(n) : 0;
            bool queued = queueSlot > 1;
            bool sel = _selected == n;

            Color bg = maxed ? CARD_DONE
                     : active ? CARD_ACTIVE
                     : queued ? new Color(0.07f, 0.16f, 0.24f, 1.00f)
                     : ready && rank > 0 ? CARD_READY
                     : ready ? CARD_READY
                     : CARD_LOCKED;
            Color borderC = sel ? BORDER_SELECT
                         : maxed ? BORDER_DONE
                         : active ? BORDER_ACTIVE
                         : queued ? new Color(0.28f, 0.68f, 0.88f, 1.00f)
                         : BORDER_DEFAULT;

            var card = new VisualElement();
            card.style.width = NODE_W;
            card.style.height = NODE_H;
            card.style.paddingTop = 8; card.style.paddingBottom = 8;
            card.style.paddingLeft = 11; card.style.paddingRight = 11;
            card.style.backgroundColor = new StyleColor(bg);
            card.style.borderTopWidth = card.style.borderBottomWidth =
            card.style.borderLeftWidth = card.style.borderRightWidth = 1.5f;
            var sc = new StyleColor(borderC);
            card.style.borderTopColor = card.style.borderBottomColor =
            card.style.borderLeftColor = card.style.borderRightColor = sc;
            SetRadius(card, 8);

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
            iconSwatch.style.width = 28; iconSwatch.style.height = 28;
            iconSwatch.style.flexShrink = 0;
            iconSwatch.style.backgroundColor = new StyleColor(n.iconTint);
            iconSwatch.style.marginRight = 8;
            iconSwatch.style.alignItems = Align.Center;
            iconSwatch.style.justifyContent = Justify.Center;
            SetRadius(iconSwatch, 5);
            var tierMark = new Label($"T{Mathf.Clamp(n.tier, 1, 99):00}");
            tierMark.style.color = new Color(0.015f, 0.035f, 0.045f);
            tierMark.style.fontSize = 8;
            tierMark.style.unityFontStyleAndWeight = FontStyle.Bold;
            iconSwatch.Add(tierMark);
            topRow.Add(iconSwatch);

            var name = new Label(n.displayName);
            name.style.color = (maxed || active) ? TextPrimary : (ready ? new Color(0.88f, 0.95f, 0.97f) : TextMuted);
            name.style.fontSize = 13;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.style.flexGrow = 1;
            name.style.maxHeight = 34;
            name.pickingMode = PickingMode.Ignore;
            topRow.Add(name);

            var branchTag = new Label($"{BranchLabelFor(n)}   /   TIER {Mathf.Clamp(n.tier, 1, 10):00}");
            branchTag.style.color = TextMuted;
            branchTag.style.fontSize = 8;
            branchTag.style.letterSpacing = 1f;
            branchTag.style.marginBottom = 4;
            branchTag.style.marginLeft = 36;
            branchTag.pickingMode = PickingMode.Ignore;
            card.Add(branchTag);

            // Cost row (compact) — hidden once a node is fully researched
            if (!maxed && n.cost != null && n.cost.Length > 0)
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
                    int have = CountAvailableSciencePack(c.pack);
                    costRow.Add(SciencePackIcon(c.pack, eff, have));
                }
            }

            // Status line
            string statusLine;
            Color statusCol = TextMuted;
            if (n.IsRepeatable && maxed) { statusLine = $"COMPLETE (RANK {rank}/{n.maxRanks})"; statusCol = ACCENT_GREEN; }
            else if (active) { statusLine = queueSlot > 0 ? $"RESEARCHING (#1)" : "RESEARCH IN PROGRESS"; statusCol = new Color(0.52f, 0.88f, 0.97f); }
            else if (queued) { statusLine = $"QUEUED (#{queueSlot} IN QUEUE)"; statusCol = new Color(0.52f, 0.82f, 0.95f); }
            else if (n.IsRepeatable) { statusLine = $"RANK {rank}/{n.maxRanks}"; statusCol = rank > 0 ? ACCENT_AMBER : TextMuted; }
            else if (maxed) { statusLine = "COMPLETE"; statusCol = ACCENT_GREEN; }
            else if (ready) { statusLine = "READY TO RESEARCH"; statusCol = TextPrimary; }
            else { statusLine = "LOCKED · PREREQUISITE REQUIRED"; statusCol = TextMuted; }

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
            card.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 0 || _isPanning) return;
                _selected = n;
                RebuildCanvas();
                RebuildDetails();
                e.StopPropagation();
            });
            return card;
        }

        private int CountAvailableSciencePack(ScienceItem pack)
        {
            if (pack == null) return 0;
            int total = 0;
            if (inventoryRef != null && inventoryRef.container != null)
            {
                total += string.IsNullOrEmpty(pack.itemId)
                    ? inventoryRef.container.CountOf(pack)
                    : inventoryRef.container.CountOfId(pack.itemId);
            }
            if (_activeLab != null && _activeLab.scienceInput != null)
            {
                total += string.IsNullOrEmpty(pack.itemId)
                    ? _activeLab.scienceInput.CountOf(pack)
                    : _activeLab.scienceInput.CountOfId(pack.itemId);
            }
            return total;
        }

        private static VisualElement BuildTierDot(Color tierColor, string romanNumeral, int size = 16)
        {
            var icon = new VisualElement();
            icon.style.width = size;
            icon.style.height = size;
            icon.style.flexShrink = 0;
            icon.style.backgroundColor = new StyleColor(tierColor);
            icon.style.alignItems = Align.Center;
            icon.style.justifyContent = Justify.Center;
            icon.pickingMode = PickingMode.Ignore;
            SetRadius(icon, size * 0.5f);

            var numeral = new Label(romanNumeral);
            numeral.style.color = new Color(0.98f, 0.99f, 1f);
            numeral.style.fontSize = size >= 16 ? 8 : 7;
            numeral.style.unityFontStyleAndWeight = FontStyle.Bold;
            numeral.style.unityTextAlign = TextAnchor.MiddleCenter;
            numeral.style.paddingLeft = 0; numeral.style.paddingRight = 0;
            numeral.style.paddingTop = 0; numeral.style.paddingBottom = 0;
            numeral.style.marginLeft = 0; numeral.style.marginRight = 0;
            numeral.style.marginTop = 0; numeral.style.marginBottom = 0;
            numeral.pickingMode = PickingMode.Ignore;
            icon.Add(numeral);
            return icon;
        }

        private VisualElement SciencePackIcon(ScienceItem pack, int need, int have)
        {
            var w = new VisualElement();
            w.style.flexDirection = FlexDirection.Row;
            w.style.alignItems = Align.Center;
            w.style.marginRight = 6;
            w.style.marginTop = 1;
            w.pickingMode = PickingMode.Ignore;

            var icon = BuildTierDot(pack.GetTierColor(), pack.GetTierRomanNumeral(), 16);
            icon.style.marginRight = 3;
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

            // Cost pills — hidden once a node is researched
            if (!maxed && n.cost != null)
            {
                foreach (var c in n.cost)
                {
                    if (c.pack == null) continue;
                    int eff = rm.GetEffectiveCount(n, c.count);
                    int have = CountAvailableSciencePack(c.pack);

                    var pill = new VisualElement();
                    pill.style.flexDirection = FlexDirection.Row;
                    pill.style.alignItems = Align.Center;
                    pill.style.marginRight = 8;
                    pill.style.paddingLeft = 6; pill.style.paddingRight = 8;
                    pill.style.paddingTop = 2; pill.style.paddingBottom = 2;
                    pill.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.18f));
                    SetRadius(pill, 8);
                    topRow.Add(pill);

                    var pIcon = BuildTierDot(c.pack.GetTierColor(), c.pack.GetTierRomanNumeral(), 16);
                    pIcon.style.marginRight = 4;
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
                    RebuildQueueBar();
                    RebuildDetails();
                    RebuildCanvas();
                });
                cancelBtn.style.minHeight = 28;
                cancelBtn.style.fontSize = 11;
                btnRow.Add(cancelBtn);

                if (rm.IsResearchQueueEnabled && n.IsRepeatable && rm.CanEnqueue(n))
                {
                    var queueNextBtn = StyledButton("QUEUE NEXT RANK (SPACE)", new Color(0.20f, 0.44f, 0.56f), () =>
                    {
                        TryResearchSelected();
                    });
                    queueNextBtn.style.minHeight = 28;
                    queueNextBtn.style.fontSize = 11;
                    queueNextBtn.style.marginLeft = 8;
                    btnRow.Add(queueNextBtn);
                }
            }
            else if (rm.IsResearchQueueEnabled && rm.IsQueued(n))
            {
                int slotNum = rm.GetQueueSlotNumber(n);
                var removeQueueBtn = StyledButton($"REMOVE FROM QUEUE (#{slotNum})", new Color(0.50f, 0.26f, 0.20f), () =>
                {
                    rm.RemoveFromQueue(n);
                    RebuildQueueBar();
                    RebuildDetails();
                    RebuildCanvas();
                });
                removeQueueBtn.style.minHeight = 28;
                removeQueueBtn.style.fontSize = 11;
                btnRow.Add(removeQueueBtn);

                if (n.IsRepeatable && rm.CanEnqueue(n))
                {
                    var addMoreBtn = StyledButton("QUEUE ANOTHER RANK (SPACE)", new Color(0.20f, 0.44f, 0.56f), () =>
                    {
                        TryResearchSelected();
                    });
                    addMoreBtn.style.minHeight = 28;
                    addMoreBtn.style.fontSize = 11;
                    addMoreBtn.style.marginLeft = 8;
                    btnRow.Add(addMoreBtn);
                }
            }
            else if (!rm.ArePrerequisitesMet(n) && !(rm.IsResearchQueueEnabled && rm.ArePrerequisitesMetOrQueued(n)))
            {
                var blocked = new Label("✗ Prerequisites not met");
                blocked.style.color = new Color(0.92f, 0.45f, 0.45f);
                blocked.style.fontSize = 11;
                btnRow.Add(blocked);
            }
            else if (rm.GetFacilityBlockReason(n) is string facilityBlock)
            {
                // The manager already refuses these, but silently - the button would look
                // live and simply do nothing. Stating the requirement here is the whole
                // difference between a designed gate and an apparent bug.
                var blocked = new Label("✗ " + facilityBlock);
                blocked.style.color = new Color(0.95f, 0.66f, 0.32f);
                blocked.style.fontSize = 11;
                blocked.style.whiteSpace = WhiteSpace.Normal;
                btnRow.Add(blocked);
            }
            else
            {
                bool canInventory = n.researchSeconds <= 0f && rm.ArePrerequisitesMet(n);
                bool canPay = AllAffordable(n);
                bool willQueue = rm.ActiveResearch != null && rm.IsResearchQueueEnabled;
                bool canQueue = rm.CanEnqueue(n);

                if (canInventory && !willQueue)
                {
                    var nowBtn = StyledButton("RESEARCH NOW (SPACE)", canPay ? ACCENT_BLUE : new Color(0.30f, 0.30f, 0.34f), () =>
                    {
                        TryResearchSelected();
                    });
                    nowBtn.style.minHeight = 28;
                    nowBtn.style.fontSize = 11;
                    nowBtn.SetEnabled(canPay);
                    btnRow.Add(nowBtn);
                }
                else if (willQueue)
                {
                    string qLabel = rm.TotalQueuedCount >= ResearchManager.MaxQueueSlots
                        ? $"QUEUE FULL ({ResearchManager.MaxQueueSlots}/{ResearchManager.MaxQueueSlots})"
                        : "ADD TO QUEUE (SPACE)";
                    var queueBtn = StyledButton(qLabel, canQueue ? new Color(0.22f, 0.48f, 0.62f) : new Color(0.30f, 0.30f, 0.34f), () =>
                    {
                        TryResearchSelected();
                    });
                    queueBtn.style.minHeight = 28;
                    queueBtn.style.fontSize = 11;
                    queueBtn.style.marginRight = 8;
                    queueBtn.SetEnabled(canQueue);
                    btnRow.Add(queueBtn);

                    if (rm.ArePrerequisitesMet(n))
                    {
                        var startNowBtn = StyledButton("START NOW", new Color(0.30f, 0.55f, 0.30f), () =>
                        {
                            AutoLoadSelectedNodePacksToLab(n);
                            rm.StartResearch(n);
                            if (_activeLab != null && !rm.ActiveHasCost && _activeLab.TryConsumeCost(n, inventoryRef?.container))
                                rm.MarkCostPaid();
                            RebuildQueueBar();
                            RebuildLabBar();
                            RebuildDetails();
                            RebuildCanvas();
                        });
                        startNowBtn.style.minHeight = 28;
                        startNowBtn.style.fontSize = 11;
                        startNowBtn.style.marginRight = 8;
                        btnRow.Add(startNowBtn);
                    }
                }
                else
                {
                    var labBtn = StyledButton("START AT LAB (SPACE)", new Color(0.30f, 0.55f, 0.30f), () =>
                    {
                        TryResearchSelected();
                    });
                    labBtn.style.minHeight = 28;
                    labBtn.style.fontSize = 11;
                    labBtn.style.marginRight = 8;
                    btnRow.Add(labBtn);
                }

                // Space hint
                var spaceHint = new Label(willQueue ? "Press SPACE to queue research" : "Press SPACE to start research");
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
                int tokenUnlocks = 0;
                foreach (var r in n.unlocksRecipes)
                {
                    if (r != null && r.outputItem is VoxelEngine.Building.Tiered.BuildToken)
                    {
                        tokenUnlocks++;
                        continue;
                    }
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
                if (shown == 0)
                {
                    unlockRow.RemoveFromHierarchy();
                }
                int visibleUnlocks = n.unlocksRecipes.Length - tokenUnlocks;
                if (shown > 0 && visibleUnlocks > 4)
                {
                    var more = new Label($"+{visibleUnlocks - 4} more");
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

            if (_selected.researchSeconds <= 0f
                && rm.ActiveResearch == null
                && rm.ArePrerequisitesMet(_selected)
                && inventoryRef != null)
            {
                if (rm.TryResearchFromInventory(_selected, inventoryRef.container))
                {
                    RebuildQueueBar();
                    RebuildLabBar();
                    RebuildCanvas();
                    RebuildDetails();
                    UpdateEraLabel();
                    return;
                }
            }

            if (rm.EnqueueOrStartResearch(_selected))
            {
                if (rm.ActiveResearch == _selected)
                {
                    AutoLoadSelectedNodePacksToLab(_selected);
                    if (_activeLab != null && !rm.ActiveHasCost && _activeLab.TryConsumeCost(_selected, inventoryRef?.container))
                        rm.MarkCostPaid();
                }
                RebuildQueueBar();
                RebuildLabBar();
                RebuildDetails();
                RebuildCanvas();
            }
        }

        private void RegisterCanvasDragPan(ScrollView scroll)
        {
            if (scroll == null) return;
            scroll.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0 && evt.button != 1 && evt.button != 2) return;
                if (evt.target is VisualElement ve && IsDescendantOfScroller(ve, scroll)) return;
                _panCandidate = true;
                _isPanning = false;
                _panPointerId = evt.pointerId;
                _panStartMouse = evt.position;
                _panStartScroll = scroll.scrollOffset;
            }, TrickleDown.TrickleDown);

            scroll.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!_panCandidate || evt.pointerId != _panPointerId) return;
                Vector2 delta = (Vector2)evt.position - _panStartMouse;
                if (!_isPanning && delta.sqrMagnitude > 25f)
                {
                    _isPanning = true;
                    scroll.CapturePointer(_panPointerId);
                }
                if (_isPanning)
                {
                    scroll.scrollOffset = new Vector2(
                        Mathf.Max(0f, _panStartScroll.x - delta.x),
                        Mathf.Max(0f, _panStartScroll.y - delta.y));
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);

            scroll.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != _panPointerId) return;
                if (scroll.HasPointerCapture(_panPointerId))
                    scroll.ReleasePointer(_panPointerId);
                _panCandidate = false;
                // Clear _isPanning on next tick so node card PointerUp in this event pass sees it
                scroll.schedule.Execute(() => _isPanning = false);
            }, TrickleDown.TrickleDown);

            scroll.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (evt.pointerId != _panPointerId) return;
                if (scroll.HasPointerCapture(_panPointerId))
                    scroll.ReleasePointer(_panPointerId);
                _panCandidate = false;
                _isPanning = false;
            });
        }

        private static bool IsDescendantOfScroller(VisualElement target, ScrollView scroll)
        {
            if (target == null || scroll == null) return false;
            var hBar = scroll.horizontalScroller;
            var vBar = scroll.verticalScroller;
            for (var cur = target; cur != null && cur != scroll; cur = cur.parent)
            {
                if (cur == hBar || cur == vBar) return true;
            }
            return false;
        }

        private void UpdateCanvasSizerDimensions()
        {
            if (_canvasSizer == null) return;
            float w = _baseCanvasW * _zoom;
            float h = _baseCanvasH * _zoom;
            _canvasSizer.style.width = w;
            _canvasSizer.style.height = h;
            _canvasSizer.style.minWidth = w;
            _canvasSizer.style.minHeight = h;
        }

        private void SetZoom(float z)
        {
            _zoom = Mathf.Clamp(z, ZOOM_MIN, ZOOM_MAX);
            if (_canvas != null)
                _canvas.style.scale = new StyleScale(new Scale(new Vector3(_zoom, _zoom, 1f)));
            UpdateCanvasSizerDimensions();
            if (_zoomLabel != null)
                _zoomLabel.text = $"{_zoom * 100:0}%";
        }

        private bool AllAffordable(ResearchNode n)
        {
            var rm = ResearchManager.Instance;
            if (rm == null || n == null || n.cost == null) return false;
            foreach (var c in n.cost)
            {
                if (c.pack == null || c.count <= 0) continue;
                if (CountAvailableSciencePack(c.pack) < rm.GetEffectiveCount(n, c.count)) return false;
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
