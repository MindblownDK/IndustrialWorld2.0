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
            BuildFamily.Doorway, BuildFamily.Window, BuildFamily.Stairs,
            BuildFamily.Roof, BuildFamily.Pillar, BuildFamily.HalfWall
        };

        private static readonly string[] Icons =
        {
            "▣", "▥", "▤", "⊡", "☐", "⟋", "⌂", "▏", "▤"
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
            _root = _document.rootVisualElement;
            _root.style.flexGrow = 1;
            Hide();
        }

        private void Start()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            if (registry == null) registry = Resources.Load<TieredBlockRegistry>("TieredBlockRegistry");
        }

        private void Update()
        {
            if (inventory == null) inventory = FindAnyObjectByType<Inventory>();
            var stack = inventory != null ? inventory.ActiveStack : null;
            bool holdingHammer = stack != null && !stack.IsEmpty && stack.item is Hammer;

            if (!holdingHammer)
            {
                ActiveFamily = null;
                if (_open) Close();
                return;
            }

            if (!VoxelEngine.UI.UIState.PauseConsumedThisFrame && GameSettings.WasPressed(InputAction.Pause))
            {
                ExitBuildMode();
                VoxelEngine.UI.UIState.PauseConsumedFrame = Time.frameCount;
                return;
            }

            if (GameSettings.WasPressed(InputAction.BuildWheel))
            {
                if (_open) Close(); else Open();
            }

            if (!_open) return;
            HandlePageScroll();
            UpdateParallax();
        }

        private void OnDisable()
        {
            if (_open) Close();
            ReleaseRingTexture();
        }

        private void OnDestroy()
        {
            if (_open) Close();
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

        public void Close()
        {
            if (!_open) return;
            _open = false;
            VoxelEngine.UI.UIState.PopBlock();
            Hide();
        }

        public void ExitBuildMode()
        {
            ActiveFamily = null;
            if (_open) Close();
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
            _root.style.backgroundColor = new StyleColor(new Color(0.008f, 0.012f, 0.02f, 0.66f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            _wheelCenter = new VisualElement();
            _wheelCenter.style.width = 520;
            _wheelCenter.style.height = 520;
            _wheelCenter.style.position = Position.Relative;
            _wheelCenter.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "translate" };
            _wheelCenter.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.08f, TimeUnit.Second) };
            _root.Add(_wheelCenter);

            System.Array.Clear(_segmentLabels, 0, _segmentLabels.Length);
            BuildRing();
            BuildCenterDisc();
            for (int i = 0; i < PageSize; i++) BuildSegmentLabel(i);
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
            _ringElement.pickingMode = PickingMode.Position;
            _wheelCenter.Add(_ringElement);
            RefreshRingTexture();

            _ringElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                int segment = SegmentAt(evt.localPosition);
                if (segment == _hoveredSegment) return;
                _hoveredSegment = segment;
                RefreshRingTexture();
                RefreshSegmentLabels();
            });
            _ringElement.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_hoveredSegment < 0) return;
                _hoveredSegment = -1;
                RefreshRingTexture();
                RefreshSegmentLabels();
            });
            _ringElement.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                int segment = SegmentAt(evt.localPosition);
                int familyIndex = FamilyIndex(segment);
                if (familyIndex < 0) return;
                SelectFamily(familyIndex);
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(_ringElement);
        }

        private void BuildCenterDisc()
        {
            var disc = new VisualElement();
            disc.style.position = Position.Absolute;
            disc.style.left = 105;
            disc.style.top = 105;
            disc.style.width = 310;
            disc.style.height = 310;
            disc.style.alignItems = Align.Center;
            disc.style.justifyContent = Justify.Center;
            disc.style.backgroundColor = new StyleColor(new Color(0.025f, 0.035f, 0.055f, 0.99f));
            disc.pickingMode = PickingMode.Position;
            T.Radius(disc, 155f);
            T.Border(disc, 2f, T.BorderBright);
            _wheelCenter.Add(disc);

            var icon = new Label("⌁");
            icon.style.fontSize = 34;
            icon.style.color = new StyleColor(ActiveFamily.HasValue ? T.AccentCyan : T.AccentGold);
            icon.pickingMode = PickingMode.Ignore;
            disc.Add(icon);

            var title = new Label(ActiveFamily.HasValue ? ActiveFamily.Value.ToString().ToUpperInvariant() : "UPGRADE MODE");
            title.style.fontSize = 16;
            title.style.marginTop = 5;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextPrimary);
            title.pickingMode = PickingMode.Ignore;
            disc.Add(title);

            var page = new Label($"PAGE {_page + 1}/{PageCount}  ·  SCROLL TO BROWSE");
            page.style.fontSize = 9;
            page.style.marginTop = 7;
            page.style.letterSpacing = 1f;
            page.style.color = new StyleColor(T.TextMuted);
            page.pickingMode = PickingMode.Ignore;
            disc.Add(page);

            var hint = new Label("CLICK CENTER FOR UPGRADE MODE");
            hint.style.fontSize = 8;
            hint.style.marginTop = 5;
            hint.style.color = new StyleColor(T.TextMuted);
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
            const float radius = 203f;
            float angle = (-90f + segment * (360f / PageSize)) * Mathf.Deg2Rad;
            var root = new VisualElement();
            root.style.position = Position.Absolute;
            root.style.left = center + Mathf.Cos(angle) * radius - 42f;
            root.style.top = center + Mathf.Sin(angle) * radius - 34f;
            root.style.width = 84;
            root.style.height = 68;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.pickingMode = PickingMode.Ignore;
            root.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "scale", "background-color" };
            root.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            T.Radius(root, 28f);
            _wheelCenter.Add(root);
            _segmentLabels[segment] = root;

            var icon = new Label(Icons[index]);
            icon.name = "Icon";
            icon.style.fontSize = 20;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.pickingMode = PickingMode.Ignore;
            root.Add(icon);

            var name = new Label(Families[index].ToString().ToUpperInvariant());
            name.name = "Name";
            name.style.fontSize = 8;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.unityTextAlign = TextAnchor.MiddleCenter;
            name.style.whiteSpace = WhiteSpace.Normal;
            name.pickingMode = PickingMode.Ignore;
            root.Add(name);

            var cost = new Label(GetCostText(Families[index]).Replace("Cost: ", string.Empty));
            cost.name = "Cost";
            cost.style.fontSize = 7;
            cost.style.marginTop = 1;
            cost.style.unityTextAlign = TextAnchor.MiddleCenter;
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
                root.style.scale = new StyleScale(new Scale(hovered ? new Vector3(1.10f, 1.10f, 1f) : Vector3.one));
                root.style.backgroundColor = new StyleColor(hovered ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.16f) : Color.clear);
            }
        }

        private int SegmentAt(Vector2 localPosition)
        {
            Vector2 delta = localPosition - new Vector2(240f, 240f);
            float radius = delta.magnitude;
            if (radius < 155f || radius > 232f) return -1;
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

        private void SelectFamily(int familyIndex)
        {
            var family = Families[familyIndex];
            ActiveFamily = family;
            string cost = GetCostText(family);
            VoxelEngine.UI.BuildFeedbackHud.Show($"Build: {family}", cost, null,
                CanAffordFamily(family) ? T.AccentCyan : T.AccentRed);
            Build();
            _root.schedule.Execute(Close).ExecuteLater(140);
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
            const float innerRadius = 82f;
            const float outerRadius = 123f;
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
                    Color32 color = selected
                        ? new Color32(22, 157, 220, 250)
                        : (hovered ? new Color32(70, 188, 232, 252) : new Color32(220, 218, 211, 246));
                    float edge = Mathf.Min(radius - innerRadius, outerRadius - radius);
                    color.a = (byte)Mathf.RoundToInt(color.a * Mathf.Clamp01(edge));
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
            if (registry == null) return "Free";
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
