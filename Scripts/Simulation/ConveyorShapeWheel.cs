// Assets/Scripts/VoxelEngine/Simulation/ConveyorShapeWheel.cs

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.Simulation
{
    public enum ConveyorBuildMode
    {
        Straight,
        Ramp,
        Vertical
    }

    /// <summary>
    /// Contextual hold-to-open radial selector for the three conveyor build modes.
    /// The held conveyor item supplies the speed tier, so one item and recipe own
    /// every conveyor shape available at that tier.
    /// </summary>
    public sealed class ConveyorShapeWheel : MonoBehaviour
    {
        private static readonly ConveyorBuildMode[] SelectedByTier =
        {
            ConveyorBuildMode.Straight,
            ConveyorBuildMode.Straight,
            ConveyorBuildMode.Straight
        };

        private static readonly ConveyorBuildMode[] Modes =
        {
            ConveyorBuildMode.Straight,
            ConveyorBuildMode.Ramp,
            ConveyorBuildMode.Vertical
        };

        private static readonly string[] Icons = { "→", "↗", "↑" };

        private Inventory _inventory;
        private VisualElement _uiRoot;
        private VisualElement _prompt;
        private Label _promptLabel;
        private VisualElement _wheelOverlay;
        private VisualElement _wheelCenter;
        private VisualElement _ringElement;
        private Texture2D _ringTexture;
        private readonly VisualElement[] _segmentLabelRoots = new VisualElement[3];
        private readonly Label[] _segmentIcons = new Label[3];
        private readonly Label[] _segmentNames = new Label[3];
        private int _hoveredSegment = -1;
        private bool _open;
        private ConveyorSpeed _activeTier;
        private Vector2 _parallax;

        public static ConveyorBuildMode GetMode(ConveyorSpeed tier)
        {
            return SelectedByTier[Mathf.Clamp((int)tier, 0, SelectedByTier.Length - 1)];
        }

        private static void SetMode(ConveyorSpeed tier, ConveyorBuildMode mode)
        {
            SelectedByTier[Mathf.Clamp((int)tier, 0, SelectedByTier.Length - 1)] = mode;
        }

        private void Start()
        {
            _inventory = GetComponentInParent<Inventory>();
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
        }

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            bool holdingConveyor = TryGetHeldConveyor(out var belt);

            if (!holdingConveyor)
            {
                if (_open) Close(selectHovered: false);
                HidePrompt();
                return;
            }

            _activeTier = belt.speed;
            bool wheelHeld = GameSettings.IsHeld(InputAction.BuildWheel);
            if (!_open && !UIState.IsBlocking)
            {
                ShowPrompt();
                if (wheelHeld) Open();
            }
            else if (_open)
            {
                HidePrompt();
                if (!wheelHeld) Close(selectHovered: true);
                else
                {
                    if (_wheelOverlay == null || _wheelOverlay.parent == null) BuildWheel();
                    UpdateParallax();
                }
            }
            else
            {
                HidePrompt();
            }
        }

        private void OnDisable()
        {
            if (_open) Close(selectHovered: false);
            RemoveUi();
        }

        private void OnDestroy()
        {
            if (_open) Close(selectHovered: false);
            RemoveUi();
        }

        private bool TryGetHeldConveyor(out ConveyorBelt belt)
        {
            belt = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is BlockItem block) || block.placedPrefab == null)
                return false;

            belt = block.placedPrefab.GetComponentInChildren<ConveyorBelt>(true);
            return belt != null && belt.autoShape && belt.shape == ConveyorShape.Straight;
        }

        private bool EnsureUiRoot()
        {
            if (_uiRoot != null && _uiRoot.panel != null) return true;
            var controller = GameUIController.Instance;
            var document = controller != null ? controller.GetComponent<UIDocument>() : FindAnyObjectByType<UIDocument>();
            if (document == null) return false;
            _uiRoot = document.rootVisualElement;
            return _uiRoot != null;
        }

        private void ShowPrompt()
        {
            if (!EnsureUiRoot()) return;
            if (_prompt == null)
            {
                _prompt = new VisualElement { name = "ConveyorShapePrompt" };
                _prompt.style.position = Position.Absolute;
                _prompt.style.left = 0;
                _prompt.style.right = 0;
                _prompt.style.bottom = 82;
                _prompt.style.alignItems = Align.Center;
                _prompt.pickingMode = PickingMode.Ignore;

                var pill = new VisualElement();
                pill.style.height = 30;
                pill.style.paddingLeft = 12;
                pill.style.paddingRight = 12;
                pill.style.flexDirection = FlexDirection.Row;
                pill.style.alignItems = Align.Center;
                pill.style.justifyContent = Justify.Center;
                pill.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.065f, 0.94f));
                UITheme.Radius(pill, 15f);
                UITheme.Border(pill, 1f, UITheme.BorderBright);
                _prompt.Add(pill);

                _promptLabel = new Label();
                _promptLabel.style.fontSize = 11;
                _promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _promptLabel.style.color = new StyleColor(UITheme.TextPrimary);
                _promptLabel.style.letterSpacing = 0.5f;
                _promptLabel.pickingMode = PickingMode.Ignore;
                pill.Add(_promptLabel);
            }

            if (_prompt.parent == null) _uiRoot.Add(_prompt);
            _prompt.style.display = DisplayStyle.Flex;
            _promptLabel.text = $"[{GameSettings.GetKey(InputAction.BuildWheel)}]  CONVEYOR SHAPE  ·  {GetMode(_activeTier).ToString().ToUpperInvariant()}";
        }

        private void HidePrompt()
        {
            if (_prompt != null) _prompt.style.display = DisplayStyle.None;
        }

        private void Open()
        {
            if (_open || !EnsureUiRoot()) return;
            _open = true;
            _parallax = Vector2.zero;
            UIState.PushBlock();
            BuildWheel();
        }

        private void Close(bool selectHovered = false)
        {
            if (!_open) return;
            if (selectHovered) SelectHoveredSegment();
            _open = false;
            UIState.PopBlock();
            if (_wheelOverlay != null) _wheelOverlay.RemoveFromHierarchy();
            ReleaseRingTexture();
            _wheelOverlay = null;
            _wheelCenter = null;
            _ringElement = null;
            _hoveredSegment = -1;
            ShowPrompt();
        }

        private void BuildWheel()
        {
            if (!EnsureUiRoot()) return;
            if (_wheelOverlay != null) _wheelOverlay.RemoveFromHierarchy();

            _wheelOverlay = new VisualElement { name = "ConveyorShapeWheel" };
            _wheelOverlay.style.position = Position.Absolute;
            _wheelOverlay.style.left = 0;
            _wheelOverlay.style.top = 0;
            _wheelOverlay.style.right = 0;
            _wheelOverlay.style.bottom = 0;
            _wheelOverlay.style.alignItems = Align.Center;
            _wheelOverlay.style.justifyContent = Justify.Center;
            _wheelOverlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.015f, 0.025f, 0.58f));
            _wheelOverlay.pickingMode = PickingMode.Position;
            _uiRoot.Add(_wheelOverlay);

            _wheelCenter = new VisualElement();
            _wheelCenter.style.width = 390;
            _wheelCenter.style.height = 390;
            _wheelCenter.style.position = Position.Relative;
            _wheelCenter.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "translate", "scale" };
            _wheelCenter.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.08f, TimeUnit.Second) };
            float safeScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 460f, 0.68f, 1f);
            _wheelCenter.style.scale = new StyleScale(new Scale(new Vector3(safeScale, safeScale, 1f)));
            _wheelOverlay.Add(_wheelCenter);

            System.Array.Clear(_segmentLabelRoots, 0, _segmentLabelRoots.Length);
            System.Array.Clear(_segmentIcons, 0, _segmentIcons.Length);
            System.Array.Clear(_segmentNames, 0, _segmentNames.Length);
            BuildRing();
            BuildCenterBadge();
            for (int i = 0; i < Modes.Length; i++)
                BuildRingLabel(i, Modes[i], Icons[i]);
            RefreshSegmentLabels();
        }

        private void BuildCenterBadge()
        {
            var badge = new VisualElement();
            badge.style.position = Position.Absolute;
            badge.style.left = 85;
            badge.style.top = 85;
            badge.style.width = 220;
            badge.style.height = 220;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.backgroundColor = new StyleColor(new Color(0.035f, 0.05f, 0.075f, 0.98f));
            UITheme.Radius(badge, 110f);
            UITheme.Border(badge, 2f, UITheme.BorderBright);
            badge.pickingMode = PickingMode.Ignore;
            _wheelCenter.Add(badge);

            var tier = new Label($"{_activeTier.ToString().ToUpperInvariant()} BELT");
            tier.style.fontSize = 10;
            tier.style.letterSpacing = 1.4f;
            tier.style.unityFontStyleAndWeight = FontStyle.Bold;
            tier.style.color = new StyleColor(UITheme.TextMuted);
            tier.pickingMode = PickingMode.Ignore;
            badge.Add(tier);

            var selected = new Label(GetMode(_activeTier).ToString().ToUpperInvariant());
            selected.style.fontSize = 15;
            selected.style.marginTop = 4;
            selected.style.unityFontStyleAndWeight = FontStyle.Bold;
            selected.style.color = new StyleColor(UITheme.AccentCyan);
            selected.pickingMode = PickingMode.Ignore;
            badge.Add(selected);

            var hint = new Label("HOLD TO SELECT");
            hint.style.fontSize = 8;
            hint.style.marginTop = 5;
            hint.style.letterSpacing = 1f;
            hint.style.color = new StyleColor(UITheme.TextMuted);
            hint.pickingMode = PickingMode.Ignore;
            badge.Add(hint);
        }

        private void BuildRing()
        {
            _ringElement = new VisualElement { name = "ConveyorShapeRing" };
            _ringElement.style.position = Position.Absolute;
            _ringElement.style.left = 15;
            _ringElement.style.top = 15;
            _ringElement.style.width = 360;
            _ringElement.style.height = 360;
            _ringElement.pickingMode = PickingMode.Position;
            _wheelCenter.Add(_ringElement);
            RefreshRingTexture();

            _ringElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                int segment = SegmentAt(evt.localPosition);
                if (segment == _hoveredSegment) return;
                _hoveredSegment = segment;
                if (segment >= 0 && segment < Modes.Length)
                    SetMode(_activeTier, Modes[segment]);
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
                if (segment < 0 || segment >= Modes.Length) return;
                SelectSegment(segment);
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(_ringElement);
        }

        private void SelectHoveredSegment()
        {
            if (_hoveredSegment < 0 || _hoveredSegment >= Modes.Length) return;
            SelectSegment(_hoveredSegment);
        }

        private void SelectSegment(int segment)
        {
            if (segment < 0 || segment >= Modes.Length) return;
            var mode = Modes[segment];
            SetMode(_activeTier, mode);
            BuildFeedbackHud.Show("Conveyor Shape", mode.ToString(), null, UITheme.AccentCyan);
            if (_open) BuildWheel();
        }

        private void BuildRingLabel(int index, ConveyorBuildMode mode, string iconText)
        {
            const float center = 195f;
            const float radius = 136f;
            float angle = (-90f + index * 120f) * Mathf.Deg2Rad;

            var labelRoot = new VisualElement();
            labelRoot.style.position = Position.Absolute;
            labelRoot.style.left = center + Mathf.Cos(angle) * radius - 35f;
            labelRoot.style.top = center + Mathf.Sin(angle) * radius - 27f;
            labelRoot.style.width = 70;
            labelRoot.style.height = 54;
            labelRoot.style.alignItems = Align.Center;
            labelRoot.style.justifyContent = Justify.Center;
            labelRoot.style.overflow = Overflow.Hidden;
            labelRoot.pickingMode = PickingMode.Ignore;
            UITheme.Radius(labelRoot, 30f);
            labelRoot.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "scale", "background-color" };
            labelRoot.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            _wheelCenter.Add(labelRoot);
            _segmentLabelRoots[index] = labelRoot;

            bool selected = GetMode(_activeTier) == mode;
            var icon = new Label(iconText);
            icon.style.fontSize = 24;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.color = new StyleColor(selected ? Color.white : new Color(0.16f, 0.18f, 0.20f));
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.pickingMode = PickingMode.Ignore;
            labelRoot.Add(icon);
            _segmentIcons[index] = icon;

            var label = new Label(mode.ToString().ToUpperInvariant());
            label.style.fontSize = 9;
            label.style.marginTop = 1;
            label.style.letterSpacing = 0.8f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(selected ? Color.white : new Color(0.20f, 0.22f, 0.24f));
            label.pickingMode = PickingMode.Ignore;
            labelRoot.Add(label);
            _segmentNames[index] = label;
        }

        private void RefreshSegmentLabels()
        {
            int selected = (int)GetMode(_activeTier);
            for (int i = 0; i < Modes.Length; i++)
            {
                var root = _segmentLabelRoots[i];
                var icon = _segmentIcons[i];
                var label = _segmentNames[i];
                if (root == null || icon == null || label == null) continue;

                bool isSelected = i == selected;
                bool isHovered = i == _hoveredSegment;
                Color foreground = isSelected
                    ? Color.white
                    : (isHovered ? new Color(0.04f, 0.62f, 0.88f) : new Color(0.16f, 0.18f, 0.20f));
                icon.style.color = new StyleColor(foreground);
                label.style.color = new StyleColor(foreground);
                root.style.scale = new StyleScale(new Scale(isHovered
                    ? new Vector3(1.04f, 1.04f, 1f)
                    : Vector3.one));
                root.style.backgroundColor = new StyleColor(isHovered
                    ? new Color(0.10f, 0.68f, 0.92f, 0.18f)
                    : Color.clear);
            }
        }

        private int SegmentAt(Vector2 localPosition)
        {
            Vector2 delta = localPosition - new Vector2(180f, 180f);
            float radius = delta.magnitude;
            if (radius < 115f || radius > 173f) return -1;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float normalized = Mathf.Repeat(angle + 150f, 360f);
            float withinSegment = normalized % 120f;
            if (withinSegment < 3.5f || withinSegment > 116.5f) return -1;
            return Mathf.Clamp(Mathf.FloorToInt(normalized / 120f), 0, Modes.Length - 1);
        }

        private void RefreshRingTexture()
        {
            const int size = 256;
            if (_ringTexture == null)
            {
                _ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "ConveyorShapeWheelRing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            const float innerRadius = 81f;
            const float outerRadius = 123f;
            int selected = (int)GetMode(_activeTier);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = center - y;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    if (radius < innerRadius || radius > outerRadius) continue;

                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float normalized = Mathf.Repeat(angle + 150f, 360f);
                    float withinSegment = normalized % 120f;
                    if (withinSegment < 3.5f || withinSegment > 116.5f) continue;

                    int segment = Mathf.Clamp(Mathf.FloorToInt(normalized / 120f), 0, Modes.Length - 1);
                    Color32 color;
                    if (segment == selected)
                        color = new Color32(22, 157, 220, 250);
                    else if (segment == _hoveredSegment)
                        color = new Color32(70, 188, 232, 252);
                    else
                        color = new Color32(220, 218, 211, 246);

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
            Vector2 target = Vector2.ClampMagnitude(fromCenter * 0.035f, 18f);
            _parallax = Vector2.Lerp(_parallax, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            _wheelCenter.style.translate = new StyleTranslate(new Translate(
                new Length(_parallax.x, LengthUnit.Pixel),
                new Length(-_parallax.y, LengthUnit.Pixel), 0f));
        }

        private void RemoveUi()
        {
            if (_prompt != null) _prompt.RemoveFromHierarchy();
            if (_wheelOverlay != null) _wheelOverlay.RemoveFromHierarchy();
            ReleaseRingTexture();
            _prompt = null;
            _wheelOverlay = null;
            _wheelCenter = null;
            _ringElement = null;
            _hoveredSegment = -1;
        }
    }
}
