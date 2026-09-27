// Assets/Scripts/VoxelEngine/Building/Tiered/HammerBuildWheel.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║             INDUSTRIAL WORLD — HAMMER BUILD WHEEL                 ║
// ║                                                                  ║
// ║  Hold the build key, flick, release. The wheel never asks for a  ║
// ║  second click and never asks the hand to land on a target.       ║
// ║                                                                  ║
// ║  Feel contract:                                                  ║
// ║   • The hardware cursor is LOCKED and hidden while the dial is   ║
// ║     up. A virtual pointer integrates raw mouse delta from dead   ║
// ║     centre, so every open starts from the same origin and the    ║
// ║     direction of a piece becomes muscle memory.                  ║
// ║   • Selection is pure angle. Deflection is clamped, the angle    ║
// ║     is not — overshooting the ring by a mile still selects the   ║
// ║     wedge you flicked at.                                        ║
// ║   • No smoothing anywhere on the selection path. Atan2 in,       ║
// ║     wedge out, same frame.                                       ║
// ║   • Release selects what is lit. A quick tap instead pins the    ║
// ║     dial open so it can be read at leisure.                      ║
// ║   • Every new wedge answers with a scale pop, a colour flip and  ║
// ║     an audio tick.                                               ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.FX;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Building.Tiered
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class HammerBuildWheel : MonoBehaviour
    {
        public static HammerBuildWheel Instance { get; private set; }
        public BuildFamily? ActiveFamily { get; private set; }
        public bool IsOpen => _open;

        public Inventory inventory;
        public TieredBlockRegistry registry;

        // ── Dial geometry (design pixels; the whole dial is uniformly scaled) ──
        private const float WheelSize    = 620f;
        private const float RingInner    = 206f;
        private const float RingOuter    = 286f;
        private const float HairOuter    = 302f;
        private const float HairInner    = 192f;
        private const float IconRadius   = 246f;
        private const float IconBox      = 58f;
        private const float HubSize      = 360f;
        private const float PointerReach = 194f;   // the groove between the hub and the wedges

        /// <summary>Hold shorter than this and the dial stays pinned open instead of closing.</summary>
        private const float TapSeconds = 0.20f;

        // ── Palette: warm bone dial, hot iron-oxide highlight ─────────────
        private static readonly Color DialBone      = new(0.914f, 0.898f, 0.855f, 0.97f);
        private static readonly Color DialBoneWeak  = new(0.914f, 0.898f, 0.855f, 0.34f);
        private static readonly Color DialLocked    = new(0.20f, 0.21f, 0.23f, 0.60f);
        private static readonly Color DialHot       = new(0.804f, 0.259f, 0.184f, 1.00f);
        private static readonly Color DialChosen    = new(0.478f, 0.161f, 0.122f, 0.96f);
        private static readonly Color DialHotGlow   = new(0.804f, 0.259f, 0.184f, 0.20f);
        private static readonly Color IconIdle      = new(0.757f, 0.243f, 0.173f, 1.00f);
        private static readonly Color IconHot       = new(1.000f, 0.976f, 0.949f, 1.00f);
        private static readonly Color IconChosen    = new(0.980f, 0.882f, 0.855f, 1.00f);
        private static readonly Color IconBroke     = new(0.36f, 0.35f, 0.34f, 0.85f);
        private static readonly Color HubFace       = new(0.035f, 0.040f, 0.052f, 0.93f);
        private static readonly Color Backdrop      = new(0.010f, 0.012f, 0.018f, 0.58f);

        private static readonly BuildFamily[] StructuralFamilies =
        {
            BuildFamily.Foundation, BuildFamily.Wall, BuildFamily.Floor,
            BuildFamily.Doorway, BuildFamily.Door, BuildFamily.Window,
            BuildFamily.Stairs, BuildFamily.Roof, BuildFamily.Pillar,
            BuildFamily.HalfWall
        };

        private static readonly BuildFamily[] StationFamilies =
        {
            BuildFamily.StationHull, BuildFamily.StationFloor, BuildFamily.StationCorridor,
            BuildFamily.StationJunction, BuildFamily.StationWindow, BuildFamily.StationAirlock,
            BuildFamily.StationDock, BuildFamily.StationDome
        };

        /// <summary>
        /// Which family group the dial is showing. Held per-session rather than saved:
        /// the wheel should open on the everyday pieces, because that is what the player
        /// uses most, even after the station set unlocks.
        /// </summary>
        private BuildFamilyGroup _group = BuildFamilyGroup.Structural;

        private BuildFamily[] Families => _group == BuildFamilyGroup.OrbitalStation
            ? StationFamilies : StructuralFamilies;

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _dial;
        private RadialRing _ring;
        private VisualElement _pointer;
        private readonly List<VisualElement> _iconSlots = new(12);

        private VisualElement _hubIcon;
        private Label _hubGlyph;
        private Label _hubGroupLabel;
        private Label _hubTitle;
        private Label _hubDesc;
        private VisualElement _hubCostColumn;
        private readonly List<Label> _hubCostLines = new(4);
        private Label _hubHint;
        private Label _hubSwapHint;

        private readonly RadialWheelInput _input = new();
        private int _hovered = -1;
        private bool _open;
        private bool _pinned;
        private bool _wasWheelHeld;
        private float _pressTime;
        private float _nextGroupInput;

        /// <summary>
        /// True when the station set is available. Checked live rather than cached, so
        /// finishing the research makes the group appear without reopening the wheel.
        /// </summary>
        private static bool StationGroupUnlocked
        {
            get
            {
                string nodeId = BuildFamilyInfo.RequiredResearchId(BuildFamilyGroup.OrbitalStation);
                if (string.IsNullOrEmpty(nodeId)) return true;
                var rm = VoxelEngine.Research.ResearchManager.Instance;
                return rm != null && rm.IsUnlocked(nodeId);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  LIFECYCLE
        // ══════════════════════════════════════════════════════════════════

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _document = GetComponent<UIDocument>();
            if (_document.panelSettings == null)
                _document.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            if (_document.panelSettings != null)
            {
                // Same fit-to-screen scaling as the main HUD (also forces
                // ScreenSpaceOverlay) — the build wheel must never be anchored
                // off-screen on smaller windows.
                GameSettings.ApplyUiScaleAndFit(_document.panelSettings);
            }
            _root = _document.rootVisualElement;
            _root.style.flexGrow = 1;
            Hide();
        }

        private void Start() => ResolveDependencies();

        private void OnDisable()
        {
            if (_open) Close(selectHovered: false);
        }

        private void OnDestroy()
        {
            if (_open) Close(selectHovered: false);
            if (Instance == this) Instance = null;
        }

        private void ResolveDependencies()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (registry == null && BuildSystemV2.Instance != null) registry = BuildSystemV2.Instance.registry;
            if (registry == null) registry = Resources.Load<TieredBlockRegistry>("TieredBlockRegistry");
        }

        // ══════════════════════════════════════════════════════════════════
        //  INPUT LOOP
        // ══════════════════════════════════════════════════════════════════

        private void Update()
        {
            ResolveDependencies();

            var stack = inventory != null ? inventory.ActiveStack : null;
            bool holdingHammer = stack != null && !stack.IsEmpty && stack.item is Hammer;
            if (!holdingHammer)
            {
                ActiveFamily = null;
                _wasWheelHeld = false;
                if (_open) Close(selectHovered: false);
                return;
            }

            if (!UIState.PauseConsumedThisFrame && GameSettings.WasPressed(InputAction.Pause))
            {
                ExitBuildMode();
                UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            bool pressed = GameSettings.WasPressed(InputAction.BuildWheel);
            bool held    = GameSettings.IsHeld(InputAction.BuildWheel);

            // The edge, not the level: opening on the press means a key already down
            // when the hammer is equipped does not pop the dial in the player's face.
            // The second clause catches a binding whose press edge was eaten by a
            // panel closing on the same frame.
            if (pressed || (held && !_wasWheelHeld))
            {
                _pressTime = Time.unscaledTime;
                if (!_open) { if (!UIState.IsBlocking) Open(); }
                else Unpin();   // a second press re-arms hold-and-release
            }

            if (!held && _wasWheelHeld && _open && !_pinned)
            {
                if (_hovered >= 0) Close(selectHovered: true);
                else if (Time.unscaledTime - _pressTime < TapSeconds) PinOpen();
                else Close(selectHovered: false);
            }
            _wasWheelHeld = held;

            if (!_open) return;

            // A blocking panel claims the cursor on push; re-assert the lock every
            // frame so the dial keeps its own pointer instead of the OS one.
            RadialWheelInput.HoldCursorCentred();

            _input.Sample();
            int hovered = _input.SegmentAt(Families.Length);
            if (hovered != _hovered)
            {
                _hovered = hovered;
                OnHoverChanged();
            }

            UpdatePointer();
            HandleGroupToggle();
            HandleConfirmClick();
        }

        private void HandleConfirmClick()
        {
            if (!LeftMousePressed()) return;
            if (_hovered >= 0)
            {
                SelectFamily(_hovered);
                Close(selectHovered: false);
                return;
            }
            // Centre click is the upgrade tool: no family armed, hammer raises tiers.
            EnterUpgradeMode();
            Close(selectHovered: false);
        }

        /// <summary>
        /// Swapping family groups is contextual: the key only means anything with the
        /// dial up, so it costs the player no binding they might want elsewhere.
        /// </summary>
        private void HandleGroupToggle()
        {
            if (Time.unscaledTime < _nextGroupInput) return;
            bool wants = TabPressed() || Mathf.Abs(ReadScroll()) > 0.01f;
            if (!wants) return;
            _nextGroupInput = Time.unscaledTime + 0.18f;
            ToggleGroup();
        }

        /// <summary>
        /// Flips between the structural and station sets. Does nothing when the station
        /// research is not done, so the toggle cannot reveal locked content.
        /// </summary>
        private void ToggleGroup()
        {
            if (_group == BuildFamilyGroup.Structural)
            {
                if (!StationGroupUnlocked)
                {
                    BuildFeedbackHud.Show("Orbital Station pieces locked",
                        "Research Orbital Construction to unlock the station family.",
                        null, T.AccentAmber);
                    return;
                }
                _group = BuildFamilyGroup.OrbitalStation;
            }
            else _group = BuildFamilyGroup.Structural;

            _hovered = _input.SegmentAt(Families.Length);
            Build();
            PlayTick(0.92f);
        }

        // ══════════════════════════════════════════════════════════════════
        //  OPEN / CLOSE
        // ══════════════════════════════════════════════════════════════════

        public void Open()
        {
            if (_open) return;
            _open = true;
            _pinned = false;
            _hovered = -1;
            _input.Begin();
            UIState.PushBlock();
            RadialWheelInput.HoldCursorCentred();
            Build();
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.45f, 1.18f);
        }

        public void Close(bool selectHovered = false)
        {
            if (!_open) return;
            if (selectHovered && _hovered >= 0) SelectFamily(_hovered);
            _open = false;
            _pinned = false;
            _hovered = -1;
            UIState.PopBlock();
            Hide();
        }

        /// <summary>
        /// A quick tap leaves the dial up so it can be read without holding the key.
        /// The hint line switches to the pinned contract so the change is never silent.
        /// </summary>
        private void PinOpen()
        {
            _pinned = true;
            if (_hubHint != null)
                _hubHint.text = "PINNED  ·  LEFT CLICK TO CONFIRM  ·  ESC TO EXIT";
            PlayTick(0.86f);
        }

        private void Unpin()
        {
            _pinned = false;
            if (_hubHint != null) _hubHint.text = DefaultHint;
        }

        private static string DefaultHint =>
            $"HOLD {GameSettings.GetKey(InputAction.BuildWheel)}  ·  FLICK  ·  RELEASE";

        public void ExitBuildMode()
        {
            ActiveFamily = null;
            if (_open) Close(selectHovered: false);
            BuildFeedbackHud.Show("Building Hammer", "Build mode closed", null, T.TextMuted);
        }

        private void EnterUpgradeMode()
        {
            ActiveFamily = null;
            BuildFeedbackHud.Show("Building Hammer", "Upgrade mode — strike a piece to raise its tier",
                null, T.AccentGold);
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.6f, 0.94f);
        }

        private void SelectFamily(int index)
        {
            if (index < 0 || index >= Families.Length) return;
            var family = Families[index];

            if (!IsAvailable(family))
            {
                BuildFeedbackHud.Show($"{BuildFamilyInfo.DisplayName(family)} unavailable",
                    "No prefab is registered for this piece yet.", null, T.AccentRed);
                AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.5f, 0.72f);
                return;
            }

            ActiveFamily = family;
            bool affordable = CanAffordFamily(family);
            BuildFeedbackHud.Show($"Build: {BuildFamilyInfo.DisplayName(family)}",
                CostSummary(family), null, affordable ? T.AccentCyan : T.AccentRed);
            AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiClick), 0.7f, affordable ? 1.06f : 0.8f);
        }

        private void Hide()
        {
            if (_root == null) return;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
            _dial = null;
            _ring = null;
            _pointer = null;
            _hubIcon = null;
            _hubGlyph = null;
            _hubGroupLabel = null;
            _hubTitle = null;
            _hubDesc = null;
            _hubCostColumn = null;
            _hubHint = null;
            _hubSwapHint = null;
            _iconSlots.Clear();
            _hubCostLines.Clear();
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONSTRUCTION
        // ══════════════════════════════════════════════════════════════════

        private void Build()
        {
            if (_root == null) return;
            _root.Clear();
            _iconSlots.Clear();
            _hubCostLines.Clear();

            // Nothing in the dial is clickable: the cursor is locked, so every
            // interaction runs through the virtual pointer instead of pick events.
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Backdrop);
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            float fit = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 760f, 0.52f, 1f);

            _dial = new VisualElement { name = "BuildDial" };
            _dial.style.width = WheelSize;
            _dial.style.height = WheelSize;
            _dial.style.position = Position.Relative;
            _dial.pickingMode = PickingMode.Ignore;
            _root.Add(_dial);

            BuildRing();
            BuildHub();
            for (int i = 0; i < Families.Length; i++) BuildIconSlot(i);
            BuildPointer();

            // Fast, confident entry: a short ease-out pop, never a slow fade. The
            // selection is already live on frame one — this is decoration only.
            _dial.style.transitionProperty = new List<StylePropertyName> { "scale", "opacity" };
            _dial.style.transitionDuration = new List<TimeValue>
                { new(0.085f, TimeUnit.Second), new(0.085f, TimeUnit.Second) };
            _dial.style.transitionTimingFunction = new List<EasingFunction>
                { new(EasingMode.EaseOutCubic), new(EasingMode.EaseOutCubic) };
            _dial.style.scale = new StyleScale(new Scale(new Vector3(fit * 0.9f, fit * 0.9f, 1f)));
            _dial.style.opacity = 0f;
            _dial.schedule.Execute(() =>
            {
                if (_dial == null) return;
                _dial.style.scale = new StyleScale(new Scale(new Vector3(fit, fit, 1f)));
                _dial.style.opacity = 1f;
            }).ExecuteLater(1);

            RefreshWedges();
            RefreshHub();
        }

        private void BuildRing()
        {
            _ring = new RadialRing
            {
                name = "BuildDialRing",
                SegmentCount = Families.Length,
                InnerRadius = RingInner,
                OuterRadius = RingOuter,
                HairlineOuterRadius = HairOuter,
                HairlineInnerRadius = HairInner,
                HairlineColor = new Color(DialBone.r, DialBone.g, DialBone.b, 0.22f),
                BackdropColor = new Color(0.015f, 0.018f, 0.024f, 0.72f),
                GapPixels = 7f,
                ExpandOuter = 15f,
                ExpandInner = 9f
            };
            _ring.style.position = Position.Absolute;
            _ring.style.left = 0;
            _ring.style.top = 0;
            _ring.style.width = WheelSize;
            _ring.style.height = WheelSize;
            _ring.WedgeProvider = DescribeWedge;
            _dial.Add(_ring);
        }

        private RadialWedge DescribeWedge(int index)
        {
            var wedge = new RadialWedge { RimWidth = 0f };
            if (index < 0 || index >= Families.Length) return wedge;

            var family = Families[index];
            bool hovered = index == _hovered;
            bool chosen = ActiveFamily.HasValue && ActiveFamily.Value == family;

            if (!IsAvailable(family))
            {
                wedge.Fill = DialLocked;
                return wedge;
            }

            if (hovered)
            {
                wedge.Fill = DialHot;
                wedge.Rim = new Color(1f, 0.92f, 0.88f, 0.55f);
                wedge.RimWidth = 1.8f;
                wedge.Expanded = true;
                wedge.Glow = DialHotGlow;
            }
            else if (chosen)
            {
                wedge.Fill = DialChosen;
                wedge.Rim = new Color(DialHot.r, DialHot.g, DialHot.b, 0.8f);
                wedge.RimWidth = 1.6f;
            }
            else
            {
                wedge.Fill = CanAffordFamily(family) ? DialBone : DialBoneWeak;
            }
            return wedge;
        }

        private void BuildIconSlot(int index)
        {
            float angle = RadialWheelInput.SegmentAngle(index, Families.Length) * Mathf.Deg2Rad;
            // UI space is Y-down, so 12 o'clock is -cos.
            float x = WheelSize * 0.5f + Mathf.Sin(angle) * IconRadius;
            float y = WheelSize * 0.5f - Mathf.Cos(angle) * IconRadius;

            var holder = new VisualElement();
            holder.style.position = Position.Absolute;
            holder.style.left = x - IconBox * 0.5f;
            holder.style.top = y - IconBox * 0.5f;
            holder.style.width = IconBox;
            holder.style.height = IconBox;
            holder.pickingMode = PickingMode.Ignore;
            holder.style.backgroundImage = new StyleBackground(BuildPieceIcons.Get(Families[index]));
            holder.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            holder.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            holder.style.transitionProperty = new List<StylePropertyName> { "scale" };
            holder.style.transitionDuration = new List<TimeValue> { new(0.07f, TimeUnit.Second) };
            holder.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic) };
            _dial.Add(holder);
            _iconSlots.Add(holder);
        }

        private void BuildPointer()
        {
            _pointer = new VisualElement { name = "DialPointer" };
            _pointer.style.position = Position.Absolute;
            _pointer.style.width = 13;
            _pointer.style.height = 13;
            _pointer.style.left = WheelSize * 0.5f - 6.5f;
            _pointer.style.top = WheelSize * 0.5f - 6.5f;
            _pointer.style.backgroundColor = new StyleColor(new Color(DialHot.r, DialHot.g, DialHot.b, 0.9f));
            _pointer.pickingMode = PickingMode.Ignore;
            T.Radius(_pointer, 6.5f);
            T.Border(_pointer, 1.5f, new Color(1f, 0.95f, 0.92f, 0.55f));
            _pointer.style.opacity = 0f;
            _dial.Add(_pointer);
        }

        private void BuildHub()
        {
            var hub = new VisualElement { name = "DialHub" };
            hub.style.position = Position.Absolute;
            hub.style.left = (WheelSize - HubSize) * 0.5f;
            hub.style.top = (WheelSize - HubSize) * 0.5f;
            hub.style.width = HubSize;
            hub.style.height = HubSize;
            hub.style.alignItems = Align.Center;
            hub.style.justifyContent = Justify.Center;
            hub.style.paddingLeft = hub.style.paddingRight = 34;
            hub.style.backgroundColor = new StyleColor(HubFace);
            hub.pickingMode = PickingMode.Ignore;
            T.Radius(hub, HubSize * 0.5f);
            T.Border(hub, 1.5f, new Color(DialBone.r, DialBone.g, DialBone.b, 0.14f));
            _dial.Add(hub);

            _hubGroupLabel = Caption(hub, 9, new Color(DialBone.r, DialBone.g, DialBone.b, 0.45f));
            _hubGroupLabel.style.marginBottom = 10;

            var iconWrap = new VisualElement();
            iconWrap.style.width = 74;
            iconWrap.style.height = 74;
            iconWrap.style.alignItems = Align.Center;
            iconWrap.style.justifyContent = Justify.Center;
            iconWrap.pickingMode = PickingMode.Ignore;
            hub.Add(iconWrap);

            _hubIcon = new VisualElement();
            _hubIcon.style.width = 74;
            _hubIcon.style.height = 74;
            _hubIcon.style.position = Position.Absolute;
            _hubIcon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            _hubIcon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            _hubIcon.pickingMode = PickingMode.Ignore;
            iconWrap.Add(_hubIcon);

            _hubGlyph = new Label("\u2301");
            _hubGlyph.style.fontSize = 46;
            _hubGlyph.style.color = new StyleColor(T.AccentGold);
            _hubGlyph.style.position = Position.Absolute;
            _hubGlyph.pickingMode = PickingMode.Ignore;
            iconWrap.Add(_hubGlyph);

            _hubTitle = new Label("UPGRADE MODE");
            _hubTitle.style.fontSize = 23;
            _hubTitle.style.marginTop = 8;
            _hubTitle.style.letterSpacing = 1.5f;
            _hubTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _hubTitle.style.color = new StyleColor(new Color(0.98f, 0.97f, 0.95f));
            _hubTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hubTitle.style.whiteSpace = WhiteSpace.NoWrap;
            _hubTitle.pickingMode = PickingMode.Ignore;
            hub.Add(_hubTitle);

            _hubDesc = new Label();
            _hubDesc.style.fontSize = 11;
            _hubDesc.style.marginTop = 4;
            _hubDesc.style.maxWidth = HubSize - 90f;
            _hubDesc.style.color = new StyleColor(new Color(0.62f, 0.62f, 0.60f));
            _hubDesc.style.unityTextAlign = TextAnchor.MiddleCenter;
            _hubDesc.style.whiteSpace = WhiteSpace.Normal;
            _hubDesc.pickingMode = PickingMode.Ignore;
            hub.Add(_hubDesc);

            _hubCostColumn = new VisualElement();
            _hubCostColumn.style.marginTop = 12;
            _hubCostColumn.style.alignItems = Align.Center;
            _hubCostColumn.pickingMode = PickingMode.Ignore;
            hub.Add(_hubCostColumn);

            for (int i = 0; i < 4; i++)
            {
                var line = new Label();
                line.style.fontSize = 12;
                line.style.unityFontStyleAndWeight = FontStyle.Bold;
                line.style.unityTextAlign = TextAnchor.MiddleCenter;
                line.style.whiteSpace = WhiteSpace.NoWrap;
                line.style.display = DisplayStyle.None;
                line.pickingMode = PickingMode.Ignore;
                _hubCostColumn.Add(line);
                _hubCostLines.Add(line);
            }

            _hubHint = Caption(hub, 9, new Color(DialBone.r, DialBone.g, DialBone.b, 0.32f));
            _hubHint.style.marginTop = 14;
            _hubHint.text = _pinned ? "PINNED  ·  LEFT CLICK TO CONFIRM  ·  ESC TO EXIT" : DefaultHint;

            _hubSwapHint = Caption(hub, 9, new Color(DialBone.r, DialBone.g, DialBone.b, 0.22f));
            _hubSwapHint.style.marginTop = 3;
        }

        private static Label Caption(VisualElement parent, int size, Color color)
        {
            var label = new Label();
            label.style.fontSize = size;
            label.style.letterSpacing = 1.4f;
            label.style.color = new StyleColor(color);
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.pickingMode = PickingMode.Ignore;
            parent.Add(label);
            return label;
        }

        // ══════════════════════════════════════════════════════════════════
        //  LIVE REFRESH
        // ══════════════════════════════════════════════════════════════════

        private void OnHoverChanged()
        {
            RefreshWedges();
            RefreshHub();
            if (_hovered >= 0) PlayTick(Random.Range(1.02f, 1.10f));
        }

        private void RefreshWedges()
        {
            _ring?.Repaint();

            for (int i = 0; i < _iconSlots.Count && i < Families.Length; i++)
            {
                var slot = _iconSlots[i];
                if (slot == null) continue;
                var family = Families[i];
                bool hovered = i == _hovered;
                bool chosen = ActiveFamily.HasValue && ActiveFamily.Value == family;
                bool available = IsAvailable(family);

                Color tint = !available ? IconBroke
                    : hovered ? IconHot
                    : chosen ? IconChosen
                    : CanAffordFamily(family) ? IconIdle
                    : new Color(IconIdle.r, IconIdle.g, IconIdle.b, 0.35f);

                slot.style.unityBackgroundImageTintColor = new StyleColor(tint);
                slot.style.scale = new StyleScale(new Scale(
                    hovered ? new Vector3(1.22f, 1.22f, 1f) : Vector3.one));
            }
        }

        private void RefreshHub()
        {
            if (_hubTitle == null) return;

            _hubGroupLabel.text = _group == BuildFamilyGroup.OrbitalStation
                ? "ORBITAL STATION" : "STRUCTURAL";
            // The swap line only advertises the station set once it is actually
            // researched, so it reads as a discovery rather than a greyed-out tease.
            _hubSwapHint.text = StationGroupUnlocked
                ? "TAB / SCROLL  ·  " + (_group == BuildFamilyGroup.OrbitalStation
                    ? "STRUCTURAL" : "ORBITAL STATION")
                : string.Empty;

            bool hasHover = _hovered >= 0 && _hovered < Families.Length;
            if (!hasHover && !ActiveFamily.HasValue)
            {
                _hubIcon.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                _hubGlyph.style.display = DisplayStyle.Flex;
                _hubTitle.text = "UPGRADE MODE";
                _hubTitle.style.color = new StyleColor(T.AccentGold);
                _hubDesc.text = "Left click to arm the hammer for tier upgrades";
                SetCostLines(null);
                return;
            }

            var family = hasHover ? Families[_hovered] : ActiveFamily.Value;
            _hubGlyph.style.display = DisplayStyle.None;
            _hubIcon.style.backgroundImage = new StyleBackground(BuildPieceIcons.Get(family));
            _hubIcon.style.unityBackgroundImageTintColor = new StyleColor(
                IsAvailable(family) ? (hasHover ? DialHot : IconChosen) : IconBroke);

            _hubTitle.text = BuildFamilyInfo.DisplayName(family);
            _hubTitle.style.color = new StyleColor(new Color(0.98f, 0.97f, 0.95f));
            _hubDesc.text = IsAvailable(family)
                ? BuildFamilyInfo.Description(family)
                : "No prefab registered — run the Voxel Engine Setup tool";
            SetCostLines(family);
        }

        /// <summary>
        /// Writes the place cost into the pre-allocated hub lines. Each line reads
        /// "50 x Wood (985,703)" — the requirement, then what is actually carried.
        /// </summary>
        private void SetCostLines(BuildFamily? family)
        {
            foreach (var line in _hubCostLines) line.style.display = DisplayStyle.None;
            if (!family.HasValue) return;

            var def = registry != null ? registry.Get(family.Value) : null;
            if (def?.placeCost?.items == null || def.placeCost.items.Length == 0)
            {
                var free = _hubCostLines[0];
                free.style.display = DisplayStyle.Flex;
                free.text = "NO COST";
                free.style.color = new StyleColor(T.AccentGreen);
                return;
            }

            int used = 0;
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (used >= _hubCostLines.Count) break;
                int stock = inventory != null ? inventory.container.CountOf(ingredient.item) : 0;
                var line = _hubCostLines[used++];
                line.style.display = DisplayStyle.Flex;
                line.text = $"{ingredient.count} x {ingredient.item.displayName}  ({stock:N0})";
                line.style.color = new StyleColor(stock >= ingredient.count ? T.AccentGreen : T.AccentRed);
            }

            if (used == 0)
            {
                var free = _hubCostLines[0];
                free.style.display = DisplayStyle.Flex;
                free.text = "NO COST";
                free.style.color = new StyleColor(T.AccentGreen);
            }
        }

        /// <summary>
        /// Rides the bead out to the groove between the hub and the wedges, reaching
        /// it exactly as the pointer leaves the deadzone — so the player can see the
        /// moment the selection arms rather than having to guess at it.
        /// </summary>
        private void UpdatePointer()
        {
            if (_pointer == null) return;
            Vector2 n = _input.Normalized;
            float engage = Mathf.Clamp01(_input.Deflection / Mathf.Max(0.001f, _input.Deadzone));
            Vector2 dir = n.sqrMagnitude > 1e-8f ? n.normalized : Vector2.up;
            float radius = PointerReach * engage;

            _pointer.style.translate = new StyleTranslate(new Translate(
                new Length(dir.x * radius, LengthUnit.Pixel),
                new Length(-dir.y * radius, LengthUnit.Pixel), 0f));
            _pointer.style.opacity = engage * 0.9f;
        }

        // ══════════════════════════════════════════════════════════════════
        //  QUERIES
        // ══════════════════════════════════════════════════════════════════

        private bool IsAvailable(BuildFamily family)
        {
            if (registry == null) return false;
            var def = registry.Get(family);
            return def != null && def.GetPrefab(BuildTier.Wood) != null;
        }

        private bool CanAffordFamily(BuildFamily family)
        {
            if (registry == null || inventory == null) return false;
            var def = registry.Get(family);
            if (def?.placeCost?.items == null) return true;
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (inventory.container.CountOf(ingredient.item) < ingredient.count) return false;
            }
            return true;
        }

        private string CostSummary(BuildFamily family)
        {
            var def = registry != null ? registry.Get(family) : null;
            if (def?.placeCost?.items == null) return "Free";
            var builder = new System.Text.StringBuilder();
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (builder.Length > 0) builder.Append(", ");
                builder.Append($"{ingredient.count} {ingredient.item.displayName}");
            }
            return builder.Length == 0 ? "Free" : builder.ToString();
        }

        private static void PlayTick(float pitch)
            => AudioManager.PlayUI(SfxLibrary.Get(Sfx.UiHover), 0.42f, pitch);

        // ══════════════════════════════════════════════════════════════════
        //  RAW INPUT SHIMS
        // ══════════════════════════════════════════════════════════════════

        private static bool TabPressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.tabKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Tab);
#endif
        }

        private static bool LeftMousePressed()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static float ReadScroll()
        {
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }
    }
}
