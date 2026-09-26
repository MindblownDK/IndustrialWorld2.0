// Assets/Scripts/UI/EnergyPipeShapeWheel.cs

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.GridSystem;
using VoxelEngine.Items;
using VoxelEngine.Power;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;

namespace VoxelEngine.UI
{
    /// <summary>
    /// Radial selection wheel for Energy Pipe shape variants.
    /// Opens when holding an Energy Pipe and pressing the BuildWheel keybind.
    /// Also handles Ctrl + Scroll length adjustment for the Straight pipe variant.
    /// </summary>
    public sealed class EnergyPipeShapeWheel : MonoBehaviour
    {
        private struct VariantDescriptor
        {
            public EnergyPipeVariant Variant;
            public string Title;
            public string IconText;
            public string Blurb;
        }

        private static readonly VariantDescriptor[] Descriptors =
        {
            new() { Variant = EnergyPipeVariant.Straight,      Title = "STRAIGHT",          IconText = "━", Blurb = "Straight dual conduit. Hold Ctrl + Scroll to adjust length (1-5m)." },
            new() { Variant = EnergyPipeVariant.BendRight,     Title = "90° ELBOW",         IconText = "┓", Blurb = "90-degree horizontal turn to the right." },
            new() { Variant = EnergyPipeVariant.BendUp,        Title = "90° RISER",         IconText = "⤴", Blurb = "90-degree vertical bend going upward." },
            new() { Variant = EnergyPipeVariant.StepUp,        Title = "VERTICAL STEP",     IconText = "⤹", Blurb = "S-curve rising 1m upward and continuing forward." },
            new() { Variant = EnergyPipeVariant.StepRight,     Title = "S-CURVE",           IconText = "⤾", Blurb = "Horizontal S-curve offsetting 1m to the right." },
            new() { Variant = EnergyPipeVariant.BendLeftToUp,  Title = "LEFT → UP",         IconText = "↰", Blurb = "3D compound curve entering from left and exiting up." },
            new() { Variant = EnergyPipeVariant.BendRightToUp, Title = "RIGHT → UP",        IconText = "↱", Blurb = "3D compound curve entering from right and exiting up." },
            new() { Variant = EnergyPipeVariant.Junction4Way,  Title = "4-WAY CROSS",       IconText = "╋", Blurb = "Planar 4-way cross junction with bolted flange ports." },
            new() { Variant = EnergyPipeVariant.Junction6Way,  Title = "6-WAY HUB",         IconText = "❖", Blurb = "Omni-directional 3D 6-way junction hub." }
        };

        private Inventory _inventory;
        private VisualElement _uiRoot;
        private VisualElement _prompt;
        private Label _promptLabel;
        private VisualElement _wheelOverlay;
        private VisualElement _wheelCenter;
        private VisualElement _ringElement;
        private Texture2D _ringTexture;
        private Label _badgeTitle;
        private Label _badgeBlurb;
        private readonly VisualElement[] _segmentLabelRoots = new VisualElement[Descriptors.Length];
        private readonly Label[] _segmentIcons = new Label[Descriptors.Length];
        private readonly Label[] _segmentNames = new Label[Descriptors.Length];

        private int _hovered = -1;
        private bool _open;
        private bool _wasBlocking;
        private Vector2 _parallax;

        private void Start()
        {
            _inventory = GetComponentInParent<Inventory>() ?? FindAnyObjectByType<Inventory>();
        }

        private void Update()
        {
            if (_inventory == null) _inventory = FindAnyObjectByType<Inventory>();
            bool holdingPipe = TryGetHeldEnergyPipe(out var blockItem, out var cable);

            if (!holdingPipe)
            {
                if (_open) Close(commit: false);
                HidePrompt();
                return;
            }

            // Handle Ctrl + Scroll length adjustment for straight pipe
            if (EnergyPipeSelection.Variant == EnergyPipeVariant.Straight)
            {
                float scroll = GridInput.Scroll;
                bool ctrl = GridInput.Ctrl;
                if (ctrl && Mathf.Abs(scroll) > 0.01f)
                {
                    int dir = scroll > 0 ? 1 : -1;
                    int oldLen = EnergyPipeSelection.StraightLength;
                    EnergyPipeSelection.AdjustLength(dir);
                    if (EnergyPipeSelection.StraightLength != oldLen)
                    {
                        BuildFeedbackHud.Show("Pipe Length", $"{EnergyPipeSelection.StraightLength} m (Ctrl+Scroll)", null, UITheme.AccentCyan);
                        ShowPrompt();
                    }
                }
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
            else
            {
                HidePrompt();
            }
        }

        private void OnDisable()
        {
            if (_open) Close(commit: false);
            RemoveUi();
        }

        private void OnDestroy()
        {
            if (_open) Close(commit: false);
            RemoveUi();
        }

        private bool TryGetHeldEnergyPipe(out BlockItem block, out PowerCable cable)
        {
            block = null;
            cable = null;
            if (_inventory == null) return false;
            var stack = _inventory.ActiveStack;
            if (stack == null || stack.IsEmpty || !(stack.item is BlockItem b) || b.placedPrefab == null)
                return false;

            cable = b.placedPrefab.GetComponentInChildren<PowerCable>(true);
            if (cable != null)
            {
                block = b;
                return true;
            }
            return false;
        }

        // ── UI ───────────────────────────────────────────────────────────

        private bool EnsureUiRoot()
        {
            if (_uiRoot != null && _uiRoot.panel != null) return true;
            var controller = GameUIController.Instance;
            var document = controller != null ? controller.GetComponent<UIDocument>() : FindAnyObjectByType<UIDocument>();
            if (document == null || document.rootVisualElement == null) return false;
            _uiRoot = document.rootVisualElement;
            return true;
        }

        private void ShowPrompt()
        {
            if (!EnsureUiRoot()) return;
            if (_prompt == null || _prompt.parent == null)
            {
                _prompt = new VisualElement { name = "EnergyPipeShapePrompt" };
                _prompt.style.position = Position.Absolute;
                _prompt.style.left = 0f;
                _prompt.style.right = 0f;
                _prompt.style.bottom = 82f;
                _prompt.style.alignItems = Align.Center;
                _prompt.pickingMode = PickingMode.Ignore;

                var pill = new VisualElement();
                pill.style.height = 30f;
                pill.style.paddingLeft = 14f;
                pill.style.paddingRight = 14f;
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

            string variantName = EnergyPipeSelection.GetVariantDisplayName(EnergyPipeSelection.Variant);
            string ctrlHint = EnergyPipeSelection.Variant == EnergyPipeVariant.Straight
                ? "  ·  [Ctrl + Scroll to Resize]"
                : "";
            _promptLabel.text = $"[{GameSettings.GetKey(InputAction.BuildWheel)}]  ENERGY PIPE SHAPE  ·  {variantName}{ctrlHint}";
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
            _wasBlocking = true;
            UIState.PushBlock();
            _parallax = Vector2.zero;
            _hovered = (int)EnergyPipeSelection.Variant;
            BuildWheel();
        }

        private void Close(bool commit)
        {
            _open = false;
            if (_wasBlocking) { _wasBlocking = false; UIState.PopBlock(); }
            if (commit && _hovered >= 0 && _hovered < Descriptors.Length)
            {
                var desc = Descriptors[_hovered];
                EnergyPipeSelection.Variant = desc.Variant;
                BuildFeedbackHud.Show("Pipe Variant", desc.Title, null, UITheme.AccentCyan);
            }
            if (_wheelOverlay != null && _wheelOverlay.parent != null) _wheelOverlay.RemoveFromHierarchy();
            ReleaseRingTexture();
            _wheelOverlay = null;
            _wheelCenter = null;
            _ringElement = null;
            _badgeTitle = null;
            _badgeBlurb = null;
            _hovered = -1;
            ShowPrompt();
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

        private void BuildWheel()
        {
            if (!EnsureUiRoot()) return;
            if (_wheelOverlay != null && _wheelOverlay.parent != null) _wheelOverlay.RemoveFromHierarchy();

            _wheelOverlay = new VisualElement { name = "EnergyPipeShapeWheelOverlay" };
            _wheelOverlay.style.position = Position.Absolute;
            _wheelOverlay.style.left = 0f;
            _wheelOverlay.style.top = 0f;
            _wheelOverlay.style.right = 0f;
            _wheelOverlay.style.bottom = 0f;
            _wheelOverlay.style.alignItems = Align.Center;
            _wheelOverlay.style.justifyContent = Justify.Center;
            _wheelOverlay.style.backgroundColor = new StyleColor(new Color(0.01f, 0.012f, 0.018f, 0.85f));
            _wheelOverlay.pickingMode = PickingMode.Position;
            _uiRoot.Add(_wheelOverlay);

            _wheelCenter = new VisualElement();
            _wheelCenter.style.width = 460f;
            _wheelCenter.style.height = 460f;
            _wheelCenter.style.position = Position.Relative;
            _wheelCenter.style.transitionProperty = new List<StylePropertyName> { "translate", "scale" };
            _wheelCenter.style.transitionDuration = new List<TimeValue> { new(0.08f, TimeUnit.Second) };
            float safeScale = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) / 540f, 0.65f, 1f);
            _wheelCenter.style.scale = new StyleScale(new Scale(new Vector3(safeScale, safeScale, 1f)));
            _wheelOverlay.Add(_wheelCenter);

            Array.Clear(_segmentLabelRoots, 0, _segmentLabelRoots.Length);
            Array.Clear(_segmentIcons, 0, _segmentIcons.Length);
            Array.Clear(_segmentNames, 0, _segmentNames.Length);

            BuildRing();
            BuildCenterBadge();
            for (int i = 0; i < Descriptors.Length; i++)
                BuildRingLabel(i);
            RefreshSegmentLabels();
            RefreshBadge();
        }

        private void BuildCenterBadge()
        {
            var badge = new VisualElement();
            badge.style.position = Position.Absolute;
            badge.style.left = 110f;
            badge.style.top = 110f;
            badge.style.width = 240f;
            badge.style.height = 240f;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.backgroundColor = new StyleColor(new Color(0.035f, 0.05f, 0.075f, 0.98f));
            UITheme.Radius(badge, 120f);
            UITheme.Border(badge, 2f, UITheme.BorderBright);
            badge.pickingMode = PickingMode.Ignore;
            _wheelCenter.Add(badge);

            var title = new Label("ENERGY PIPES");
            title.style.fontSize = 13f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(UITheme.AccentCyan);
            title.style.letterSpacing = 1.2f;
            title.pickingMode = PickingMode.Ignore;
            badge.Add(title);

            _badgeTitle = new Label();
            _badgeTitle.style.fontSize = 11f;
            _badgeTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            _badgeTitle.style.color = new StyleColor(UITheme.TextPrimary);
            _badgeTitle.style.marginTop = 4f;
            _badgeTitle.pickingMode = PickingMode.Ignore;
            badge.Add(_badgeTitle);

            _badgeBlurb = new Label();
            _badgeBlurb.style.fontSize = 9.5f;
            _badgeBlurb.style.color = new StyleColor(UITheme.TextSecondary);
            _badgeBlurb.style.marginTop = 6f;
            _badgeBlurb.style.paddingLeft = 14f;
            _badgeBlurb.style.paddingRight = 14f;
            _badgeBlurb.style.unityTextAlign = TextAnchor.MiddleCenter;
            _badgeBlurb.style.whiteSpace = WhiteSpace.Normal;
            _badgeBlurb.pickingMode = PickingMode.Ignore;
            badge.Add(_badgeBlurb);
        }

        private void BuildRing()
        {
            _ringElement = new VisualElement();
            _ringElement.style.position = Position.Absolute;
            _ringElement.style.left = 50f;
            _ringElement.style.top = 50f;
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
                if (segment >= 0 && segment < Descriptors.Length)
                    EnergyPipeSelection.Variant = Descriptors[segment].Variant;
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

            _ringElement.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                int segment = SegmentAt(evt.localPosition);
                if (segment < 0 || segment >= Descriptors.Length) return;
                _hovered = segment;
                EnergyPipeSelection.Variant = Descriptors[segment].Variant;
                RefreshRingTexture();
                RefreshSegmentLabels();
                RefreshBadge();
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(_ringElement);
        }

        private void BuildRingLabel(int index)
        {
            const float center = 180f;
            const float radius = 145f;
            float slice = 360f / Descriptors.Length;
            float angle = (-90f + index * slice) * Mathf.Deg2Rad;

            var labelRoot = new VisualElement();
            labelRoot.style.position = Position.Absolute;
            labelRoot.style.left = center + Mathf.Cos(angle) * radius - 45f;
            labelRoot.style.top = center + Mathf.Sin(angle) * radius - 26f;
            labelRoot.style.width = 90f;
            labelRoot.style.height = 52f;
            labelRoot.style.alignItems = Align.Center;
            labelRoot.style.justifyContent = Justify.Center;
            labelRoot.style.overflow = Overflow.Visible;
            labelRoot.pickingMode = PickingMode.Ignore;
            UITheme.Radius(labelRoot, 26f);
            labelRoot.style.transitionProperty = new List<StylePropertyName> { "scale", "background-color" };
            labelRoot.style.transitionDuration = new List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            _wheelCenter.Add(labelRoot);
            _segmentLabelRoots[index] = labelRoot;

            bool selected = Descriptors[index].Variant == EnergyPipeSelection.Variant;
            var icon = new Label(Descriptors[index].IconText);
            icon.style.fontSize = 20f;
            icon.style.unityTextAlign = TextAnchor.MiddleCenter;
            icon.style.color = new StyleColor(selected ? Color.white : new Color(0.20f, 0.22f, 0.24f));
            icon.style.unityFontStyleAndWeight = FontStyle.Bold;
            icon.pickingMode = PickingMode.Ignore;
            labelRoot.Add(icon);
            _segmentIcons[index] = icon;

            var label = new Label(Descriptors[index].Title);
            label.style.fontSize = 8f;
            label.style.marginTop = 1f;
            label.style.letterSpacing = 0.6f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(selected ? Color.white : new Color(0.24f, 0.26f, 0.28f));
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.pickingMode = PickingMode.Ignore;
            labelRoot.Add(label);
            _segmentNames[index] = label;
        }

        private void RefreshSegmentLabels()
        {
            for (int i = 0; i < Descriptors.Length; i++)
            {
                var root = _segmentLabelRoots[i];
                var icon = _segmentIcons[i];
                var label = _segmentNames[i];
                if (root == null || icon == null || label == null) continue;

                bool isSelected = Descriptors[i].Variant == EnergyPipeSelection.Variant;
                bool isHovered = i == _hovered;
                Color foreground = isSelected
                    ? Color.white
                    : (isHovered ? new Color(0.04f, 0.62f, 0.88f) : new Color(0.20f, 0.22f, 0.24f));
                icon.style.color = new StyleColor(foreground);
                label.style.color = new StyleColor(foreground);
                root.style.scale = new StyleScale(new Scale(isHovered ? new Vector3(1.06f, 1.06f, 1f) : Vector3.one));
                root.style.backgroundColor = new StyleColor(isHovered ? new Color(0.10f, 0.68f, 0.92f, 0.18f) : Color.clear);
            }
        }

        private void RefreshBadge()
        {
            int read = _hovered >= 0 && _hovered < Descriptors.Length ? _hovered : (int)EnergyPipeSelection.Variant;
            if (_badgeTitle != null) _badgeTitle.text = Descriptors[read].Title;
            if (_badgeBlurb != null) _badgeBlurb.text = Descriptors[read].Blurb;
        }

        private int SegmentAt(Vector2 localPosition)
        {
            Vector2 delta = localPosition - new Vector2(180f, 180f);
            float radius = delta.magnitude;
            if (radius < 100f || radius > 175f) return -1;

            float slice = 360f / Descriptors.Length;
            float normalized = Mathf.Repeat(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg + 90f + slice * 0.5f, 360f);
            int segment = Mathf.Clamp(Mathf.FloorToInt(normalized / slice), 0, Descriptors.Length - 1);
            return segment;
        }

        private void RefreshRingTexture()
        {
            const int size = 256;
            if (_ringTexture == null)
            {
                _ringTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "EnergyPipeShapeWheelRing",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            var pixels = new Color32[size * size];
            float center = (size - 1) * 0.5f;
            const float innerRadius = 70f;
            const float outerRadius = 125f;
            float slice = 360f / Descriptors.Length;
            float gap = Mathf.Min(3.0f, slice * 0.04f);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = center - y;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    if (radius < innerRadius || radius > outerRadius) continue;

                    float normalized = Mathf.Repeat(Mathf.Atan2(dy, dx) * Mathf.Rad2Deg + 90f + slice * 0.5f, 360f);
                    float withinSegment = normalized % slice;
                    if (withinSegment < gap || withinSegment > slice - gap) continue;

                    int segment = Mathf.Clamp(Mathf.FloorToInt(normalized / slice), 0, Descriptors.Length - 1);
                    bool isSelected = Descriptors[segment].Variant == EnergyPipeSelection.Variant;
                    bool isHovered = segment == _hovered;

                    Color32 baseCream = new Color32(245, 242, 232, 255);
                    Color32 selectedCyan = new Color32(22, 157, 220, 255);
                    Color32 hoverAccent = new Color32(70, 188, 232, 255);

                    Color32 color = isSelected ? selectedCyan : (isHovered ? hoverAccent : baseCream);
                    float edge = Mathf.Min(radius - innerRadius, outerRadius - radius);
                    float alphaFade = Mathf.Clamp01(edge / 6f);
                    color.a = (byte)Mathf.RoundToInt(255 * alphaFade * (isSelected || isHovered ? 1f : 0.95f));

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
