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
    /// The held conveyor item supplies the speed tier, so one item/recipe owns every
    /// shape available at that tier.
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
                if (_open) Close();
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
                if (!wheelHeld) Close();
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
            if (_open) Close();
            RemoveUi();
        }

        private void OnDestroy()
        {
            if (_open) Close();
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

        private void Close()
        {
            if (!_open) return;
            _open = false;
            UIState.PopBlock();
            if (_wheelOverlay != null) _wheelOverlay.RemoveFromHierarchy();
            _wheelOverlay = null;
            _wheelCenter = null;
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
            _wheelCenter.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "translate" };
            _wheelCenter.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.08f, TimeUnit.Second) };
            _wheelOverlay.Add(_wheelCenter);

            BuildCenterBadge();
            float radius = 126f;
            float center = 195f;
            for (int i = 0; i < Modes.Length; i++)
            {
                float angle = (-90f + i * 120f) * Mathf.Deg2Rad;
                var card = BuildModeCard(Modes[i], Icons[i]);
                card.style.position = Position.Absolute;
                card.style.left = center + Mathf.Cos(angle) * radius - 58f;
                card.style.top = center + Mathf.Sin(angle) * radius - 42f;
                _wheelCenter.Add(card);
            }
        }

        private void BuildCenterBadge()
        {
            var badge = new VisualElement();
            badge.style.position = Position.Absolute;
            badge.style.left = 130;
            badge.style.top = 130;
            badge.style.width = 130;
            badge.style.height = 130;
            badge.style.alignItems = Align.Center;
            badge.style.justifyContent = Justify.Center;
            badge.style.backgroundColor = new StyleColor(new Color(0.035f, 0.05f, 0.075f, 0.98f));
            UITheme.Radius(badge, 65f);
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

        private VisualElement BuildModeCard(ConveyorBuildMode mode, string iconText)
        {
            bool selected = GetMode(_activeTier) == mode;
            var card = new VisualElement();
            card.style.width = 116;
            card.style.height = 84;
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.Center;
            card.style.backgroundColor = new StyleColor(selected
                ? new Color(UITheme.AccentCyan.r, UITheme.AccentCyan.g, UITheme.AccentCyan.b, 0.24f)
                : new Color(0.045f, 0.06f, 0.085f, 0.97f));
            UITheme.Radius(card, 14f);
            UITheme.Border(card, selected ? 2f : 1f, selected ? UITheme.AccentCyan : UITheme.BorderDim);

            var icon = new Label(iconText);
            icon.style.fontSize = 25;
            icon.style.color = new StyleColor(selected ? UITheme.AccentCyan : UITheme.TextPrimary);
            icon.pickingMode = PickingMode.Ignore;
            card.Add(icon);

            var label = new Label(mode.ToString().ToUpperInvariant());
            label.style.fontSize = 10;
            label.style.marginTop = 3;
            label.style.letterSpacing = 1f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.color = new StyleColor(UITheme.TextPrimary);
            label.pickingMode = PickingMode.Ignore;
            card.Add(label);

            card.style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { "scale", "background-color" };
            card.style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(0.10f, TimeUnit.Second), new(0.10f, TimeUnit.Second) };
            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                card.style.scale = new StyleScale(new Scale(new Vector3(1.06f, 1.06f, 1f)));
                card.style.backgroundColor = new StyleColor(new Color(UITheme.AccentCyan.r, UITheme.AccentCyan.g, UITheme.AccentCyan.b, 0.20f));
                UITheme.Border(card, 2f, UITheme.AccentCyan);
            });
            card.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                card.style.scale = new StyleScale(new Scale(Vector3.one));
                if (!selected)
                {
                    card.style.backgroundColor = new StyleColor(new Color(0.045f, 0.06f, 0.085f, 0.97f));
                    UITheme.Border(card, 1f, UITheme.BorderDim);
                }
            });
            card.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                SetMode(_activeTier, mode);
                BuildWheel();
                BuildFeedbackHud.Show("Conveyor Shape", mode.ToString(), null, UITheme.AccentCyan);
                evt.StopPropagation();
            });
            VoxelEngine.FX.UiAudio.MarkClickable(card);
            return card;
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
            _prompt = null;
            _wheelOverlay = null;
            _wheelCenter = null;
        }
    }
}
