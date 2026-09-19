// Assets/Scripts/VoxelEngine/UI/RailCostHud.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        RAIL COST HUD — live material readout while dragging      ║
// ╚══════════════════════════════════════════════════════════════════╝
//
// THE ASK
// While a rail run is being aimed, the game should say what it will cost AS THE DRAG GROWS,
// not after the commit click. A main line is one of the biggest material commitments in the
// game - hundreds of track pieces and twice that in stone - and until now the first number
// the player saw was a refusal toast telling them what they should have carried.
//
// THE SHAPE
// Same card language as BuildCostHud: dark plate, thin accent stripe, rich-text counts that
// go green or red against what the inventory actually holds. It is driven from the SAME plan
// the ghost draws and the commit lays, so the number on the card, the preview on the ground
// and the bill after the click can never disagree.
//
// It counts only cells the commit would actually lay (valid cells), and it names the route
// length in metres because that is the number a player plans a line in.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Building;
using VoxelEngine.Items;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.UI
{
    public static class RailCostHud
    {
        // ── State ──────────────────────────────────────────────────────
        private static VisualElement _root, _container, _card;
        private static Label _titleLabel, _costLabel, _noteLabel;

        // ── Mount ──────────────────────────────────────────────────────
        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            // Centre-bottom anchor, above the build-cost card so the two never stack.
            _container = new VisualElement { name = "RailCostHud" };
            _container.style.position = Position.Absolute;
            _container.style.bottom = 150;
            _container.style.left = 0;
            _container.style.right = 0;
            _container.style.alignItems = Align.Center;
            _container.pickingMode = PickingMode.Ignore;
            _container.style.display = DisplayStyle.None;
            uiRoot.Add(_container);

            _card = new VisualElement();
            _card.style.paddingTop = 8;
            _card.style.paddingBottom = 8;
            _card.style.paddingLeft = 20;
            _card.style.paddingRight = 20;
            _card.style.backgroundColor = new StyleColor(new Color(T.BgDark.r, T.BgDark.g, T.BgDark.b, 0.92f));
            _card.style.alignItems = Align.Center;
            T.Radius(_card, 8f);
            T.Border(_card, 1, T.BorderBright);
            _card.pickingMode = PickingMode.Ignore;
            _container.Add(_card);

            var stripe = new VisualElement();
            stripe.style.height = 2;
            stripe.style.alignSelf = Align.Stretch;
            stripe.style.backgroundColor = new StyleColor(new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.40f));
            stripe.style.marginBottom = 6;
            T.Radius(stripe, 1f);
            stripe.pickingMode = PickingMode.Ignore;
            _card.Add(stripe);

            _titleLabel = new Label("");
            _titleLabel.style.color = new StyleColor(T.TextPrimary);
            _titleLabel.style.fontSize = 13;
            _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _titleLabel.style.letterSpacing = 0.8f;
            _titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _titleLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_titleLabel);

            _costLabel = new Label("");
            _costLabel.style.fontSize = 11;
            _costLabel.style.marginTop = 3;
            _costLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _costLabel.enableRichText = true;
            _costLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_costLabel);

            _noteLabel = new Label("");
            _noteLabel.style.fontSize = 10;
            _noteLabel.style.marginTop = 3;
            _noteLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _noteLabel.style.color = new StyleColor(T.AccentAmber);
            _noteLabel.pickingMode = PickingMode.Ignore;
            _card.Add(_noteLabel);
        }

        // ── Drive ──────────────────────────────────────────────────────

        /// <summary>
        /// Refreshes the card from the plan the ghost is drawing. Called every frame while a
        /// run is being aimed, which is what makes the counts climb with the drag.
        /// </summary>
        public static void Show(RailPlan plan, RailLayerTool tool, int gauge, float cellSize)
        {
            if (_container == null) return;
            if (plan == null || tool == null || plan.cells.Count == 0) { Hide(); return; }

            // Count what the commit would actually lay: invalid cells are refused, so billing
            // the player for them in the preview would overstate the run.
            int valid = 0;
            for (int i = 0; i < plan.cells.Count; i++)
                if (plan.cells[i].valid) valid++;

            int perLane = valid / Mathf.Max(1, gauge);
            float metres = perLane * Mathf.Max(0.05f, cellSize);

            _container.style.display = DisplayStyle.Flex;
            _titleLabel.text = $"RAIL RUN  —  {metres:0} m  ·  {valid} cells  ·  " +
                               (gauge <= 1 ? "single track" : $"{gauge} parallel");

            var inv = Object.FindAnyObjectByType<Inventory>();
            var sb = new System.Text.StringBuilder();
            bool allAfford = plan.Refusal == null;

            int trackNeed = valid * Mathf.Max(1, tool.trackPerCell);
            int ballastNeed = valid * Mathf.Max(0, tool.ballastPerCell);

            bool first = true;
            if (tool.trackItem != null && trackNeed > 0)
            {
                first = false;
                AppendIngredient(sb, ref first, tool.trackItem.displayName, trackNeed,
                    inv != null ? inv.container.CountOf(tool.trackItem) : 0, ref allAfford);
            }
            if (tool.ballastMaterial != null && ballastNeed > 0)
            {
                AppendIngredient(sb, ref first, tool.ballastMaterial.displayName, ballastNeed,
                    inv != null ? inv.container.CountOf(tool.ballastMaterial) : 0, ref allAfford);
            }

            if (first)
            {
                sb.Append("<color=#55CC77>Free</color>");
            }

            _costLabel.text = sb.ToString();

            if (plan.Refusal != null)
            {
                _noteLabel.style.display = DisplayStyle.Flex;
                _noteLabel.text = plan.Refusal;
                allAfford = false;
            }
            else
            {
                _noteLabel.style.display = DisplayStyle.None;
                _noteLabel.text = "";
            }

            T.Border(_card, 1, allAfford ? T.BorderBright : T.BorderRed);
        }

        private static void AppendIngredient(System.Text.StringBuilder sb, ref bool first,
            string name, int need, int have, ref bool allAfford)
        {
            if (!first) sb.Append("   ");
            first = false;
            bool enough = have >= need;
            if (!enough) allAfford = false;
            string hex = enough ? "#66DD88" : "#DD5544";
            sb.Append($"<color={hex}>{need}</color> <color=#A0A8B8>{name}</color> <color={hex}>({have})</color>");
        }

        public static void Hide()
        {
            if (_container != null) _container.style.display = DisplayStyle.None;
        }
    }
}
