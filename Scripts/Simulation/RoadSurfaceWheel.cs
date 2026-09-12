using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using VoxelEngine.UI;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Hold-to-choose the paving surface, built to the same interaction contract as
    /// <see cref="ConveyorShapeWheel"/>: hold the build-wheel key while the paver is in hand, the
    /// ring opens, move to hover a segment, release to commit. Three surfaces, so the ring reads as
    /// a triangle — asphalt road at the crown, stone pathway at the lower right, drawbridge road at
    /// the lower left.
    ///
    /// The hover is POINTER-EVENT driven against the ring itself, never a screen-region guess: the
    /// 9.44.1 card row sized its cards from `Segments.Length` but kept a hover test that split the
    /// screen in two, so the third card rendered and could never be hovered. Nothing in this wheel
    /// assumes the segment count any more — the slice angle, the label placement and the ring
    /// texture all derive from `Segments.Length`.
    ///
    /// The choice lives in <see cref="RoadSurfaceSelection"/> rather than on the tool, so it
    /// survives switching hotbar slots exactly the way the conveyor shape does.
    /// </summary>
    public sealed class RoadSurfaceWheel : MonoBehaviour
    {
        private struct Segment
        {
            public RoadSurfaceKind Kind;
            public string Title;
            public string Blurb;
            public string IconText;
        }

        private static readonly Segment[] Segments =
        {
            new Segment
            {
                Kind = RoadSurfaceKind.Asphalt,
                Title = "ASPHALT ROAD",
                Blurb = "Hot mix. Carries vehicles, wears under wheels.",
                IconText = "\u25AC",
            },
            new Segment
            {
                Kind = RoadSurfaceKind.Pathway,
                Title = "STONE PATHWAY",
                Blurb = "Cobble. For feet only — no traction, costs stone.",
                IconText = "\u25A6",
            },
            new Segment
            {
                // Not a third paving material. The road laid is still asphalt — what this selects
                // is what the paver does when that road reaches water. Left as ordinary asphalt the
                // crossing is a culvert or a fixed bridge and stays shut forever; selected here, the
                // crossing the paver inserts is a drawbridge that can open for a ship. It belongs on
                // this wheel rather than on a new key because it is the same question the other two
                // cards answer: what kind of road am I laying.
                Kind = RoadSurfaceKind.Bridge,
                Title = "DRAWBRIDGE ROAD",
                Blurb = "Asphalt, but water crossings open for shipping. Costs iron and stone.",
                IconText = "\u25B2",
            },
        };

        private Inventory _inventory;
        private VisualElement _uiRoot;
        private VisualElement _prompt;
        private Label _promptLabel;
        private VisualElement _wheelOverlay;
        private VisualElement _wheelCenter;
        private VisualElement _ringElement;
        private Texture2D _ringTexture;
        private readonly VisualElement[] _segmentLabelRoots = new VisualElement[Segments.Length];
        private readonly Label[] _segmentIcons = new Label[Segments.Length];
        private readonly Label[] _segmentNames = new Label[Segments.Length];
        private Label _badgeTitle;
        private Label _badgeBlurb;
        private Vector2 _parallax;
        private bool _open;
        private bool _wasBlocking;
        private int _hovered = -1;
        private static int _openCount;

        public static RoadSurfaceKind Selected => RoadSurfaceSelection.Kind;

        /// <summary>True while any surface wheel holds the input block. The paver swallows its own
        /// tick while this is set, so the click that releases the wheel cannot also commit a plan.</summary>
        public static bool IsAnyOpen => _openCount > 0;

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            if (!HoldingPaver())
            {
                if (_open) Close(commit: false);
                HidePrompt();
                return;
            }

            bool held = GameSettings.IsHeld(InputAction.BuildWheel);
            if (!_open && !UIState.IsBlocking)
            {
                ShowPrompt();
                if (held) Open();
            }
            else if (_open)
            {
                HidePrompt();
                if (!held) Close(commit: true);
                else
                {
                    if (_wheelOverlay == null || _wheelOverlay.parent == null) BuildWheel();
                    UpdateParallax();
                }
            }
            else HidePrompt();
        }

        private void OnDisable() { if (_open) Close(commit: false); RemoveUi(); }
        private void OnDestroy() { if (_open) Close(commit: false); RemoveUi(); }

        private bool HoldingPaver()
        {
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            return !stack.IsEmpty && stack.item is RoadPaverTool;
        }

        // ── UI ───────────────────────────────────────────────────────────

        private bool EnsureUiRoot()
        {
            if (_uiRoot != null && _uiRoot.panel != null) return true;
            var document = FindAnyObjectByType<UIDocument>();
            if (document == null || document.rootVisualElement == null) return false;
            _uiRoot = document.rootVisualElement;
            return true;
        }

        private void ShowPrompt()
        {
            if (!EnsureUiRoot()) return;
            if (_prompt == null || _prompt.parent == null)
            {
                _prompt = new VisualElement { name = "RoadSurfacePrompt" };
                _prompt.style.position = Position.Absolute;
                _prompt.style.left = 0f;
                _prompt.style.right = 0f;
                _prompt.style.bottom = 82f;
                _prompt.style.alignItems = Align.Center;
                _prompt.pickingMode = PickingMode.Ignore;

                var pill = new VisualElement();
                pill.style.height = 30f;
                pill.style.paddingLeft = 12f; pill.style.paddingRight = 12f;
                pill.style.flexDirection = FlexDirection.Row;
                pill.style.alignItems = Align.Center;
                pill.style.justifyContent = Justify.Center;
                pill.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.065f, 0.94f));
                UITheme.Radius(pill, 15f);
                UITheme.Border(pill, 1f, UITheme.BorderBright);
                _prompt.Add(pill);

                _promptLabel = new Label();
                _promptLabel.style.fontSize = 11f;
                _promptLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _promptLabel.style.color = new StyleColor(UITheme.TextPrimary);
                _promptLabel.style.letterSpacing = 0.5f;
                _promptLabel.style.whiteSpace = WhiteSpace.NoWrap;
                _promptLabel.pickingMode = PickingMode.Ignore;
                pill.Add(_promptLabel);

                _uiRoot.Add(_prompt);
            }
            _promptLabel.text = "[" + GameSettings.GetKey(InputAction.BuildWheel) + "]  ROAD SURFACE  \u00b7  "
                                + Segments[(int)Selected].Title;
            _prompt.style.display = DisplayStyle.Flex;
        }

        private void HidePrompt()
        {
            if (_prompt != null) _prompt.style.display = DisplayStyle.None;
        }

        private void Open()
        {
            if (!EnsureUiRoot()) return;
            _open = true;
            _openCount++;
            _wasBlocking = true;
            // Without this the cursor stays locked and hidden at screen centre while playing, which
            // is inside the ring's dead zone and leaves the wheel without a hover at all. PushBlock
            // frees the cursor the same way the conveyor shape wheel frees it, and re-locks it when
            // the last block pops.
            VoxelEngine.UI.UIState.PushBlock();
            _parallax = Vector2.zero;
            // The current selection starts hovered, so opening the wheel and letting go without
            // moving changes nothing — the same guarantee the conveyor wheel's dead centre gives.
            _hovered = (int)Selected;
            BuildWheel();
        }

        private void Close(bool commit)
        {
            if (_open) _openCount = System.Math.Max(0, _openCount - 1);
            _open = false;
            if (_wasBlocking) { _wasBlocking = false; VoxelEngine.UI.UIState.PopBlock(); }
            if (commit && _hovered >= 0 && _hovered < Segments.Length)
            {
                var seg = Segments[_hovered];
                RoadSurfaceSelection.Kind = seg.Kind;
                BuildFeedbackHud.Show("Road Surface", seg.Title, null, UITheme.AccentCyan);
            }
            if (_wheelOverlay != null && _wheelOverlay.parent != null) _wheelOverlay.RemoveFromHierarchy();
            ReleaseRingTexture();
            _wheelOverlay = null;
            _wheelCenter = null;
            _ringElement = null;
            _badgeTitle = null;
            _badgeBlurb = null;
            _hovered = -1;
        }

        private void RemoveUi()
        {
            if (_prompt != null && _prompt.parent != null) _prompt.RemoveFromHierarchy();
            _prompt = null;
            if (_wheelOverlay != null && _wheelOverlay.parent != null) _wheelOverlay.RemoveFromHierarchy();
            ReleaseRingTexture();
            _wheelOverlay = null;
            _wheelCenter = null;
            _ringElement = null;
            _badgeTitle = null;
            _badgeBlurb = null;
            _hovered = -1;
        }

        // ── The wheel ────────────────────────────────────────────────────

        private void BuildWheel()
        {
            if (!EnsureUiRoot()) return;
            if (_wheelOverlay != null) _wheelOverlay.RemoveFromHierarchy();

            _wheelOverlay = new VisualElement { name = "RoadSurfaceWheel" };
            _wheelOverlay.style.position = Position.Absolute;
            _wheelOverlay.style.left = 0f;
            _wheelOverlay.style.top = 0f;
            _wheelOverlay.style.right = 0f;
            _wheelOverlay.style.bottom = 0f;
            _wheelOverlay.style.alignItems = Align.Center;
            _wheelOverlay.style.justifyContent = Justify.Center;
            _wheelOverlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.82f));
            _wheelOverlay.pickingMode = PickingMode.Position;
            _uiRoot.Add(_wheelOverlay);

            _wheelCenter = new VisualElement();
            _wheelCenter.style.width = 440f;
            _wheelCenter.style.height = 440f;
            _wheelCenter.style.position = Position.Relative;
            _wheelCenter.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "translate", "scale" };
            _wheelCenter.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.08f, TimeUnit.Second) };
            float safeScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 520f, 0.62f, 1f);
            _wheelCenter.style.scale = new StyleScale(new Scale(new Vector3(safeScale, safeScale, 1f)));
            _wheelOverlay.Add(_wheelCenter);

            System.Array.Clear(_segmentLabelRoots, 0, _segmentLabelRoots.Length);
            System.Array.Clear(_segmentIcons, 0, _segmentIcons.Length);
            System.Array.Clear(_segmentNames, 0, _segmentNames.Length);
            BuildRing();
            BuildCenterBadge();
            for (int i = 0; i < Segments.Length; i++)
                BuildRingLabel(i);
            RefreshSegmentLabels();
            RefreshBadge();
        }

        private void BuildCenterBadge()
        {
            var badge = new VisualElement();
            badge.style.position = Position.Absolute;
            badge.style.left = 85f;
            badge.style.top = 85f;
            badge.style.width = 220f;
            badge.style.height = 220f;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.backgroundColor = new StyleColor(new Color(0.035f, 0.05f, 0.075f, 0.98f));
            UITheme.Radius(badge, 110f);
            UITheme.Border(badge, 2f, UITheme.BorderBright);
            badge.pickingMode = PickingMode.Ignore;
            _wheelCenter.Add(badge);

            var caption = new Label("ROAD SURFACE");
            caption.style.fontSize = 10f;
            caption.style.letterSpacing = 1.4f;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = new StyleColor(UITheme.TextMuted);
            caption.pickingMode = PickingMode.Ignore;
            badge.Add(caption);

            // The badge reads the HOVERED surface while one is hovered — "what am I about to pick"
            // — and falls back to the selected one, so the blurb that used to sit on each card is
            // still one glance away without crowding the ring.
            _badgeTitle = new Label(Segments[(int)Selected].Title);
            _badgeTitle.style.fontSize = 14f;
            _badgeTitle.style.marginTop = 6f;
            _badgeTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _badgeTitle.style.color = new StyleColor(UITheme.AccentCyan);
            _badgeTitle.style.whiteSpace = WhiteSpace.NoWrap;
            _badgeTitle.pickingMode = PickingMode.Ignore;
            badge.Add(_badgeTitle);

            _badgeBlurb = new Label(Segments[(int)Selected].Blurb);
            _badgeBlurb.style.fontSize = 10f;
            _badgeBlurb.style.marginTop = 6f;
            _badgeBlurb.style.color = new StyleColor(UITheme.TextMuted);
            _badgeBlurb.style.whiteSpace = WhiteSpace.Normal;
            _badgeBlurb.style.maxWidth = 180f;
            _badgeBlurb.style.unityTextAlign = TextAnchor.UpperCenter;
            _badgeBlurb.pickingMode = PickingMode.Ignore;
            badge.Add(_badgeBlurb);

            var hint = new Label("HOLD TO SELECT");
            hint.style.fontSize = 8f;
            hint.style.marginTop = 7f;
            hint.style.letterSpacing = 1f;
            hint.style.color = new StyleColor(UITheme.TextMuted);
            hint.pickingMode = PickingMode.Ignore;
            badge.Add(hint);
        }

        private void BuildRing()
        {
            _ringElement = new VisualElement { name = "RoadSurfaceRing" };
            _ringElement.style.position = Position.Absolute;
            _ringElement.style.left = 15f;
            _ringElement.style.top = 15f;
            _ringElement.style.width = 360f;
            _ringElement.style.height = 360f;
            _ringElement.pickingMode = PickingMode.Position;
            _wheelCenter.Add(_ringElement);
            RefreshRingTexture();

            _ringElement.RegisterCallback<PointerMoveEvent>(evt =>
            {
                int segment = SegmentAt(evt.localPosition);
                if (segment == _hovered) return;
                _hovered = segment;
                RefreshRingTexture();
                RefreshSegmentLabels();
                RefreshBadge();
            });
            _ringElement.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                if (_hovered < 0) return;
                _hovered = -1;
                RefreshRingTexture();
                RefreshSegmentLabels();
                RefreshBadge();
            });
            // A click picks the segment without closing the wheel, exactly as the conveyor wheel
            // does: the wheel must stay open through the rest of the frame so `IsAnyOpen` keeps the
            // paver's own tick swallowed, or the same click could land on the world underneath.
            _ringElement.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                int segment = SegmentAt(evt.localPosition);
                if (segment < 0 || segment >= Segments.Length) return;
                _hovered = segment;
                RoadSurfaceSelection.Kind = Segments[segment].Kind;
                RefreshRingTexture();
                RefreshSegmentLabels();
                RefreshBadge();
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(_ringElement);
        }

        private void BuildRingLabel(int index)
        {
            // One label at the middle of each slice: with three slices the positions are the crown
            // and the two lower corners — the triangle. The angle is derived from the slice, never
            // from a hardcoded count, which is the assumption that made the third card unreachable.
            const float center = 195f;
            const float radius = 142f;
            float slice = 360f / Segments.Length;
            float angle = (-90f + index * slice) * Mathf.Deg2Rad;

            var labelRoot = new VisualElement();
            labelRoot.style.position = Position.Absolute;
            labelRoot.style.left = center + Mathf.Cos(angle) * radius - 60f;
            labelRoot.style.top = center + Mathf.Sin(angle) * radius - 30f;
            labelRoot.style.width = 120f;
            labelRoot.style.height = 60f;
            labelRoot.style.alignItems = Align.Center;
            labelRoot.style.justifyContent = Justify.Center;
            labelRoot.style.overflow = Overflow.Visible;
            labelRoot.pickingMode = PickingMode.Ignore;
            UITheme.Radius(labelRoot, 32f);
            labelRoot.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "scale", "background-color" };
            labelRoot.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            _wheelCenter.Add(labelRoot);
            _segmentLabelRoots[index] = labelRoot;

            bool selected = Segments[index].Kind == Selected;
            var icon = new Label(Segments[index].IconText);
            icon.style.fontSize = 24f;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.color = new StyleColor(selected ? Color.white : new Color(0.16f, 0.18f, 0.20f));
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.pickingMode = PickingMode.Ignore;
            labelRoot.Add(icon);
            _segmentIcons[index] = icon;

            var label = new Label(Segments[index].Title);
            label.style.fontSize = 8.5f;
            label.style.marginTop = 1f;
            label.style.letterSpacing = 0.8f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(selected ? Color.white : new Color(0.20f, 0.22f, 0.24f));
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.pickingMode = PickingMode.Ignore;
            labelRoot.Add(label);
            _segmentNames[index] = label;
        }

        private void RefreshSegmentLabels()
        {
            for (int i = 0; i < Segments.Length; i++)
            {
                var root = _segmentLabelRoots[i];
                var icon = _segmentIcons[i];
                var label = _segmentNames[i];
                if (root == null || icon == null || label == null) continue;

                bool isSelected = Segments[i].Kind == Selected;
                bool isHovered = i == _hovered;
                Color foreground = isSelected
                    ? Color.white
                    : (isHovered ? new Color(0.04f, 0.62f, 0.88f) : new Color(0.16f, 0.18f, 0.20f));
                icon.style.color = new StyleColor(foreground);
                label.style.color = new StyleColor(foreground);
                root.style.scale = new StyleScale(new Scale(isHovered
                    ? new Vector3(1.06f, 1.06f, 1f)
                    : Vector3.one));
                root.style.backgroundColor = new StyleColor(isHovered
                    ? new Color(0.10f, 0.68f, 0.92f, 0.18f)
                    : Color.clear);
            }
        }

        private void RefreshBadge()
        {
            int read = _hovered >= 0 && _hovered < Segments.Length ? _hovered : (int)Selected;
            if (_badgeTitle != null) _badgeTitle.text = Segments[read].Title;
            if (_badgeBlurb != null) _badgeBlurb.text = Segments[read].Blurb;
        }

        /// <summary>Which slice a point on the ring belongs to, from the ring's own geometry:
        /// radius band first (the centre badge is a dead zone, not a default), then the angle
        /// within the slice circle. With three slices this is the triangle's three 120° corners.</summary>
        private int SegmentAt(Vector2 localPosition)
        {
            Vector2 delta = localPosition - new Vector2(180f, 180f);
            float radius = delta.magnitude;
            if (radius < 115f || radius > 173f) return -1;

            float slice = 360f / Segments.Length;
            // +150 keeps slice 0 centred on the crown label (-90°), the same anchor the conveyor
            // wheel uses; every other slice follows from the count.
            float normalized = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + 150f, 360f);
            float withinSegment = normalized % slice;
            float gap = Mathf.Min(3.5f, slice * 0.03f);
            if (withinSegment < gap || withinSegment > slice - gap) return -1;
            return Mathf.Clamp(Mathf.FloorToInt(normalized / slice), 0, Segments.Length - 1);
        }

        private void RefreshRingTexture()
        {
            const int size = 256;
            if (_ringTexture == null)
            {
                _ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "RoadSurfaceWheelRing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            const float innerRadius = 78f;
            const float outerRadius = 128f;
            float slice = 360f / Segments.Length;
            float gap = Mathf.Min(3.5f, slice * 0.03f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = center - y;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    if (radius < innerRadius || radius > outerRadius) continue;

                    float normalized = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 150f, 360f);
                    float withinSegment = normalized % slice;
                    if (withinSegment < gap || withinSegment > slice - gap) continue;

                    int segment = Mathf.Clamp(Mathf.FloorToInt(normalized / slice), 0, Segments.Length - 1);
                    bool isSelected = Segments[segment].Kind == Selected;
                    bool isHovered = segment == _hovered;

                    // Premium cream/off-white ring + cyan accents, the same visual language as the
                    // conveyor shape wheel and the hammer wheel it came from.
                    Color32 baseCream = new Color32(245, 242, 232, 255);
                    Color32 selectedCyan = new Color32(22, 157, 220, 255);
                    Color32 hoverAccent = new Color32(70, 188, 232, 255);

                    Color32 color;
                    if (isSelected)
                        color = selectedCyan;
                    else if (isHovered)
                        color = hoverAccent;
                    else
                        color = baseCream;

                    float edge = Mathf.Min(radius - innerRadius, outerRadius - radius);
                    float alphaFade = Mathf.Clamp01(edge / 8f);
                    color.a = (byte)Mathf.RoundToInt(255 * alphaFade * (isSelected || isHovered ? 1f : 0.96f));

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
            Vector2 target = Vector2.ClampMagnitude(fromCenter * 0.035f, 18f);
            _parallax = Vector2.Lerp(_parallax, target, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            _wheelCenter.style.translate = new StyleTranslate(new Translate(
                new Length(_parallax.x, LengthUnit.Pixel),
                new Length(-_parallax.y, LengthUnit.Pixel), 0f));
        }
    }
}
