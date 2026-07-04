// Assets/Scripts/VoxelEngine/Building/Tiered/HammerBuildWheel.cs
//
// Rust-style radial build wheel. Opens when holding Hammer and pressing the
// BuildWheel key (default middle mouse or F). Shows building families in a
// circular arrangement with cost displayed underneath each option.
//
// Visual style: dark semi-transparent backdrop, circular segments that
// highlight on hover, cost text in green (affordable) or red (can't afford).

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Items;
using VoxelEngine.Settings;
using InputAction = VoxelEngine.Settings.InputAction;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Building.Tiered
{
    [RequireComponent(typeof(UIDocument))]
    public class HammerBuildWheel : MonoBehaviour
    {
        public static HammerBuildWheel Instance { get; private set; }

        public BuildFamily? ActiveFamily { get; private set; } = null;

        public Inventory inventory;
        public TieredBlockRegistry registry;

        private UIDocument _doc;
        private VisualElement _root;
        private bool _open;

        // Wheel layout
        private static readonly BuildFamily[] _families = {
            BuildFamily.Foundation, BuildFamily.Wall,    BuildFamily.Floor,
            BuildFamily.Doorway,    BuildFamily.Window,  BuildFamily.Stairs,
            BuildFamily.Roof,       BuildFamily.Pillar,  BuildFamily.HalfWall
        };

        private static readonly string[] _icons = {
            "▣", "▥", "▤", "⊡", "☐", "⟋", "⌂", "▏", "▤"
        };

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _doc = GetComponent<UIDocument>();
            if (_doc.panelSettings == null)
                _doc.panelSettings = Resources.Load<PanelSettings>("MenuPanelSettings");
            _root = _doc.rootVisualElement;
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
            var stack = inventory != null ? inventory.ActiveStack : null;
            bool holdingHammer = stack != null && !stack.IsEmpty && stack.item is Hammer;

            if (!holdingHammer) { if (_open) Close(); return; }
            if (GameSettings.WasPressed(InputAction.BuildWheel))
            {
                if (_open) Close(); else Open();
            }
        }

        public void Open()
        {
            _open = true;
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

        private void Hide()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Ignore;
            _root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0));
        }

        // ── Build the radial wheel UI ─────────────────────────────

        private void Build()
        {
            _root.Clear();
            _root.pickingMode = PickingMode.Position;
            _root.style.backgroundColor = new StyleColor(new Color(0, 0, 0, 0.60f));
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            // Center container.
            var center = new VisualElement();
            center.style.width = 460; center.style.height = 530;
            center.style.position = Position.Relative;
            _root.Add(center);

            // Title in the very center.
            var titleBox = new VisualElement();
            titleBox.style.position = Position.Absolute;
            titleBox.style.left = 460 / 2 - 80; titleBox.style.top = 460 / 2 - 35;
            titleBox.style.width = 160; titleBox.style.height = 70;
            titleBox.style.backgroundColor = new StyleColor(T.BgDark);
            T.Radius(titleBox, 35);
            T.Border(titleBox, 2, T.BorderBright);
            titleBox.style.alignItems = Align.Center;
            titleBox.style.justifyContent = Justify.Center;
            titleBox.pickingMode = PickingMode.Ignore;
            center.Add(titleBox);

            var titleLabel = T.Subtitle("BUILD");
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.pickingMode = PickingMode.Ignore;
            titleBox.Add(titleLabel);

            var modeLabel = T.Muted(ActiveFamily.HasValue ? ActiveFamily.Value.ToString() : "Select a piece");
            modeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            modeLabel.pickingMode = PickingMode.Ignore;
            titleBox.Add(modeLabel);

            // Arrange family cards in a circle.
            float radius = 170f;
            float cx = 460f / 2f;
            float cy = 460f / 2f;
            float angleStep = 360f / _families.Length;
            float startAngle = -90f; // top

            for (int i = 0; i < _families.Length; i++)
            {
                float angle = (startAngle + angleStep * i) * Mathf.Deg2Rad;
                float px = cx + Mathf.Cos(angle) * radius - 55;
                float py = cy + Mathf.Sin(angle) * radius - 45;

                var card = MakeFamilyCard(_families[i], _icons[i], i);
                card.style.position = Position.Absolute;
                card.style.left = px;
                card.style.top = py;
                center.Add(card);
            }

            // Bottom: upgrade-only mode button.
            var upgradeBtn = new VisualElement();
            upgradeBtn.style.position = Position.Absolute;
            upgradeBtn.style.bottom = 0;
            upgradeBtn.style.left = 460 / 2 - 100;
            upgradeBtn.style.width = 200; upgradeBtn.style.height = 36;
            upgradeBtn.style.backgroundColor = new StyleColor(new Color(T.BgSlot.r, T.BgSlot.g, T.BgSlot.b, 0.95f));
            T.Radius(upgradeBtn, 18);
            T.Border(upgradeBtn, 1, ActiveFamily == null ? T.AccentGold : T.BorderDim);
            upgradeBtn.style.alignItems = Align.Center;
            upgradeBtn.style.justifyContent = Justify.Center;
            center.Add(upgradeBtn);

            var upgLabel = new Label("⬆ UPGRADE MODE");
            upgLabel.style.color = new StyleColor(ActiveFamily == null ? T.AccentGold : T.TextSecondary);
            upgLabel.style.fontSize = 11;
            upgLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            upgLabel.pickingMode = PickingMode.Ignore;
            upgradeBtn.Add(upgLabel);

            upgradeBtn.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0) { ActiveFamily = null; Build(); } // Rebuild to show toggle visually
            });
        }

        private VisualElement MakeFamilyCard(BuildFamily family, string icon, int idx)
        {
            bool selected = ActiveFamily == family;
            bool canAfford = CanAffordFamily(family);

            var card = new VisualElement();
            card.style.width = 110; card.style.height = 90;
            card.style.alignItems = Align.Center;
            card.style.justifyContent = Justify.Center;
            card.style.paddingTop = 6; card.style.paddingBottom = 6;
            card.style.backgroundColor = new StyleColor(selected ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.25f) : T.BgPanel);
            T.Radius(card, 10);
            T.Border(card, 2, selected ? T.AccentCyan : T.BorderDim);

            // Icon
            var iconLabel = new Label(icon);
            iconLabel.style.fontSize = 22;
            iconLabel.style.color = new StyleColor(selected ? T.AccentCyan : T.TextPrimary);
            iconLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            iconLabel.pickingMode = PickingMode.Ignore;
            card.Add(iconLabel);

            // Name
            var nameLabel = new Label(family.ToString());
            nameLabel.style.fontSize = 11;
            nameLabel.style.color = new StyleColor(T.TextPrimary);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            nameLabel.pickingMode = PickingMode.Ignore;
            card.Add(nameLabel);

            // Cost underneath
            string costText = GetCostText(family);
            Color costColor = canAfford ? T.AccentGreen : T.AccentRed;
            var costLabel = new Label(costText);
            costLabel.style.fontSize = 9;
            costLabel.style.color = new StyleColor(costColor);
            costLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            costLabel.style.whiteSpace = WhiteSpace.Normal;
            costLabel.style.marginTop = 2;
            costLabel.pickingMode = PickingMode.Ignore;
            card.Add(costLabel);

            // Hover effect.
            card.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (!selected)
                {
                    card.style.backgroundColor = new StyleColor(T.BgHover);
                    T.Border(card, 2, T.AccentCyan);
                }
            });
            card.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                if (!selected)
                {
                    card.style.backgroundColor = new StyleColor(T.BgPanel);
                    T.Border(card, 2, T.BorderDim);
                }
            });

            // Click to select.
            card.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.button == 0)
                {
                    ActiveFamily = family;
                    // Brief visual update then close.
                    Build(); // rebuild to show selection
                    // Schedule close for next frame so player sees the selection.
                    _root.schedule.Execute(Close).ExecuteLater(150);

                    VoxelEngine.UI.BuildFeedbackHud.Show(
                        $"Build: {family}",
                        costText,
                        null, canAfford ? T.AccentCyan : T.AccentRed);
                }
                e.StopPropagation();
            });

            return card;
        }

        // ── Cost helpers ──────────────────────────────────────────

        private string GetCostText(BuildFamily family)
        {
            if (registry == null) return "";
            var def = registry.Get(family);
            if (def == null || def.placeCost == null || def.placeCost.items == null) return "Free";

            var sb = new System.Text.StringBuilder();
            sb.Append("Cost: ");
            bool first = true;
            foreach (var ing in def.placeCost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (!first) sb.Append(", ");
                sb.Append($"{ing.count} {ing.item.displayName}");
                first = false;
            }
            return first ? "Free" : sb.ToString();
        }

        private bool CanAffordFamily(BuildFamily family)
        {
            if (registry == null || inventory == null) return false;
            var def = registry.Get(family);
            if (def == null || def.placeCost == null || def.placeCost.items == null) return true;
            foreach (var ing in def.placeCost.items)
            {
                if (ing.item == null || ing.count <= 0) continue;
                if (inventory.container.CountOf(ing.item) < ing.count) return false;
            }
            return true;
        }
    }
}
