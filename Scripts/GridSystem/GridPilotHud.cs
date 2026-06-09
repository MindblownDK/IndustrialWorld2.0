// Assets/Scripts/VoxelEngine/GridSystem/GridPilotHud.cs
//
// Overlay HUD shown while the player is piloting a ship/vehicle.
// Shows speed, altitude, power, hydrogen, dampeners, thrust direction.

using UnityEngine;
using UnityEngine.UIElements;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem
{
    public static class GridPilotHud
    {
        private static VisualElement _root;
        private static VisualElement _container;
        private static GridCockpit _cachedCockpit;
        private static float _cockpitSearchTimer;
        private static Label _speedLabel, _altLabel, _powerLabel, _h2Label, _dampLabel;
        private static VisualElement _powerFill, _h2Fill;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            _container = new VisualElement { name = "GridPilotHud" };
            _container.style.position = Position.Absolute;
            _container.style.left = 16; _container.style.bottom = 96;
            _container.style.width = 200;
            _container.style.backgroundColor = new StyleColor(new Color(T.BgPanel.r, T.BgPanel.g, T.BgPanel.b, 0.85f));
            _container.style.paddingTop = 10; _container.style.paddingBottom = 10;
            _container.style.paddingLeft = 12; _container.style.paddingRight = 12;
            T.Radius(_container, 8);
            T.Border(_container, 1, T.BorderBright);
            _container.pickingMode = PickingMode.Ignore;
            _container.style.display = DisplayStyle.None;
            uiRoot.Add(_container);

            var title = T.Subtitle("SHIP CONTROL");
            _container.Add(title);
            _container.Add(T.Spacer(4));

            _speedLabel = T.StatLabel("Speed: 0 m/s");
            _container.Add(_speedLabel);
            _altLabel = T.StatLabel("Alt: 0 m");
            _container.Add(_altLabel);
            _container.Add(T.Spacer(4));

            _powerLabel = T.StatLabel("Power: 0 W");
            _container.Add(_powerLabel);
            var (pb, pf) = T.ProgressBar(1f, T.AccentGold, 6, true);
            _powerFill = pf; _container.Add(pb);
            _container.Add(T.Spacer(3));

            _h2Label = T.StatLabel("H₂: 0");
            _container.Add(_h2Label);
            var (hb, hf) = T.ProgressBar(0f, T.AccentCyan, 6, true);
            _h2Fill = hf; _container.Add(hb);
            _container.Add(T.Spacer(3));

            _dampLabel = T.StatLabel("Dampeners: ON", T.AccentGreen);
            _container.Add(_dampLabel);

            _container.Add(T.Spacer(6));
            var hint = T.Muted("WASD=Move  Space/Shift=Up/Down\nQ/E=Yaw  Z=Dampeners  F=Exit\nX=Dock/Undock  C=Auto-Export  P=Landing Gear");
            _container.Add(hint);
        }

        public static void Tick()
        {
            if (_container == null) return;

            // Find active cockpit (cached — only search every 0.5s).
            GridCockpit cockpit = _cachedCockpit;
            if (cockpit == null || cockpit.Pilot == null)
            {
                _cockpitSearchTimer += Time.unscaledDeltaTime;
                if (_cockpitSearchTimer > 0.5f)
                {
                    _cockpitSearchTimer = 0;
                    _cachedCockpit = null;
                    var cockpits = Object.FindObjectsByType<GridCockpit>(FindObjectsInactive.Exclude);
                    foreach (var cp in cockpits)
                        if (cp.Pilot != null) { _cachedCockpit = cp; break; }
                    cockpit = _cachedCockpit;
                }
            }

            if (cockpit == null || cockpit.Grid == null)
            {
                _container.style.display = DisplayStyle.None;
                return;
            }

            _container.style.display = DisplayStyle.Flex;
            var grid = cockpit.Grid;

            float speed = grid.Body != null ? grid.Body.linearVelocity.magnitude : 0;
            float alt = grid.transform.position.y;

            _speedLabel.text = $"Speed: {speed:0.0} m/s";
            _altLabel.text = $"Alt: {alt:0} m";

            float powerBal = grid.PowerBalance;
            _powerLabel.text = $"Power: {grid.PowerGenerated:0}W gen / {grid.PowerConsumed:0}W use";
            _powerLabel.style.color = new StyleColor(powerBal >= 0 ? T.AccentGreen : T.AccentRed);
            _powerFill.style.width = new StyleLength(new Length(
                Mathf.Clamp01(grid.PowerGenerated / Mathf.Max(1, grid.PowerConsumed)) * 100, LengthUnit.Percent));

            float h2Fill = grid.HydrogenCapacity > 0 ? grid.HydrogenStored / grid.HydrogenCapacity : 0;
            _h2Label.text = $"H₂: {grid.HydrogenStored:0} / {grid.HydrogenCapacity:0}";
            _h2Fill.style.width = new StyleLength(new Length(Mathf.Clamp01(h2Fill) * 100, LengthUnit.Percent));

            _dampLabel.text = $"Dampeners: {(grid.DampenersOn ? "ON" : "OFF")}";
            _dampLabel.style.color = new StyleColor(grid.DampenersOn ? T.AccentGreen : T.AccentRed);
        }
    }
}
