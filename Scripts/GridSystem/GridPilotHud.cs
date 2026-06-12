// Assets/Scripts/VoxelEngine/GridSystem/GridPilotHud.cs
//
// Overlay HUD shown while the player is piloting a ship/vehicle.
// Shows speed, altitude, power, hydrogen, dampeners, thrust direction.

using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using VoxelEngine.Items;
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
        private static float _smoothSpeed, _smoothAlt, _smoothPower;
        private static VisualElement _toolBar;
        private static readonly List<VisualElement> _toolPills = new();

        public static void EnsureMounted(VisualElement uiRoot)
        {
            if (_root == uiRoot && _container != null && _container.parent == uiRoot) return;
            _root = uiRoot;
            if (_container != null) _container.RemoveFromHierarchy();

            // ── Main Info Container (Bottom Left) ──
            _container = new VisualElement { name = "GridPilotHud" };
            _container.style.position = Position.Absolute;
            _container.style.left = 24; _container.style.bottom = 24;
            _container.style.width = 240;
            _container.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.07f, 0.85f));
            _container.style.paddingTop = 14; _container.style.paddingBottom = 14;
            _container.style.paddingLeft = 16; _container.style.paddingRight = 16;
            T.Radius(_container, 12);
            T.Border(_container, 1, T.BorderBright);
            _container.pickingMode = PickingMode.Ignore;
            _container.style.display = DisplayStyle.None;
            uiRoot.Add(_container);

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 8;
            titleRow.Add(T.IconBadge("🛰", T.AccentCyan));
            var title = T.Subtitle("SHIP SYSTEMS");
            title.style.flexGrow = 1;
            titleRow.Add(title);
            _container.Add(titleRow);

            _speedLabel = T.StatLabel("0.0 m/s", T.TextPrimary);
            _speedLabel.style.fontSize = 20;
            _container.Add(T.StatRow("💨", "Velocity", ""));
            _container.Add(_speedLabel);
            _container.Add(T.Spacer(8));

            _altLabel = T.StatLabel("0 m", T.TextSecondary);
            _container.Add(T.StatRow("🏔", "Altitude", ""));
            _container.Add(_altLabel);
            _container.Add(T.Spacer(10));

            _powerLabel = T.StatLabel("Power", T.AccentGold);
            _container.Add(_powerLabel);
            var (pb, pf) = T.ProgressBar(1f, T.AccentGold, 6, true);
            _powerFill = pf; _container.Add(pb);
            _container.Add(T.Spacer(8));

            _h2Label = T.StatLabel("Hydrogen", T.AccentCyan);
            _container.Add(_h2Label);
            var (hb, hf) = T.ProgressBar(0f, T.AccentCyan, 6, true);
            _h2Fill = hf; _container.Add(hb);
            _container.Add(T.Spacer(12));

            _dampLabel = T.StatLabel("DAMPENERS", T.AccentGreen);
            _dampLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _dampLabel.style.backgroundColor = new StyleColor(new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.15f));
            T.Radius(_dampLabel, 4);
            T.Border(_dampLabel, 1, new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.3f));
            _container.Add(_dampLabel);

            // ── Tool Bar (Bottom Center) ──
            _toolBar = new VisualElement { name = "GridToolBar" };
            _toolBar.style.position = Position.Absolute;
            _toolBar.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            _toolBar.style.bottom = 32;
            _toolBar.style.translate = new StyleTranslate(new Translate(new Length(-50, LengthUnit.Percent), 0));
            _toolBar.style.flexDirection = FlexDirection.Row;
            _toolBar.style.alignItems = Align.Center;
            _toolBar.pickingMode = PickingMode.Ignore;
            uiRoot.Add(_toolBar);

            Tick();
        }

        public static void Tick()
        {
            if (_container == null || _root == null) return;

            if (VoxelEngine.UI.UIState.IsBlocking)
            {
                _container.style.display = DisplayStyle.None;
                if (_toolBar != null) _toolBar.style.display = DisplayStyle.None;
                return;
            }

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
                if (_toolBar != null) _toolBar.style.display = DisplayStyle.None;
                return;
            }

            _container.style.display = DisplayStyle.Flex;
            if (_toolBar != null) _toolBar.style.display = DisplayStyle.Flex;
            
            var grid = cockpit.Grid;
            float dt = Time.unscaledDeltaTime;

            // Smooth Updates
            float targetSpeed = grid.Body != null ? grid.Body.linearVelocity.magnitude : 0;
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, targetSpeed, dt * 5f);
            float targetAlt = grid.transform.position.y;
            _smoothAlt = Mathf.Lerp(_smoothAlt, targetAlt, dt * 5f);
            
            _speedLabel.text = $"{_smoothSpeed:0.0} m/s";
            _altLabel.text = $"{_smoothAlt:0} m";

            float powerBal = grid.PowerBalance;
            float powerLoad = grid.PowerGenerated > 0.1f ? grid.PowerConsumed / grid.PowerGenerated : (grid.PowerConsumed > 0 ? 1f : 0f);
            _smoothPower = Mathf.Lerp(_smoothPower, powerLoad, dt * 5f);
            
            _powerLabel.text = $"Power: {PowerFormat.Watts(grid.PowerConsumed)} / {PowerFormat.Watts(grid.PowerGenerated)}";
            _powerLabel.style.color = new StyleColor(powerBal >= 0 ? T.AccentGreen : T.AccentRed);
            _powerFill.style.width = new StyleLength(new Length(Mathf.Clamp01(_smoothPower) * 100, LengthUnit.Percent));
            _powerFill.style.backgroundColor = new StyleColor(powerBal >= 0 ? T.AccentGold : T.AccentRed);

            float h2Fill = grid.HydrogenCapacity > 0 ? grid.HydrogenStored / grid.HydrogenCapacity : 0;
            _h2Label.text = $"H₂: {grid.HydrogenStored:0} / {grid.HydrogenCapacity:0}";
            _h2Fill.style.width = new StyleLength(new Length(Mathf.Clamp01(h2Fill) * 100, LengthUnit.Percent));

            _dampLabel.text = grid.DampenersOn ? "DAMPENERS: ACTIVE" : "DAMPENERS: DISABLED";
            _dampLabel.style.color = new StyleColor(grid.DampenersOn ? T.AccentGreen : T.AccentRed);
            T.Border(_dampLabel, 1, grid.DampenersOn ? T.AccentGreen : T.AccentRed);

            UpdateToolBar(grid);
        }

        private static void UpdateToolBar(GridEntity grid)
        {
            var groups = grid.GetToolGroups();
            int selectedIdx = grid.SelectedToolIndex;
            if (groups.Count == 0)
            {
                if (_toolBar != null) _toolBar.Clear();
                _toolPills.Clear();
                return;
            }

            // Sync pills
            while (_toolPills.Count < groups.Count)
            {
                var pill = new VisualElement();
                pill.style.paddingLeft = 16; pill.style.paddingRight = 16;
                pill.style.height = 36;
                pill.style.flexDirection = FlexDirection.Row;
                pill.style.alignItems = Align.Center;
                pill.style.marginRight = 8;
                T.Radius(pill, 18); // Oval/Pill shape
                T.Border(pill, 1, T.BorderDim);
                
                var label = new Label();
                label.style.fontSize = 12;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = new StyleColor(T.TextSecondary);
                pill.Add(label);
                
                _toolPills.Add(pill);
                _toolBar.Add(pill);
            }
            while (_toolPills.Count > groups.Count)
            {
                var pill = _toolPills[_toolPills.Count - 1];
                _toolPills.RemoveAt(_toolPills.Count - 1);
                pill.RemoveFromHierarchy();
            }

            for (int i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                var pill = _toolPills[i];
                var label = pill.Q<Label>();
                
                bool isSelected = i == ((selectedIdx % groups.Count) + groups.Count) % groups.Count;
                
                label.text = group == GridEntity.ToolGroup.Drill ? "⚒ DRILL GROUP" : "⚔ WEAPON GROUP";
                
                pill.style.backgroundColor = new StyleColor(isSelected ? new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.3f) : T.BgCard);
                T.Border(pill, 1, isSelected ? T.AccentCyan : T.BorderDim);
                label.style.color = new StyleColor(isSelected ? Color.white : T.TextSecondary);
                
                // Slight scale shift for selected
                pill.style.scale = new StyleScale(new Scale(new Vector2(isSelected ? 1.05f : 1f, isSelected ? 1.05f : 1f)));
            }
        }
    }
}
