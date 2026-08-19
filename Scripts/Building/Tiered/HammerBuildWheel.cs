// Assets/Scripts/VoxelEngine/Building/Tiered/HammerBuildWheel.cs

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Settings;
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

        private static readonly BuildFamily[] Families =
        {
            BuildFamily.Foundation, BuildFamily.Wall, BuildFamily.Floor,
            BuildFamily.Doorway, BuildFamily.Door, BuildFamily.Window,
            BuildFamily.Stairs, BuildFamily.Roof, BuildFamily.Pillar,
            BuildFamily.HalfWall
        };

        private static readonly string[] Icons =
        {
            "▣", "▥", "▤", "⊡", "▯", "☐", "⟋", "⌂", "▏", "▤"
        };

        private const int PageSize = 8;
        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _wheelCenter;
        private VisualElement _ringElement;
        private Texture2D _ringTexture;
        private readonly VisualElement[] _segmentLabels = new VisualElement[PageSize];
        private int _page;
        private int _hoveredSegment = -1;
        private bool _open;
        private bool _wasWheelHeld;
        private Vector2 _parallax;
        private float _nextPageInput;

        private int PageCount => Mathf.Max(1, Mathf.CeilToInt(Families.Length / (float)PageSize));

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
                VoxelEngine.Settings.GameSettings.ApplyUiScaleAndFit(_document.panelSettings);
            }
            _root = _document.rootVisualElement;
            _root.style.flexGrow = 1;
            Hide();
        }

        private void Start()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (registry == null) registry = BuildSystemV2.Instance != null ? BuildSystemV2.Instance.registry : null;
            if (registry == null) registry = Resources.Load<TieredBlockRegistry>("TieredBlockRegistry");
        }

        private void Update()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (registry == null && BuildSystemV2.Instance != null) registry = BuildSystemV2.Instance.registry;
            var stack = inventory != null ? inventory.ActiveStack : null;
            bool holdingHammer = stack != null && !stack.IsEmpty && stack.item is Hammer;

            if (!holdingHammer)
            {
                ActiveFamily = null;
                _wasWheelHeld = false;
                if (_open) Close(selectHovered: false);
                return;
            }

            if (!VoxelEngine.UI.UIState.PauseConsumedThisFrame && GameSettings.WasPressed(InputAction.Pause))
            {
                ExitBuildMode();
                VoxelEngine.UI.UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            bool wheelHeld = GameSettings.IsHeld(InputAction.BuildWheel);
            if (wheelHeld && !_open) Open();
            if (!wheelHeld && _wasWheelHeld && _open) Close(selectHovered: true);
            _wasWheelHeld = wheelHeld;

            if (!_open) return;
            HandlePageScroll();
            UpdateParallax();
        }

        private void OnDisable()
        {
            if (_open) Close(selectHovered: false);
            ReleaseRingTexture();
        }

        private void OnDestroy()
        {
            if (_open) Close(selectHovered: false);
            ReleaseRingTexture();
            if (Instance == this) Instance = null;
        }

        public void Open()
        {
            if (_open) return;
            _open = true;
            _parallax = Vector2.zero;
            VoxelEngine.UI.UIState.PushBlock();
            Build();
        }

        public void Close(bool selectHovered = false)
        {
            if (!_open) return;
            if (selectHovered) SelectHoveredSegment();
            _open = false;
            VoxelEngine.UI.UIState.PopBlock();
            Hide();
        }

        public void ExitBuildMode()
        {
            ActiveFamily = null;
            if (_open) Close(selectHovered: false);
            VoxelEngine.UI.BuildFeedbackHud.Show("Building Hammer", "Build mode closed", null, T.TextMuted);
        }

        private void Hide()
        {
            if (_root == null) return;
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(Color.clear);
            _wheelCenter = null;
            _ringElement = null;
            _hoveredSegment = -1;
            ReleaseRingTexture();
        }

        private void Build()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.82f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            _wheelCenter = new VisualElement();
            _wheelCenter.style.width = 560;
            _wheelCenter.style.height = 560;
            _wheelCenter.style.position = Position.Relative;
            _wheelCenter.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "translate", "scale" };
            _wheelCenter.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.08f, TimeUnit.Second) };
            float safeScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 640f, 0.55f, 1f);
            _wheelCenter.style.scale = new StyleScale(new Scale(new Vector3(safeScale, safeScale, 1f)));
            _root.Add(_wheelCenter);

            System.Array.Clear(_segmentLabels, 0, _segmentLabels.Length);
            BuildRing();
            BuildCenterDisc();
            for (int i = 0; i < PageSize; i++) BuildSegmentLabel(i);
            RegisterWheelPointerEvents();
            RefreshSegmentLabels();
        }

        private void BuildRing()
        {
            _ringElement = new VisualElement { name = "HammerBuildRing" };
            _ringElement.style.position = Position.Absolute;
            _ringElement.style.left = 20;
            _ringElement.style.top = 20;
            _ringElement.style.width = 480;
            _ringElement.style.height = 480;
            _ringElement.pickingMode = PickingMode.Ignore;
            _wheelCenter.Add(_ringElement);
            RefreshRingTexture();
        }

        private void RegisterWheelPointerEvents()
        {
            _wheelCenter.RegisterCallback<PointerMoveEvent>(evt =>
            {
                int segment = SegmentAt(_wheelCenter.WorldToLocal(evt.position));
                if (segment == _hoveredSegment) return;
                _hoveredSegment = segment;
                RefreshRingTexture();
                RefreshSegmentLabels();
            });
            _wheelCenter.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_hoveredSegment < 0) return;
                _hoveredSegment = -1;
                RefreshRingTexture();
                RefreshSegmentLabels();
            });
            _wheelCenter.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                int segment = SegmentAt(_wheelCenter.WorldToLocal(evt.position));
                int familyIndex = FamilyIndex(segment);
                if (familyIndex < 0) return;
                SelectFamily(familyIndex);
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(_wheelCenter);
        }

        private void BuildCenterDisc()
        {
            var disc = new VisualElement();
            disc.style.position = Position.Absolute;
            disc.style.left = 115;
            disc.style.top = 115;
            disc.style.width = 290;
            disc.style.height = 290;
            disc.style.alignItems = Align.Center;
            disc.style.justifyContent = Justify.Center;
            disc.style.backgroundColor = new StyleColor(new Color(0.025f, 0.035f, 0.055f, 0.99f));
            disc.pickingMode = PickingMode.Position;
            T.Radius(disc, 145f);
            T.Border(disc, 2f, T.BorderBright);
            _wheelCenter.Add(disc);

            var icon = new Label("⌁");
            icon.style.fontSize = 38;
            icon.style.color = new StyleColor(ActiveFamily.HasValue ? T.AccentCyan : T.AccentGold);
            icon.pickingMode = PickingMode.Ignore;
            disc.Add(icon);

            var title = new Label(ActiveFamily.HasValue ? ActiveFamily.Value.ToString().ToUpperInvariant() : "UPGRADE MODE");
            title.style.fontSize = 17;
            title.style.marginTop = 4;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextPrimary);
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.pickingMode = PickingMode.Ignore;
            disc.Add(title);

            var page = new Label($"PAGE {_page + 1}/{PageCount}  ·  SCROLL TO BROWSE");
            page.style.fontSize = 10;
            page.style.marginTop = 6;
            page.style.letterSpacing = 1f;
            page.style.color = new StyleColor(T.TextMuted);
            page.style.whiteSpace = WhiteSpace.NoWrap;
            page.pickingMode = PickingMode.Ignore;
            disc.Add(page);

            var hint = new Label("CLICK CENTER FOR UPGRADE MODE");
            hint.style.fontSize = 9;
            hint.style.marginTop = 4;
            hint.style.color = new StyleColor(T.TextMuted);
            hint.style.whiteSpace = WhiteSpace.NoWrap;
            hint.pickingMode = PickingMode.Ignore;
            disc.Add(hint);

            disc.RegisterCallback<PointerEnterEvent>(_ =>
                disc.style.backgroundColor = new StyleColor(new Color(0.045f, 0.065f, 0.095f, 1f)));
            disc.RegisterCallback<PointerLeaveEvent>(_ =>
                disc.style.backgroundColor = new StyleColor(new Color(0.025f, 0.035f, 0.055f, 0.99f)));
            disc.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                ActiveFamily = null;
                Build();
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(disc);
        }

        private void BuildSegmentLabel(int segment)
        {
            int index = FamilyIndex(segment);
            if (index < 0) return;

            const float center = 260f;
            const float radius = 195f;
            float angle = (-90f + segment * (360f / PageSize)) * Mathf.Deg2Rad;
            var root = new VisualElement();
            root.style.position = Position.Absolute;
            root.style.left = center + Mathf.Cos(angle) * radius - 40f;
            root.style.top = center + Mathf.Sin(angle) * radius - 34f;
            root.style.width = 80;
            root.style.height = 68;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.overflow = Overflow.Visible;
            root.pickingMode = PickingMode.Ignore;
            root.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "scale", "background-color" };
            root.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            T.Radius(root, 30f);
            _wheelCenter.Add(root);
            _segmentLabels[segment] = root;

            var icon = new Label(Icons[index]);
            icon.name = "Icon";
            icon.style.fontSize = 22;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.pickingMode = PickingMode.Ignore;
            root.Add(icon);

            var name = new Label(Families[index].ToString().ToUpperInvariant());
            name.name = "Name";
            name.style.fontSize = 8;
            name.style.maxWidth = 76;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Visible;
            name.pickingMode = PickingMode.Ignore;
            root.Add(name);

            var cost = new Label(GetCostText(Families[index]).Replace("Cost: ", string.Empty));
            cost.name = "Cost";
            cost.style.fontSize = 7;
            cost.style.maxWidth = 76;
            cost.style.marginTop = 1;
            cost.style.unityTextAlign = TextAnchor.MiddleCenter;
            cost.style.whiteSpace = WhiteSpace.NoWrap;
            cost.style.overflow = Overflow.Visible;
            cost.pickingMode = PickingMode.Ignore;
            root.Add(cost);
        }

        private void RefreshSegmentLabels()
        {
            for (int segment = 0; segment < PageSize; segment++)
            {
                var root = _segmentLabels[segment];
                int index = FamilyIndex(segment);
                if (root == null || index < 0) continue;
                bool selected = ActiveFamily == Families[index];
                bool hovered = segment == _hoveredSegment;
                Color foreground = selected ? Color.white : (hovered ? T.AccentCyan : new Color(0.16f, 0.18f, 0.20f));
                var icon = root.Q<Label>("Icon");
                var name = root.Q<Label>("Name");
                var cost = root.Q<Label>("Cost");
                if (icon != null) icon.style.color = new StyleColor(foreground);
                if (name != null) name.style.color = new StyleColor(foreground);
                if (cost != null) cost.style.color = new StyleColor(CanAffordFamily(Families[index]) ? T.AccentGreen : T.AccentRed);
                root.style.scale = new StyleScale(new Scale(hovered ? new Vector3(1.035f, 1.035f, 1f) : Vector3.one));
                root.style.backgroundColor = new StyleColor(hovered ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.16f) : Color.clear);
            }
        }

        private int SegmentAt(Vector2 localPosition)
        {
            Vector2 delta = localPosition - new Vector2(260f, 260f);
            float radius = delta.magnitude;
            if (radius < 154f || radius > 232f) return -1;
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float segmentAngle = 360f / PageSize;
            float normalized = Mathf.Repeat(angle + 90f + segmentAngle * 0.5f, 360f);
            float within = normalized % segmentAngle;
            if (within < 2.2f || within > segmentAngle - 2.2f) return -1;
            int segment = Mathf.FloorToInt(normalized / segmentAngle);
            return FamilyIndex(segment) >= 0 ? segment : -1;
        }

        private int FamilyIndex(int segment)
        {
            if (segment < 0 || segment >= PageSize) return -1;
            int index = _page * PageSize + segment;
            return index >= 0 && index < Families.Length ? index : -1;
        }

        private void SelectHoveredSegment()
        {
            int familyIndex = FamilyIndex(_hoveredSegment);
            if (familyIndex < 0) return;
            SelectFamily(familyIndex, closeAfterSelect: false);
        }

        private void SelectFamily(int familyIndex, bool closeAfterSelect = true)
        {
            var family = Families[familyIndex];
            ActiveFamily = family;
            string cost = GetCostText(family);
            VoxelEngine.UI.BuildFeedbackHud.Show($"Build: {family}", cost, null,
                CanAffordFamily(family) ? T.AccentCyan : T.AccentRed);
            Build();
            if (closeAfterSelect) _root.schedule.Execute(() => Close(selectHovered: false)).ExecuteLater(140);
        }

        private void HandlePageScroll()
        {
            if (PageCount <= 1 || Time.unscaledTime < _nextPageInput) return;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            float scroll = UnityEngine.InputSystem.Mouse.current != null
                ? UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y
                : 0f;
#else
            float scroll = Input.mouseScrollDelta.y;
#endif
            if (Mathf.Abs(scroll) < 0.01f) return;
            _nextPageInput = Time.unscaledTime + 0.12f;
            _page += scroll < 0f ? 1 : -1;
            if (_page < 0) _page = PageCount - 1;
            if (_page >= PageCount) _page = 0;
            _hoveredSegment = -1;
            Build();
        }

        private void UpdateParallax()
        {
            if (_wheelCenter == null) return;
#if ENABLE_INPUT_SYSTEM || VE_HAS_INPUT_SYSTEM
            var mouse = UnityEngine.InputSystem.Mouse.current;
            Vector2 position = mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#else
            Vector2 position = Input.mousePosition;
#endif
            Vector2 fromCenter = position - new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 target = Vector2.ClampMagnitude(fromCenter * 0.025f, 14f);
            _parallax = Vector2.Lerp(_parallax, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            _wheelCenter.style.translate = new StyleTranslate(new Translate(
                new Length(_parallax.x, LengthUnit.Pixel),
                new Length(-_parallax.y, LengthUnit.Pixel), 0f));
        }

        private void RefreshRingTexture()
        {
            const int size = 256;
            if (_ringTexture == null)
            {
                _ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "HammerBuildWheelRing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            const float innerRadius = 78f;
            const float outerRadius = 128f;
            float segmentAngle = 360f / PageSize;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = center - y;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    if (radius < innerRadius || radius > outerRadius) continue;
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float normalized = Mathf.Repeat(angle + 90f + segmentAngle * 0.5f, 360f);
                    float within = normalized % segmentAngle;
                    if (within < 2.2f || within > segmentAngle - 2.2f) continue;
                    int segment = Mathf.FloorToInt(normalized / segmentAngle);
                    int index = FamilyIndex(segment);
                    if (index < 0) continue;

                    bool selected = ActiveFamily == Families[index];
                    bool hovered = segment == _hoveredSegment;

                    // Premium cream/off-white ring like reference, with red accents for selected
                    Color32 baseCream = new Color32(245, 242, 232, 255);
                    Color32 hoverCream = new Color32(255, 250, 235, 255);
                    Color32 selectedRed = new Color32(215, 55, 45, 255);
                    Color32 hoverAccent = new Color32(245, 120, 80, 255);

                    Color32 color;
                    if (selected)
                        color = selectedRed;
                    else if (hovered)
                        color = hoverAccent;
                    else
                        color = baseCream;

                    // Subtle bevel / thickness effect
                    float edge = Mathf.Min(radius - innerRadius, outerRadius - radius);
                    float alphaFade = Mathf.Clamp01(edge / 8f);
                    color.a = (byte)Mathf.RoundToInt(255 * alphaFade * (selected || hovered ? 1f : 0.96f));

                    // Add thin outer/inner rim for premium depth
                    if (edge < 4f) color = new Color32(180, 175, 160, (byte)(color.a * 0.7f));

                    pixels[y * size + x] = color;
                }
            }

            _ringTexture.SetPixels32(pixels);
            _ringTexture.Apply(false, false);
            if (_ringElement != null)
                _ringElement.style.backgroundImage = new StyleBackground(_ringTexture);
        }

        private void ReleaseRingTexture()
        {
            if (_ringTexture == null) return;
            Destroy(_ringTexture);
            _ringTexture = null;
        }

        private string GetCostText(BuildFamily family)
        {
            if (registry == null) return "Registry unavailable";
            var def = registry.Get(family);
            if (def == null || def.placeCost?.items == null) return "Free";
            var builder = new System.Text.StringBuilder("Cost: ");
            bool first = true;
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (!first) builder.Append(", ");
                builder.Append($"{ingredient.count} {ingredient.item.displayName}");
                first = false;
            }
            return first ? "Free" : builder.ToString();
        }

        private bool CanAffordFamily(BuildFamily family)
        {
            if (registry == null || inventory == null) return false;
            var def = registry.Get(family);
            if (def == null || def.placeCost?.items == null) return true;
            foreach (var ingredient in def.placeCost.items)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                if (inventory.container.CountOf(ingredient.item) < ingredient.count) return false;
            }
            return true;
        }
    }
}
