// Assets/Scripts/VoxelEngine/UI/HazardWarningHud.cs
//
// THE GEIGER COUNTER — the warning strip for localised environmental hazards.
//
// A hazard zone the player cannot detect until they are dying in it is not a design
// feature, it is an ambush. This strip is the half that makes zones playable: it reads
// the field ahead of the damage, so a player crossing a contaminated valley gets a
// rising warning and a chance to turn back.
//
// It deliberately shows three things and no more:
//   • WHAT the hazard is, so the player knows which threat they are in.
//   • HOW STRONG it is, as a meter that fills before the damage becomes serious.
//   • WHETHER THEY ARE PROTECTED, because "radiation, and you are fine" and
//     "radiation, and you are not" are completely different situations and a bare
//     number cannot distinguish them.
//
// The reading comes from `PlayerStats.LastHazard` rather than re-sampling the field.
// Two independent samples of the same noise could disagree at a boundary, and a HUD
// that says SAFE while the damage path disagrees is worse than no HUD.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Player;

namespace VoxelEngine.UI
{
    public static class HazardWarningHud
    {
        private static VisualElement _root, _strip, _fill;
        private static Label _label, _statusLabel;
        private static bool _visible;

        /// <summary>Pulse phase for the alert flash when the player is unprotected.</summary>
        private static float _pulse;

        private static readonly Color RadiationInk = new(0.55f, 0.95f, 0.35f);
        private static readonly Color HeatInk = new(1.00f, 0.55f, 0.24f);
        private static readonly Color ToxicInk = new(0.72f, 0.95f, 0.30f);
        private static readonly Color SafeInk = new(0.45f, 0.80f, 1.00f);
        private static readonly Color DangerInk = new(1.00f, 0.32f, 0.28f);

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            if (_root == uiRoot && _strip != null && _strip.parent == uiRoot) return;
            _root = uiRoot;
            if (_strip != null) _strip.RemoveFromHierarchy();

            _strip = new VisualElement { name = "HazardWarningHud" };
            _strip.style.position = Position.Absolute;
            _strip.style.left = Length.Percent(50f);
            _strip.style.bottom = 128;
            _strip.style.translate = new Translate(new Length(-50f, LengthUnit.Percent), 0f, 0f);
            _strip.style.flexDirection = FlexDirection.Row;
            _strip.style.alignItems = Align.Center;
            _strip.style.paddingTop = 4;
            _strip.style.paddingBottom = 4;
            _strip.style.paddingLeft = 10;
            _strip.style.paddingRight = 10;
            _strip.style.backgroundColor = new StyleColor(new Color(0.07f, 0.06f, 0.04f, 0.88f));
            UITheme.Radius(_strip, 8f);
            UITheme.Border(_strip, 1, new Color(0.62f, 0.52f, 0.20f, 0.60f));
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
            track.style.backgroundColor = new StyleColor(new Color(0.045f, 0.04f, 0.03f));
            UITheme.Radius(track, 4f);
            UITheme.Border(track, 1, new Color(0.34f, 0.31f, 0.24f));
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            _strip.Add(track);

            _fill = new VisualElement();
            _fill.style.height = Length.Percent(100);
            _fill.style.width = Length.Percent(0);
            _fill.pickingMode = PickingMode.Ignore;
            track.Add(_fill);

            _statusLabel = new Label();
            _statusLabel.style.fontSize = 9;
            _statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _statusLabel.style.letterSpacing = 1.1f;
            _statusLabel.style.marginLeft = 8;
            _statusLabel.pickingMode = PickingMode.Ignore;
            _strip.Add(_statusLabel);
        }

        public static void Tick(PlayerStats stats, PlayerEquipment equipment)
        {
            if (_strip == null) return;

            if (stats == null)
            {
                Hide();
                return;
            }

            var hazard = stats.LastHazard;

            // Show only for a genuine LOCAL zone. A uniformly irradiated planet is a known
            // constant the player already accounts for; flashing a permanent warning on it
            // would train them to ignore the strip exactly when a real zone appears.
            if (hazard.Dominant == HazardKind.None || hazard.ZoneIntensity <= 0.05f)
            {
                Hide();
                return;
            }

            _visible = true;
            _strip.style.display = DisplayStyle.Flex;

            Color ink = hazard.Dominant switch
            {
                HazardKind.Radiation => RadiationInk,
                HazardKind.Heat => HeatInk,
                HazardKind.Toxic => ToxicInk,
                _ => SafeInk,
            };

            // Is the player actually protected against THIS hazard?
            float multiplier = hazard.Dominant switch
            {
                HazardKind.Radiation => equipment != null ? equipment.RadiationDamageMultiplier : 1f,
                HazardKind.Heat => equipment != null ? equipment.HeatDamageMultiplier : 1f,
                HazardKind.Toxic => equipment != null ? equipment.ToxicDamageMultiplier : 1f,
                _ => 0f,
            };
            bool shielded = multiplier <= 0.001f;
            bool reduced = !shielded && multiplier < 0.9f;

            _label.text = HazardField.Label(hazard.Dominant);
            _label.style.color = new StyleColor(ink);

            _fill.style.width = Length.Percent(Mathf.Clamp01(hazard.ZoneIntensity) * 100f);
            _fill.style.backgroundColor = new StyleColor(shielded ? SafeInk : ink);

            if (shielded)
            {
                _statusLabel.text = "SHIELDED";
                _statusLabel.style.color = new StyleColor(SafeInk);
                _strip.style.opacity = 1f;
            }
            else
            {
                _statusLabel.text = reduced ? "PARTIAL" : "UNPROTECTED";
                _statusLabel.style.color = new StyleColor(reduced ? HeatInk : DangerInk);

                // Only an actually-dangerous reading pulses. The flash is the loudest thing
                // the strip can do, so it is reserved for the case that can kill you.
                _pulse += Time.deltaTime * 6f;
                _strip.style.opacity = reduced ? 1f : 0.72f + 0.28f * Mathf.Abs(Mathf.Sin(_pulse));
            }

            UITheme.Border(_strip, 1, new Color(ink.r, ink.g, ink.b, shielded ? 0.45f : 0.85f));
        }

        private static void Hide()
        {
            if (!_visible) return;
            _visible = false;
            _pulse = 0f;
            if (_strip != null)
            {
                _strip.style.display = DisplayStyle.None;
                _strip.style.opacity = 1f;
            }
        }
    }
}
