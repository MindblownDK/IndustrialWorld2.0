// Assets/Scripts/VoxelEngine/UI/GridShapeWheel.cs
//
// Contextual radial wheel for grid block shape variants (Cube, Slope, Half, etc.).
// Reuses the same premium visual language as the HammerBuildWheel and ConveyorShapeWheel.
// Activated when holding a grid armour / structural block item.
//
// v5.40.0-dev — Clean wheel: each segment shows a short variant label positioned
// directly on the ring surface. Icon colour matches the premium cream-on-dark
// palette of the other wheels — no garish multi-colour dots.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    public enum GridShapeVariant
    {
        Cube,
        Slope,
        HalfBlock,
        HalfSlope,
        Corner,
        InvertedSlope
    }

    public sealed class GridShapeWheel : MonoBehaviour
    {
        private static readonly GridShapeVariant[] Variants =
        {
            GridShapeVariant.Cube,
            GridShapeVariant.Slope,
            GridShapeVariant.HalfBlock,
            GridShapeVariant.HalfSlope,
            GridShapeVariant.Corner,
            GridShapeVariant.InvertedSlope
        };

        private static readonly string[] ShortLabels = { "FULL", "SLOPE", "HALF", "H-SL", "CORNER", "INV" };
        /// <summary>Even shorter single-char icons rendered via the ring texture's gap area.</summary>
        private static readonly string[] IconText = { "\u25A0", "\u25B2", "\u25AE", "\u25B4", "\u25A2", "\u25BC" };

        private static GridShapeVariant _current = GridShapeVariant.Cube;

        public static GridShapeVariant CurrentShape => _current;

        private Inventory _inventory;
        private VisualElement _uiRoot;
        private VisualElement _wheelOverlay;
        private VisualElement _wheelCenter;
        private VisualElement _ringElement;
        private Texture2D _ringTexture;
        private readonly VisualElement[] _segmentRoots = new VisualElement[6];
        private readonly Label[] _segmentIcons = new Label[6];
        private readonly Label[] _segmentNames = new Label[6];
        private int _hoveredSegment = -1;
        private bool _open;

        private void Start()
        {
            _inventory = GetComponentInParent<Inventory>();
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
        }

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            bool holdingGridArmor = TryGetHeldGridArmor(out _);

            if (!holdingGridArmor)
            {
                if (_open) Close(selectHovered: false);
                return;
            }

            bool wheelHeld = GameSettings.IsHeld(InputAction.BuildWheel);
            if (!_open && !UIState.IsBlocking)
            {
                if (wheelHeld) Open();
            }
            else if (_open)
            {
                if (!wheelHeld) Close(selectHovered: true);
                else UpdateParallax();
            }
        }

        private bool TryGetHeldGridArmor(out GridBlockItem item)
        {
            item = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is GridBlockItem gbi))
                return false;

            string name = (gbi.displayName ?? "").ToLowerInvariant();
            if (name.Contains("armor") || name.Contains("plate") || name.Contains("block") || name.Contains("wall"))
            {
                item = gbi;
                return true;
            }
            return false;
        }

        private void Open()
        {
            if (_open || !EnsureUiRoot()) return;
            _open = true;
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

        private void BuildWheel()
        {
            if (!EnsureUiRoot()) return;
            if (_wheelOverlay != null) _wheelOverlay.RemoveFromHierarchy();

            _wheelOverlay = new VisualElement { name = "GridShapeWheel" };
            _wheelOverlay.style.position = Position.Absolute;
            _wheelOverlay.style.left = 0;
            _wheelOverlay.style.top = 0;
            _wheelOverlay.style.right = 0;
            _wheelOverlay.style.bottom = 0;
            _wheelOverlay.style.alignItems = Align.Center;
            _wheelOverlay.style.justifyContent = Justify.Center;
            _wheelOverlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.82f));
            _wheelOverlay.pickingMode = PickingMode.Position;
            _uiRoot.Add(_wheelOverlay);

            _wheelCenter = new VisualElement();
            _wheelCenter.style.width = 420;
            _wheelCenter.style.height = 420;
            _wheelCenter.style.position = Position.Relative;
            _wheelCenter.style.overflow = Overflow.Visible;
            _wheelCenter.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "translate", "scale" };
            _wheelCenter.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.08f, TimeUnit.Second) };
            float safeScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 500f, 0.64f, 1f);
            _wheelCenter.style.scale = new StyleScale(new Scale(new Vector3(safeScale, safeScale, 1f)));
            _wheelOverlay.Add(_wheelCenter);

            System.Array.Clear(_segmentRoots, 0, _segmentRoots.Length);
            System.Array.Clear(_segmentIcons, 0, _segmentIcons.Length);
            System.Array.Clear(_segmentNames, 0, _segmentNames.Length);
            BuildRing();
            BuildCenterBadge();
            for (int i = 0; i < Variants.Length; i++)
                BuildSegment(i, Variants[i], IconText[i], ShortLabels[i]);
            RefreshSegmentLabels();
        }

        private void BuildCenterBadge()
        {
            var badge = new VisualElement();
            badge.style.position = Position.Absolute;
            badge.style.left = 75;
            badge.style.top = 75;
            badge.style.width = 270;
            badge.style.height = 270;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.backgroundColor = new StyleColor(new Color(0.035f, 0.05f, 0.075f, 0.98f));
            UITheme.Radius(badge, 135f);
            UITheme.Border(badge, 2f, UITheme.BorderBright);
            badge.pickingMode = PickingMode.Ignore;
            _wheelCenter.Add(badge);

            var title = new Label("SHAPE VARIANT");
            title.style.fontSize = 10;
            title.style.letterSpacing = 1.4f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(UITheme.TextMuted);
            title.pickingMode = PickingMode.Ignore;
            badge.Add(title);

            var icon = new Label(IconText[(int)_current]);
            icon.name = "GSW_CenterIcon";
            icon.style.fontSize = 28;
            icon.style.marginTop = 4;
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.color = new StyleColor(UITheme.AccentCyan);
            icon.pickingMode = PickingMode.Ignore;
            badge.Add(icon);

            var selected = new Label(_current.ToString().ToUpperInvariant());
            selected.name = "GSW_SelectedLabel";
            selected.style.fontSize = 14;
            selected.style.marginTop = 2;
            selected.style.unityFontStyleAndWeight = FontStyle.Bold;
            selected.style.color = new StyleColor(UITheme.AccentCyan);
            selected.pickingMode = PickingMode.Ignore;
            badge.Add(selected);

            var hint = new Label("HOLD TO SELECT");
            hint.style.fontSize = 8;
            hint.style.marginTop = 4;
            hint.style.letterSpacing = 1f;
            hint.style.color = new StyleColor(UITheme.TextMuted);
            hint.pickingMode = PickingMode.Ignore;
            badge.Add(hint);
        }

        private void BuildRing()
        {
            _ringElement = new VisualElement { name = "GridShapeRing" };
            _ringElement.style.position = Position.Absolute;
            _ringElement.style.left = 12;
            _ringElement.style.top = 12;
            _ringElement.style.width = 396;
            _ringElement.style.height = 396;
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
                if (segment < 0 || segment >= Variants.Length) return;
                SelectSegment(segment);
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(_ringElement);
        }

        private void SelectHoveredSegment()
        {
            if (_hoveredSegment < 0 || _hoveredSegment >= Variants.Length) return;
            SelectSegment(_hoveredSegment);
        }

        private void SelectSegment(int segment)
        {
            if (segment < 0 || segment >= Variants.Length) return;
            _current = Variants[segment];
            UpdateCenterBadge();
            BuildFeedbackHud.Show("Grid Shape", _current.ToString(), null, UITheme.AccentCyan);
            if (_open) RefreshSegmentLabels();
            RefreshRingTexture();
        }

        private void UpdateCenterBadge()
        {
            if (_wheelCenter == null) return;
            var iconLabel = _wheelCenter.Q("GSW_CenterIcon") as Label;
            if (iconLabel != null) iconLabel.text = IconText[(int)_current];
            var nameLabel = _wheelCenter.Q("GSW_SelectedLabel") as Label;
            if (nameLabel != null) nameLabel.text = _current.ToString().ToUpperInvariant();
        }

        /// <summary>Build a segment label overlaid on the ring surface.</summary>
        private void BuildSegment(int index, GridShapeVariant variant, string iconText, string labelText)
        {
            const float center = 198f;
            const float labelRadius = 140f;
            float angleRad = (-90f + index * (360f / Variants.Length)) * Mathf.Deg2Rad;

            // Container sits right on the ring surface
            var segmentRoot = new VisualElement();
            segmentRoot.style.position = Position.Absolute;
            segmentRoot.style.left = center + Mathf.Cos(angleRad) * labelRadius - 36f;
            segmentRoot.style.top = center + Mathf.Sin(angleRad) * labelRadius - 28f;
            segmentRoot.style.width = 72;
            segmentRoot.style.height = 56;
            segmentRoot.style.alignItems = Align.Center;
            segmentRoot.style.justifyContent = Justify.Center;
            segmentRoot.style.overflow = Overflow.Visible;
            segmentRoot.pickingMode = PickingMode.Ignore;
            UITheme.Radius(segmentRoot, 14f);
            segmentRoot.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "scale", "background-color" };
            segmentRoot.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            _wheelCenter.Add(segmentRoot);
            _segmentRoots[index] = segmentRoot;

            // Icon character
            var icon = new Label(iconText);
            icon.style.fontSize = 18;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.color = new StyleColor(new Color(0.40f, 0.42f, 0.44f));
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.style.marginBottom = 1;
            icon.pickingMode = PickingMode.Ignore;
            segmentRoot.Add(icon);
            _segmentIcons[index] = icon;

            // Label
            var label = new Label(labelText);
            label.style.fontSize = 7;
            label.style.letterSpacing = 0.3f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(new Color(0.35f, 0.37f, 0.40f));
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.pickingMode = PickingMode.Ignore;
            segmentRoot.Add(label);
            _segmentNames[index] = label;
        }

        private void RefreshSegmentLabels()
        {
            for (int i = 0; i < Variants.Length; i++)
            {
                var root = _segmentRoots[i];
                var icon = _segmentIcons[i];
                var label = _segmentNames[i];
                if (root == null || icon == null || label == null) continue;

                bool isSelected = Variants[i] == _current;
                bool isHovered = i == _hoveredSegment;

                Color fgColor = isSelected
                    ? Color.white
                    : (isHovered ? new Color(0.10f, 0.72f, 0.94f) : new Color(0.40f, 0.42f, 0.44f));

                icon.style.color = new StyleColor(fgColor);
                label.style.color = new StyleColor(isHovered ? new Color(0.10f, 0.72f, 0.94f) : new Color(0.35f, 0.37f, 0.40f));

                root.style.scale = new StyleScale(new Scale(isHovered
                    ? new Vector3(1.06f, 1.06f, 1f)
                    : Vector3.one));
                root.style.backgroundColor = new StyleColor(isHovered
                    ? new Color(0.10f, 0.68f, 0.92f, 0.15f)
                    : Color.clear);
            }
        }

        private int SegmentAt(Vector2 localPosition)
        {
            Vector2 delta = localPosition - new Vector2(198f, 198f);
            float radius = delta.magnitude;
            if (radius < 118f || radius > 172f) return -1;

            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            float normalized = Mathf.Repeat(angle + 90f, 360f);
            float segmentAngle = 360f / Variants.Length;
            float within = normalized % segmentAngle;
            if (within < 4f || within > segmentAngle - 4f) return -1;
            return Mathf.Clamp(Mathf.FloorToInt(normalized / segmentAngle), 0, Variants.Length - 1);
        }

        private void RefreshRingTexture()
        {
            const int size = 256;
            if (_ringTexture == null)
            {
                _ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "GridShapeWheelRing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            const float innerRadius = 78f;
            const float outerRadius = 128f;
            int selected = (int)_current;
            float segmentAngle = 360f / Variants.Length;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = center - y;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r < innerRadius || r > outerRadius) continue;

                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float normalized = Mathf.Repeat(angle + 90f, 360f);
                    float within = normalized % segmentAngle;
                    if (within < 4f || within > segmentAngle - 4f) continue;

                    int seg = Mathf.Clamp(Mathf.FloorToInt(normalized / segmentAngle), 0, Variants.Length - 1);
                    Color32 color;
                    if (seg == selected)
                        color = new Color32(22, 157, 220, 250);
                    else if (seg == _hoveredSegment)
                        color = new Color32(70, 188, 232, 252);
                    else
                        color = new Color32(245, 242, 232, 255); // premium cream

                    float edge = Mathf.Min(r - innerRadius, outerRadius - r);
                    color.a = (byte)Mathf.RoundToInt(color.a * Mathf.Clamp01(edge / 8f));
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
            Vector2 target = Vector2.ClampMagnitude(fromCenter * 0.035f, 16f);
            _wheelCenter.style.translate = new StyleTranslate(new Translate(
                new Length(target.x, LengthUnit.Pixel),
                new Length(-target.y, LengthUnit.Pixel), 0f));
        }
    }
}
