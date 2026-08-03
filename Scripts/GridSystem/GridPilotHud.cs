// Assets/Scripts/VoxelEngine/GridSystem/GridPilotHud.cs
//
// Overlay HUD shown while the player is piloting a ship/vehicle.
// Shows speed, altitude, atmosphere, gravity pull, power, hydrogen, dampeners, and a clean readable compass.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
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
        private static Label _speedLabel, _altLabel, _environmentLabel, _gravityGLabel, _gravityDetailLabel, _powerLabel, _h2Label, _dampLabel, _batteryValueLabel, _offlineLabel;
        private static VisualElement _gravityModule, _gravityDot, _gravityFill, _powerFill, _h2Fill, _batteryGaugeFill;
        private static float _smoothSpeed, _smoothAlt, _smoothPower;
        private const int LayoutRevision = 8;
        private static int _mountedRevision;
        
        // Compass. The strip is three full 360° cycles wide so heading wrap-around never
        // exposes an empty edge. Labels are spaced widely enough that 180/185/190-style
        // markings remain readable instead of mushing together.
        private static VisualElement _compassBar;
        private static Label _compassCenter;
        private static VisualElement _compassMarkers;
        private const int COMPASS_WIDTH = 440;
        private const float COMPASS_PIXELS_PER_DEGREE = 10f;
        private const int COMPASS_TOTAL_DEGREES = 1080;
        private const int COMPASS_CENTER_DEGREES = 360;
        private const float COMPASS_STRIP_WIDTH = COMPASS_TOTAL_DEGREES * COMPASS_PIXELS_PER_DEGREE;

        public static void EnsureMounted(VisualElement uiRoot)
        {
            RemoveStaleLegacyToolbars(uiRoot);

            bool sameMountedRoot = _root == uiRoot && _container != null && _container.parent == uiRoot;
            if (sameMountedRoot && _mountedRevision == LayoutRevision) return;

            _root = uiRoot;
            _mountedRevision = LayoutRevision;
            if (_container != null) _container.RemoveFromHierarchy();
            if (_compassBar != null) _compassBar.RemoveFromHierarchy();
            if (_offlineLabel != null) _offlineLabel.RemoveFromHierarchy();

            BuildCompass(uiRoot);
            BuildSystemsPanel(uiRoot);
            BuildOfflineWarning(uiRoot);

            Tick();
        }

        private static void RemoveStaleLegacyToolbars(VisualElement uiRoot)
        {
            if (uiRoot == null) return;
            VisualElement stale;
            while ((stale = uiRoot.Q<VisualElement>("GridToolBar")) != null)
                stale.RemoveFromHierarchy();
        }

        private static void BuildCompass(VisualElement uiRoot)
        {
            _compassBar = new VisualElement { name = "GridCompass" };
            _compassBar.style.position = Position.Absolute;
            _compassBar.style.top = 24;
            _compassBar.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            _compassBar.style.translate = new StyleTranslate(new Translate(new Length(-50, LengthUnit.Percent), 0));
            _compassBar.style.width = COMPASS_WIDTH;
            _compassBar.style.height = 60;
            _compassBar.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.07f, 0.78f));
            _compassBar.style.overflow = Overflow.Hidden;
            _compassBar.pickingMode = PickingMode.Ignore;
            T.Radius(_compassBar, 8);
            T.Border(_compassBar, 1, new Color(T.AccentCyan.r, T.AccentCyan.g, T.AccentCyan.b, 0.32f));
            uiRoot.Add(_compassBar);

            _compassMarkers = new VisualElement { name = "GridCompassMarkers" };
            _compassMarkers.style.position = Position.Absolute;
            _compassMarkers.style.left = 0;
            _compassMarkers.style.top = 0;
            _compassMarkers.style.width = COMPASS_STRIP_WIDTH;
            _compassMarkers.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            _compassMarkers.pickingMode = PickingMode.Ignore;
            _compassBar.Add(_compassMarkers);

            for (int deg = 0; deg <= COMPASS_TOTAL_DEGREES; deg += 5)
            {
                int heading = WrapHeading(deg);
                bool cardinal = heading % 90 == 0;
                bool major = cardinal || heading % 30 == 0;
                bool medium = heading % 15 == 0;
                float x = deg * COMPASS_PIXELS_PER_DEGREE;

                var tick = new VisualElement();
                tick.style.position = Position.Absolute;
                tick.style.left = x;
                tick.style.top = cardinal ? 23 : (medium ? 26 : 29);
                tick.style.width = cardinal ? 2 : 1;
                tick.style.height = cardinal ? 16 : (medium ? 12 : 8);
                tick.style.backgroundColor = new StyleColor(cardinal
                    ? Color.white
                    : (medium ? new Color(0.62f, 0.70f, 0.78f, 0.82f) : new Color(0.42f, 0.48f, 0.55f, 0.58f)));
                tick.pickingMode = PickingMode.Ignore;
                _compassMarkers.Add(tick);

                var label = new Label(CompassLabel(heading));
                float labelWidth = cardinal ? 48f : 36f;
                label.style.position = Position.Absolute;
                label.style.left = x - labelWidth * 0.5f;
                label.style.top = 41;
                label.style.width = labelWidth;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.fontSize = cardinal ? 13 : (major ? 10 : 9);
                label.style.unityFontStyleAndWeight = cardinal || major ? FontStyle.Bold : FontStyle.Normal;
                label.style.color = new StyleColor(cardinal
                    ? Color.white
                    : (major ? new Color(0.78f, 0.86f, 0.94f, 0.96f) : new Color(0.58f, 0.65f, 0.74f, 0.82f)));
                label.pickingMode = PickingMode.Ignore;
                _compassMarkers.Add(label);
            }

            var notch = new VisualElement { name = "GridCompassNotch" };
            notch.style.position = Position.Absolute;
            notch.style.top = 21;
            notch.style.left = COMPASS_WIDTH / 2f - 1f;
            notch.style.width = 2;
            notch.style.height = 12;
            notch.style.backgroundColor = new StyleColor(T.AccentCyan);
            notch.pickingMode = PickingMode.Ignore;
            _compassBar.Add(notch);

            _compassCenter = new Label("000°");
            _compassCenter.style.position = Position.Absolute;
            _compassCenter.style.top = 2;
            _compassCenter.style.bottom = StyleKeyword.Auto;
            _compassCenter.style.left = COMPASS_WIDTH / 2f - 36f;
            _compassCenter.style.width = 72;
            _compassCenter.style.height = 18;
            _compassCenter.style.unityTextAlign = TextAnchor.MiddleCenter;
            _compassCenter.style.fontSize = 12;
            _compassCenter.style.unityFontStyleAndWeight = FontStyle.Bold;
            _compassCenter.style.color = new StyleColor(T.AccentCyan);
            _compassCenter.style.backgroundColor = new StyleColor(new Color(0.02f, 0.025f, 0.035f, 0.78f));
            T.Radius(_compassCenter, 8);
            _compassCenter.pickingMode = PickingMode.Ignore;
            _compassBar.Add(_compassCenter);
        }

        private static void BuildSystemsPanel(VisualElement uiRoot)
        {
            _container = new VisualElement { name = "GridPilotHud" };
            _container.style.position = Position.Absolute;
            _container.style.left = 24;
            _container.style.bottom = 24;
            _container.style.width = 240;
            _container.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.07f, 0.85f));
            _container.style.paddingTop = 14;
            _container.style.paddingBottom = 14;
            _container.style.paddingLeft = 16;
            _container.style.paddingRight = 16;
            T.Radius(_container, 12);
            T.Border(_container, 1, T.BorderBright);
            _container.pickingMode = PickingMode.Ignore;
            _container.style.display = DisplayStyle.None;
            uiRoot.Add(_container);

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.alignItems = Align.Center;
            titleRow.style.marginBottom = 8;
            var title = T.Subtitle("SHIP SYSTEMS");
            title.style.flexGrow = 1;
            titleRow.Add(title);
            _container.Add(titleRow);

            _speedLabel = T.StatLabel("0.0 m/s", T.TextPrimary);
            _speedLabel.style.fontSize = 20;
            _container.Add(T.StatRow("", "Velocity", ""));
            _container.Add(_speedLabel);
            _container.Add(T.Spacer(8));

            _altLabel = T.StatLabel("0 m", T.TextSecondary);
            _container.Add(T.StatRow("", "Altitude", ""));
            _container.Add(_altLabel);

            _environmentLabel = T.StatLabel("ATMOSPHERE", T.AccentCyan);
            _environmentLabel.style.fontSize = 9;
            _environmentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _environmentLabel.style.letterSpacing = 0.8f;
            _environmentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _environmentLabel.style.marginTop = 4;
            _container.Add(_environmentLabel);
            _container.Add(T.Spacer(8));
            _container.Add(BuildGravityModule());
            _container.Add(T.Spacer(10));

            _powerLabel = T.StatLabel("Power", T.AccentGold);
            _container.Add(_powerLabel);
            var (pb, pf) = T.ProgressBar(1f, T.AccentGold, 6, true);
            _powerFill = pf;
            _container.Add(pb);
            _container.Add(T.Spacer(8));

            _h2Label = T.StatLabel("Hydrogen", T.AccentCyan);
            _container.Add(_h2Label);
            var (hb, hf) = T.ProgressBar(0f, T.AccentCyan, 6, true);
            _h2Fill = hf;
            _container.Add(hb);
            _container.Add(T.Spacer(10));

            var batteryRow = new VisualElement();
            batteryRow.style.justifyContent = Justify.Center;
            batteryRow.style.alignItems = Align.Center;
            batteryRow.Add(BuildBatteryGauge());
            _container.Add(batteryRow);
            _container.Add(T.Spacer(12));

            _dampLabel = T.StatLabel("DAMPENERS", T.AccentGreen);
            _dampLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _dampLabel.style.backgroundColor = new StyleColor(new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.15f));
            T.Radius(_dampLabel, 4);
            T.Border(_dampLabel, 1, new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.3f));
            _container.Add(_dampLabel);
        }

        private static VisualElement BuildGravityModule()
        {
            var module = new VisualElement { name = "CockpitGravityPull" };
            _gravityModule = module;
            module.style.paddingLeft = 9;
            module.style.paddingRight = 9;
            module.style.paddingTop = 7;
            module.style.paddingBottom = 7;
            module.style.backgroundColor = new StyleColor(new Color(T.AccentPurple.r, T.AccentPurple.g, T.AccentPurple.b, 0.10f));
            module.pickingMode = PickingMode.Ignore;
            T.Radius(module, 6f);
            T.Border(module, 1f, new Color(T.AccentPurple.r, T.AccentPurple.g, T.AccentPurple.b, 0.44f));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.pickingMode = PickingMode.Ignore;
            module.Add(header);

            _gravityDot = new VisualElement { name = "CockpitGravityPulse" };
            _gravityDot.style.width = 6;
            _gravityDot.style.height = 6;
            _gravityDot.style.marginRight = 6;
            _gravityDot.style.backgroundColor = new StyleColor(T.AccentPurple);
            _gravityDot.pickingMode = PickingMode.Ignore;
            T.Radius(_gravityDot, 3f);
            header.Add(_gravityDot);

            var title = new Label("GRAVITY PULL");
            title.style.flexGrow = 1;
            title.style.fontSize = 8;
            title.style.letterSpacing = 1.1f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextMuted);
            title.pickingMode = PickingMode.Ignore;
            header.Add(title);

            _gravityGLabel = new Label("1.00 G");
            _gravityGLabel.style.fontSize = 13;
            _gravityGLabel.style.letterSpacing = 0.35f;
            _gravityGLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _gravityGLabel.style.color = new StyleColor(T.AccentPurple);
            _gravityGLabel.pickingMode = PickingMode.Ignore;
            header.Add(_gravityGLabel);

            _gravityDetailLabel = new Label("9.81 m/s² · COREWARD");
            _gravityDetailLabel.style.fontSize = 9;
            _gravityDetailLabel.style.marginTop = 3;
            _gravityDetailLabel.style.color = new StyleColor(T.TextSecondary);
            _gravityDetailLabel.pickingMode = PickingMode.Ignore;
            module.Add(_gravityDetailLabel);

            var track = new VisualElement { name = "CockpitGravityTrack" };
            track.style.height = 4;
            track.style.marginTop = 5;
            track.style.backgroundColor = new StyleColor(new Color(0.12f, 0.14f, 0.20f, 0.90f));
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            T.Radius(track, 2f);
            module.Add(track);

            _gravityFill = new VisualElement { name = "CockpitGravityFill" };
            _gravityFill.style.position = Position.Absolute;
            _gravityFill.style.left = 0;
            _gravityFill.style.top = 0;
            _gravityFill.style.bottom = 0;
            _gravityFill.style.width = new StyleLength(new Length(100f, LengthUnit.Percent));
            _gravityFill.style.backgroundColor = new StyleColor(T.AccentPurple);
            _gravityFill.pickingMode = PickingMode.Ignore;
            T.Radius(_gravityFill, 2f);
            track.Add(_gravityFill);

            return module;
        }

        private static void BuildOfflineWarning(VisualElement uiRoot)
        {
            _offlineLabel = new Label("POWER OFFLINE");
            _offlineLabel.style.position = Position.Absolute;
            _offlineLabel.style.top = new StyleLength(new Length(42, LengthUnit.Percent));
            _offlineLabel.style.left = new StyleLength(new Length(50, LengthUnit.Percent));
            _offlineLabel.style.translate = new StyleTranslate(new Translate(new Length(-50, LengthUnit.Percent), 0));
            _offlineLabel.style.paddingLeft = 22;
            _offlineLabel.style.paddingRight = 22;
            _offlineLabel.style.paddingTop = 10;
            _offlineLabel.style.paddingBottom = 10;
            _offlineLabel.style.fontSize = 22;
            _offlineLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _offlineLabel.style.letterSpacing = 2.5f;
            _offlineLabel.style.color = new StyleColor(T.AccentRed);
            _offlineLabel.style.backgroundColor = new StyleColor(new Color(0.08f, 0.02f, 0.02f, 0.88f));
            _offlineLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _offlineLabel.pickingMode = PickingMode.Ignore;
            T.Radius(_offlineLabel, 10);
            T.Border(_offlineLabel, 1, T.AccentRed);
            _offlineLabel.style.display = DisplayStyle.None;
            uiRoot.Add(_offlineLabel);
        }

        private static VisualElement BuildBatteryGauge()
        {
            var col = new VisualElement();
            col.style.alignItems = Align.Center;
            col.style.width = 72;
            col.pickingMode = PickingMode.Ignore;

            var lbl = new Label("BATTERY");
            lbl.style.color = new StyleColor(T.TextMuted);
            lbl.style.fontSize = 8;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            lbl.style.letterSpacing = 1.5f;
            lbl.style.marginBottom = 4;
            lbl.pickingMode = PickingMode.Ignore;
            col.Add(lbl);

            var tube = new VisualElement();
            tube.style.width = 56;
            tube.style.height = 72;
            tube.style.backgroundColor = new StyleColor(new Color(0.04f, 0.045f, 0.065f));
            tube.style.overflow = Overflow.Hidden;
            tube.pickingMode = PickingMode.Ignore;
            T.Radius(tube, 5);
            T.Border(tube, 1, T.BorderDim);

            _batteryGaugeFill = new VisualElement();
            _batteryGaugeFill.style.position = Position.Absolute;
            _batteryGaugeFill.style.left = 0;
            _batteryGaugeFill.style.right = 0;
            _batteryGaugeFill.style.bottom = 0;
            _batteryGaugeFill.style.height = new StyleLength(new Length(0, LengthUnit.Percent));
            _batteryGaugeFill.style.backgroundColor = new StyleColor(new Color(0.3f, 0.85f, 0.4f, 0.65f));
            _batteryGaugeFill.pickingMode = PickingMode.Ignore;
            tube.Add(_batteryGaugeFill);

            var sheen = new VisualElement();
            sheen.style.position = Position.Absolute;
            sheen.style.top = 1;
            sheen.style.left = 2;
            sheen.style.right = 2;
            sheen.style.height = 2;
            sheen.style.backgroundColor = new StyleColor(new Color(1f, 1f, 1f, 0.06f));
            sheen.pickingMode = PickingMode.Ignore;
            T.Radius(sheen, 1);
            tube.Add(sheen);

            col.Add(tube);

            _batteryValueLabel = new Label("0% CHARGED");
            _batteryValueLabel.style.color = new StyleColor(T.TextPrimary);
            _batteryValueLabel.style.fontSize = 9;
            _batteryValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _batteryValueLabel.style.marginTop = 4;
            _batteryValueLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _batteryValueLabel.pickingMode = PickingMode.Ignore;
            col.Add(_batteryValueLabel);

            return col;
        }

        public static void Tick()
        {
            if (_container == null || _root == null) return;

            if (VoxelEngine.UI.UIState.IsBlocking)
            {
                _container.style.display = DisplayStyle.None;
                if (_compassBar != null) _compassBar.style.display = DisplayStyle.None;
                if (_offlineLabel != null) _offlineLabel.style.display = DisplayStyle.None;
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
                if (_compassBar != null) _compassBar.style.display = DisplayStyle.None;
                if (_offlineLabel != null) _offlineLabel.style.display = DisplayStyle.None;
                return;
            }

            _container.style.display = DisplayStyle.Flex;
            if (_compassBar != null) _compassBar.style.display = DisplayStyle.Flex;
            
            var grid = cockpit.Grid;
            float dt = Time.unscaledDeltaTime;
            bool powerOffline = !cockpit.Enabled || !grid.HasPower;
            if (_offlineLabel != null)
                _offlineLabel.style.display = powerOffline ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateCompass(grid.transform.eulerAngles.y);

            // Smooth Updates
            float targetSpeed = grid.Body != null ? grid.Body.linearVelocity.magnitude : 0;
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, targetSpeed, dt * 5f);
            AtmosphereSample environment = grid.CurrentAtmosphere;
            _smoothAlt = Mathf.Lerp(_smoothAlt, environment.Altitude, dt * 5f);

            _speedLabel.text = $"{_smoothSpeed:0.0} m/s";
            _altLabel.text = $"{_smoothAlt:0} m";
            if (_environmentLabel != null)
            {
                Color environmentColor = environment.Band switch
                {
                    AtmosphereBand.DenseAir => T.AccentCyan,
                    AtmosphereBand.UpperAtmosphere => T.AccentAmber,
                    _ => new Color(0.70f, 0.52f, 1.00f),
                };
                _environmentLabel.text = $"{environment.Label} · {environment.Density01 * 100f:0}% AIR";
                _environmentLabel.style.color = new StyleColor(environmentColor);
            }
            UpdateGravityReadout(grid);

            float powerBal = grid.PowerBalance;
            float powerLoad = grid.PowerGenerated > 0.1f ? grid.PowerConsumed / grid.PowerGenerated : (grid.PowerConsumed > 0 ? 1f : 0f);
            _smoothPower = Mathf.Lerp(_smoothPower, powerLoad, dt * 5f);
            
            _powerLabel.text = $"Power: {PowerFormat.Watts(grid.PowerConsumed)} / {PowerFormat.Watts(grid.PowerGenerated)}";
            _powerLabel.style.color = new StyleColor(powerBal >= 0 ? T.AccentGreen : T.AccentRed);
            _powerFill.style.width = new StyleLength(new Length(Mathf.Clamp01(_smoothPower) * 100, LengthUnit.Percent));
            _powerFill.style.backgroundColor = new StyleColor(powerBal >= 0 ? T.AccentGold : T.AccentRed);

            float h2Fill = grid.HydrogenCapacity > 0 ? grid.HydrogenStored / grid.HydrogenCapacity : 0;
            _h2Label.text = $"Hydrogen: {grid.HydrogenStored:0} / {grid.HydrogenCapacity:0}";
            _h2Fill.style.width = new StyleLength(new Length(Mathf.Clamp01(h2Fill) * 100, LengthUnit.Percent));

            UpdateBatteryGauge(grid);

            _dampLabel.text = grid.DampenersOn ? "DAMPENERS: ACTIVE" : "DAMPENERS: DISABLED";
            _dampLabel.style.color = new StyleColor(grid.DampenersOn ? T.AccentGreen : T.AccentRed);
            _dampLabel.style.backgroundColor = new StyleColor(grid.DampenersOn
                ? new Color(T.AccentGreen.r, T.AccentGreen.g, T.AccentGreen.b, 0.15f)
                : new Color(T.AccentRed.r, T.AccentRed.g, T.AccentRed.b, 0.12f));
            T.Border(_dampLabel, 1, grid.DampenersOn ? T.AccentGreen : T.AccentRed);
        }

        private static void UpdateGravityReadout(GridEntity grid)
        {
            if (_gravityGLabel == null || _gravityDetailLabel == null || _gravityFill == null || grid == null) return;

            GravityFieldSample gravity = GravityProvider.Sample(grid.transform.position, grid.gravityScale);
            Color accent = gravity.Gees >= 1.75f ? T.AccentAmber
                : gravity.Gees <= 0.20f || gravity.SurfaceFraction <= 0.15f ? T.AccentBlue
                : gravity.Gees <= 0.70f || gravity.SurfaceFraction <= 0.50f ? T.AccentCyan
                : T.AccentPurple;
            string direction = gravity.IsRadial ? "COREWARD" : "DOWNWARD";

            _gravityGLabel.text = $"{gravity.Gees:0.00} G";
            _gravityDetailLabel.text = $"{gravity.Magnitude:0.00} m/s² · {direction}";
            _gravityGLabel.style.color = new StyleColor(accent);
            if (_gravityModule != null)
            {
                _gravityModule.style.backgroundColor = new StyleColor(new Color(accent.r, accent.g, accent.b, 0.10f));
                T.Border(_gravityModule, 1f, new Color(accent.r, accent.g, accent.b, 0.44f));
            }
            _gravityDot.style.backgroundColor = new StyleColor(accent);
            _gravityDot.style.opacity = 0.64f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.28f;
            _gravityFill.style.backgroundColor = new StyleColor(accent);
            T.SetFillPercent(_gravityFill, gravity.SurfaceFraction);
        }

        private static void UpdateBatteryGauge(GridEntity grid)
        {
            if (_batteryGaugeFill == null || _batteryValueLabel == null || grid == null) return;

            float stored = 0f;
            float capacity = 0f;
            foreach (var block in grid.AllBlocks)
            {
                if (block is GridBattery battery)
                {
                    stored += Mathf.Max(0f, battery.storedWh);
                    capacity += Mathf.Max(0f, battery.capacityWh);
                }
            }

            float fill = capacity > 0.01f ? Mathf.Clamp01(stored / capacity) : 0f;
            _batteryGaugeFill.style.height = new StyleLength(new Length(fill * 100f, LengthUnit.Percent));
            _batteryGaugeFill.style.backgroundColor = new StyleColor(fill > 0.2f
                ? new Color(0.3f, 0.85f, 0.4f, 0.65f)
                : new Color(T.AccentAmber.r, T.AccentAmber.g, T.AccentAmber.b, 0.72f));
            _batteryValueLabel.text = capacity > 0.01f ? $"{fill * 100f:0}% CHARGED" : "NO BATTERY";
            _batteryValueLabel.style.color = new StyleColor(capacity > 0.01f
                ? (fill > 0.2f ? T.TextPrimary : T.AccentAmber)
                : T.TextMuted);
        }

        private static void UpdateCompass(float yaw)
        {
            if (_compassMarkers == null || _compassCenter == null) return;

            float heading = Mathf.Repeat(yaw, 360f);
            int headingInt = Mathf.RoundToInt(heading) % 360;
            _compassCenter.text = $"{headingInt:0}°";

            float centerX = (COMPASS_CENTER_DEGREES + heading) * COMPASS_PIXELS_PER_DEGREE;
            _compassMarkers.style.left = (COMPASS_WIDTH * 0.5f) - centerX;
        }

        private static int WrapHeading(int degrees)
        {
            int h = degrees % 360;
            return h < 0 ? h + 360 : h;
        }

        private static string CompassLabel(int heading)
        {
            switch (heading)
            {
                case 0: return "N";
                case 90: return "E";
                case 180: return "S";
                case 270: return "W";
                default: return heading.ToString("0");
            }
        }
    }
}
