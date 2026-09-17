// Assets/Scripts/VoxelEngine/UI/DeepSurveyHud.cs
//
// THE DEEP SURVEY READOUT — how a player finds a deposit they cannot see.
//
// A deep ore node sits below the voxel world. Nothing on the surface marks it, which
// is deliberate: if a node were visible from orbit it would be a pickup, not a find.
// But an invisible resource with no instrument is not mysterious, it is arbitrary.
// This strip is the instrument.
//
// It shows the NEAREST node and the distance to it, so surveying is a game of walking
// until the number drops - the same loop as a metal detector, which works because the
// player is always getting feedback and always knows whether the last step helped.
//
// It appears only while a Deep Survey Scanner is in the hotbar, so it is a tool the
// player chooses to use rather than a permanent overlay. That also keeps it consistent
// with how the Orbital Map is gated on an equipped device.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Generation;
using VoxelEngine.Items;

namespace VoxelEngine.UI
{
    public static class DeepSurveyHud
    {
        private static VisualElement _root, _strip, _fill;
        private static Label _label, _distanceLabel;
        private static bool _visible;

        /// <summary>How far the scanner reaches. Generous, because a node is rare.</summary>
        private const float SURVEY_RANGE = 1400f;

        /// <summary>Inside this distance the player is standing on the deposit.</summary>
        private const float ON_TARGET = DeepOreField.NODE_RADIUS;

        private static readonly Color FarInk = new(0.48f, 0.56f, 0.66f);
        private static readonly Color NearInk = new(0.95f, 0.78f, 0.32f);
        private static readonly Color OnTargetInk = new(0.40f, 0.92f, 0.58f);
        private static readonly Color DepletedInk = new(0.78f, 0.42f, 0.38f);

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _strip != null && _strip.parent == uiRoot) return;
            _root = uiRoot;
            if (_strip != null) _strip.RemoveFromHierarchy();

            _strip = new VisualElement { name = "DeepSurveyHud" };
            _strip.style.position = Position.Absolute;
            _strip.style.left = Length.Percent(50f);
            _strip.style.bottom = 160;
            _strip.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f, 0f);
            _strip.style.flexDirection = FlexDirection.Row;
            _strip.style.alignItems = Align.Center;
            _strip.style.paddingTop = 4;
            _strip.style.paddingBottom = 4;
            _strip.style.paddingLeft = 10;
            _strip.style.paddingRight = 10;
            _strip.style.backgroundColor = new StyleColor(new Color(0.045f, 0.06f, 0.075f, 0.88f));
            UITheme.Radius(_strip, 8f);
            UITheme.Border(_strip, 1, new Color(0.28f, 0.44f, 0.55f, 0.60f));
            _strip.style.display = DisplayStyle.None;
            _strip.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_strip);

            _label = new Label();
            _label.style.fontSize = 10;
            _label.style.unityFontStyleAndWeight = FontStyle.Bold;
            _label.style.letterSpacing = 1.2f;
            _label.style.marginRight = 8;
            _label.pickingMode = PickingMode.Ignore;
            _strip.Add(_label);

            var track = new VisualElement();
            track.style.width = 92;
            track.style.height = 8;
            track.style.backgroundColor = new StyleColor(new Color(0.035f, 0.045f, 0.055f));
            UITheme.Radius(track, 4f);
            UITheme.Border(track, 1, new Color(0.26f, 0.32f, 0.40f));
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            _strip.Add(track);

            _fill = new VisualElement();
            _fill.style.height = Length.Percent(100);
            _fill.style.width = Length.Percent(0);
            _fill.pickingMode = PickingMode.Ignore;
            track.Add(_fill);

            _distanceLabel = new Label();
            _distanceLabel.style.fontSize = 9;
            _distanceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _distanceLabel.style.letterSpacing = 1.1f;
            _distanceLabel.style.marginLeft = 8;
            _distanceLabel.pickingMode = PickingMode.Ignore;
            _strip.Add(_distanceLabel);
        }

        /// <summary>
        /// Called every frame with the player's inventory. Shows nothing unless a survey
        /// scanner is carried, so the strip is a tool the player opted into.
        /// </summary>
        public static void Tick(Inventory inventory, Vector3 playerPosition)
        {
            if (_strip == null) return;

            if (inventory == null || !CarriesScanner(inventory))
            {
                Hide();
                return;
            }

            if (!DeepOreField.TryFindNearest(playerPosition, SURVEY_RANGE, out var node, out float distance))
            {
                _visible = true;
                _strip.style.display = DisplayStyle.Flex;
                _label.text = "DEEP SURVEY";
                _label.style.color = new StyleColor(FarInk);
                _fill.style.width = Length.Percent(0f);
                _distanceLabel.text = "NO DEPOSIT IN RANGE";
                _distanceLabel.style.color = new StyleColor(FarInk);
                return;
            }

            _visible = true;
            _strip.style.display = DisplayStyle.Flex;

            bool onTarget = distance <= ON_TARGET;
            bool depleted = node.IsDepleted;

            Color ink = depleted ? DepletedInk
                      : onTarget ? OnTargetInk
                      : distance < SURVEY_RANGE * 0.25f ? NearInk
                      : FarInk;

            _label.text = DeepOreField.MaterialName(node.Material).ToUpperInvariant();
            _label.style.color = new StyleColor(ink);

            // The meter fills as the player CLOSES on the node, so walking the right way
            // is immediately legible without reading the number.
            float proximity = 1f - Mathf.Clamp01(distance / SURVEY_RANGE);
            _fill.style.width = Length.Percent(proximity * 100f);
            _fill.style.backgroundColor = new StyleColor(ink);

            if (depleted)
            {
                _distanceLabel.text = onTarget ? "EXHAUSTED" : $"EXHAUSTED  {distance:0} m";
            }
            else if (onTarget)
            {
                // Standing on it: stop reporting distance and start reporting worth.
                _distanceLabel.text = $"HERE  ·  {node.Remaining:N0} UNITS";
            }
            else
            {
                _distanceLabel.text = $"{distance:0} m";
            }
            _distanceLabel.style.color = new StyleColor(ink);

            UITheme.Border(_strip, 1, new Color(ink.r, ink.g, ink.b, 0.75f));
        }

        private static bool CarriesScanner(Inventory inventory)
        {
            var container = inventory.container;
            if (container == null) return false;

            for (int i = 0; i < container.Size; i++)
            {
                var slot = container.GetSlot(i);
                if (slot == null || slot.IsEmpty || slot.item == null) continue;
                if (slot.item.itemId == ScannerItemId) return true;
            }
            return false;
        }

        /// <summary>Item id authored by the setup step. Owned by the data layer.</summary>
        public const string ScannerItemId = DeepOreField.ScannerItemId;

        private static void Hide()
        {
            if (!_visible) return;
            _visible = false;
            if (_strip != null) _strip.style.display = DisplayStyle.None;
        }
    }
}
