// Assets/Scripts/VoxelEngine/GridSystem/GridPilotHud.cs
//
// Overlay HUD shown while the player is piloting a ship/vehicle.
// Shows speed, altitude, atmosphere, gravity pull, power, hydrogen, dampeners, and a clean readable compass.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.Cosmos;
using VoxelEngine.Items;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.GridSystem
{
    public static class GridPilotHud
    {
        private static VisualElement _root;
        private static VisualElement _container;
        private static GridCockpit _cachedCockpit;
        private static float _cockpitSearchTimer;
        private static Label _speedLabel, _altLabel, _verticalSpeedLabel, _environmentLabel, _gravityGLabel, _gravityDetailLabel, _gravityReferenceLabel, _trajectoryStatusLabel, _trajectorySpeedLabel, _trajectoryApsisLabel, _powerLabel, _h2Label, _dampLabel, _batteryValueLabel, _offlineLabel;
        private static VisualElement _gravityModule, _gravityLcdBezel, _trajectoryModule, _trajectoryLcdBezel, _powerFill, _h2Fill, _batteryGaugeFill;
        private static VisualElement[] _gravitySegments;
        private static float _smoothSpeed, _smoothAlt, _smoothPower;
        private const int LayoutRevision = 11;
        private const int GravitySegmentCount = 8;
        private static readonly Color GravityLcdGlass = new(0.105f, 0.125f, 0.075f, 0.98f);
        private static readonly Color GravityLcdFrame = new(0.31f, 0.37f, 0.21f, 0.88f);
        private static readonly Color GravityLcdInk = new(0.72f, 0.84f, 0.42f, 1f);
        private static readonly Color GravityLcdOff = new(0.085f, 0.10f, 0.07f, 0.98f);
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
            _compassBar.style.backgroundColor = new StyleColor(LcdHudTheme.Chassis);
            _compassBar.style.overflow = Overflow.Hidden;
            _compassBar.pickingMode = PickingMode.Ignore;
            T.Radius(_compassBar, 2);
            T.Border(_compassBar, 1, LcdHudTheme.Bezel);
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
                    ? LcdHudTheme.Phosphor
                    : (medium ? new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.65f)
                        : new Color(LcdHudTheme.PhosphorDim.r, LcdHudTheme.PhosphorDim.g, LcdHudTheme.PhosphorDim.b, 0.55f)));
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
                    ? LcdHudTheme.Phosphor
                    : (major ? new Color(LcdHudTheme.Phosphor.r, LcdHudTheme.Phosphor.g, LcdHudTheme.Phosphor.b, 0.88f)
                        : new Color(LcdHudTheme.PhosphorDim.r, LcdHudTheme.PhosphorDim.g, LcdHudTheme.PhosphorDim.b, 0.82f)));
                label.pickingMode = PickingMode.Ignore;
                _compassMarkers.Add(label);
            }

            var notch = new VisualElement { name = "GridCompassNotch" };
            notch.style.position = Position.Absolute;
            notch.style.top = 21;
            notch.style.left = COMPASS_WIDTH / 2f - 1f;
            notch.style.width = 2;
            notch.style.height = 12;
            notch.style.backgroundColor = new StyleColor(LcdHudTheme.Phosphor);
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
            _compassCenter.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _compassCenter.style.backgroundColor = new StyleColor(LcdHudTheme.Glass);
            T.Radius(_compassCenter, 1);
            T.Border(_compassCenter, 1, LcdHudTheme.Bezel);
            _compassCenter.pickingMode = PickingMode.Ignore;
            _compassBar.Add(_compassCenter);
        }

        private static void BuildSystemsPanel(VisualElement uiRoot)
        {
            _container = new VisualElement { name = "GridPilotHud" };
            _container.style.position = Position.Absolute;
            _container.style.left = 18;
            _container.style.bottom = 18;
            _container.style.width = 302;
            _container.style.paddingTop = 8;
            _container.style.paddingBottom = 8;
            _container.style.paddingLeft = 8;
            _container.style.paddingRight = 8;
            _container.pickingMode = PickingMode.Ignore;
            _container.style.display = DisplayStyle.None;
            LcdHudTheme.ApplyChassis(_container, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.96f), 3f);
            uiRoot.Add(_container);
            LcdHudTheme.AnimateScreenBoot(_container);
            // Step aside while the ship terminal / master terminal is open.
            LcdHudTheme.YieldWhileBlocking(_container);

            var titleRow = LcdHudTheme.CreateDisplayHeader("GRID NAVIGATION", "FLIGHT COMPUTER", "FC-01", "LIVE");
            titleRow.name = "FlightComputerHeader";
            titleRow.style.marginBottom = 5;
            _container.Add(titleRow);

            _container.Add(BuildPrimaryFlightScreen());
            _container.Add(BuildGravityModule());
            _container.Add(BuildTrajectoryModule());
            _container.Add(BuildResourceScreen());
            _container.Add(BuildBatteryGauge());
            _container.Add(BuildDampenerScreen());
        }

        private static VisualElement BuildPrimaryFlightScreen()
        {
            var screen = new VisualElement { name = "FlightPrimaryLcd" };
            screen.style.marginBottom = 7;
            screen.style.paddingLeft = 7;
            screen.style.paddingRight = 7;
            screen.style.paddingTop = 5;
            screen.style.paddingBottom = 5;
            screen.style.overflow = Overflow.Hidden;
            screen.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(screen, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.90f), 1f);
            LcdHudTheme.AddScanlines(screen, 3, top: 9f, spacing: 15f);

            var values = new VisualElement();
            values.style.flexDirection = FlexDirection.Row;
            values.style.alignItems = Align.Stretch;
            values.pickingMode = PickingMode.Ignore;
            screen.Add(values);

            var speedColumn = new VisualElement();
            speedColumn.style.flexGrow = 1;
            speedColumn.pickingMode = PickingMode.Ignore;
            values.Add(speedColumn);
            var speedCaption = LcdHudTheme.CaptionLabel("VELOCITY");
            speedColumn.Add(speedCaption);
            _speedLabel = new Label("0.0 m/s");
            _speedLabel.style.fontSize = 19;
            _speedLabel.style.letterSpacing = 0.45f;
            _speedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _speedLabel.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _speedLabel.pickingMode = PickingMode.Ignore;
            speedColumn.Add(_speedLabel);

            var divider = new VisualElement();
            divider.style.width = 1;
            divider.style.marginLeft = 8;
            divider.style.marginRight = 8;
            divider.style.backgroundColor = new StyleColor(new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.75f));
            divider.pickingMode = PickingMode.Ignore;
            values.Add(divider);

            var altitudeColumn = new VisualElement();
            altitudeColumn.style.width = 112;
            altitudeColumn.pickingMode = PickingMode.Ignore;
            values.Add(altitudeColumn);
            var altitudeCaption = LcdHudTheme.CaptionLabel("ALTITUDE / V-S");
            altitudeColumn.Add(altitudeCaption);
            _altLabel = new Label("0 m");
            _altLabel.style.fontSize = 16;
            _altLabel.style.letterSpacing = 0.35f;
            _altLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _altLabel.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _altLabel.pickingMode = PickingMode.Ignore;
            altitudeColumn.Add(_altLabel);
            _verticalSpeedLabel = new Label("V/S 0.0 m/s");
            _verticalSpeedLabel.style.marginTop = 1;
            _verticalSpeedLabel.style.fontSize = 8;
            _verticalSpeedLabel.style.letterSpacing = 0.35f;
            _verticalSpeedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _verticalSpeedLabel.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
            _verticalSpeedLabel.pickingMode = PickingMode.Ignore;
            altitudeColumn.Add(_verticalSpeedLabel);

            _environmentLabel = new Label("ATMOSPHERE");
            _environmentLabel.style.marginTop = 4;
            _environmentLabel.style.fontSize = 8;
            _environmentLabel.style.letterSpacing = 0.8f;
            _environmentLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _environmentLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _environmentLabel.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
            _environmentLabel.pickingMode = PickingMode.Ignore;
            screen.Add(_environmentLabel);
            return screen;
        }

        private static VisualElement BuildResourceScreen()
        {
            var screen = new VisualElement { name = "FlightResourceLcd" };
            screen.style.marginTop = 8;
            screen.style.marginBottom = 7;
            screen.style.paddingLeft = 7;
            screen.style.paddingRight = 7;
            screen.style.paddingTop = 5;
            screen.style.paddingBottom = 5;
            screen.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(screen, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.90f), 1f);
            LcdHudTheme.AddScanlines(screen, 3, top: 7f, spacing: 14f);

            var powerRow = BuildLcdResourceRow(screen, "BUS", LcdHudTheme.Phosphor);
            _powerLabel = powerRow.label;
            _powerFill = powerRow.fill;
            AddResourceGap(screen);
            var h2Row = BuildLcdResourceRow(screen, "H₂", new Color(0.44f, 0.78f, 0.72f));
            _h2Label = h2Row.label;
            _h2Fill = h2Row.fill;
            return screen;
        }

        private static (Label label, VisualElement fill) BuildLcdResourceRow(VisualElement parent, string code, Color color)
        {
            var row = new VisualElement();
            row.style.height = 19;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.pickingMode = PickingMode.Ignore;
            parent.Add(row);

            var caption = new Label(code);
            caption.style.width = 27;
            caption.style.fontSize = 8;
            caption.style.letterSpacing = 0.8f;
            caption.style.unityFontStyleAndWeight = FontStyle.Bold;
            caption.style.color = new StyleColor(color);
            caption.pickingMode = PickingMode.Ignore;
            row.Add(caption);

            var track = new VisualElement();
            track.style.flexGrow = 1;
            track.style.height = 8;
            track.style.marginRight = 6;
            track.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            T.Radius(track, 1f);
            T.Border(track, 1f, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.82f));
            row.Add(track);

            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.left = 0;
            fill.style.top = 0;
            fill.style.bottom = 0;
            fill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            fill.style.backgroundColor = new StyleColor(color);
            fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);

            var label = new Label("—");
            label.style.width = 122;
            label.style.fontSize = 8;
            label.style.letterSpacing = 0.25f;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.unityTextAlign = TextAnchor.MiddleRight;
            label.style.color = new StyleColor(color);
            label.pickingMode = PickingMode.Ignore;
            row.Add(label);
            return (label, fill);
        }

        private static void AddResourceGap(VisualElement parent)
        {
            var gap = new VisualElement();
            gap.style.height = 3;
            gap.pickingMode = PickingMode.Ignore;
            parent.Add(gap);
        }

        private static VisualElement BuildDampenerScreen()
        {
            var screen = new VisualElement { name = "DampenerLcd" };
            screen.style.height = 22;
            screen.style.paddingLeft = 7;
            screen.style.paddingRight = 7;
            screen.style.flexDirection = FlexDirection.Row;
            screen.style.alignItems = Align.Center;
            screen.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(screen, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.90f), 1f);
            LcdHudTheme.AddScanlines(screen, 2, top: 6f, spacing: 10f);

            var caption = LcdHudTheme.CaptionLabel("INERTIAL");
            caption.style.width = 58;
            screen.Add(caption);
            _dampLabel = new Label("DAMPENERS · ACTIVE");
            _dampLabel.style.flexGrow = 1;
            _dampLabel.style.fontSize = 8;
            _dampLabel.style.letterSpacing = 0.75f;
            _dampLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _dampLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _dampLabel.style.color = new StyleColor(LcdHudTheme.Phosphor);
            _dampLabel.pickingMode = PickingMode.Ignore;
            screen.Add(_dampLabel);
            return screen;
        }

        private static VisualElement BuildGravityModule()
        {
            var module = new VisualElement { name = "CockpitGravityPull" };
            _gravityModule = module;
            module.style.paddingLeft = 7;
            module.style.paddingRight = 7;
            module.style.paddingTop = 6;
            module.style.paddingBottom = 6;
            module.style.backgroundColor = new StyleColor(new Color(0.028f, 0.034f, 0.035f, 0.96f));
            module.pickingMode = PickingMode.Ignore;
            T.Radius(module, 2f);
            T.Border(module, 1f, new Color(0.25f, 0.29f, 0.34f, 0.90f));

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4;
            header.pickingMode = PickingMode.Ignore;
            module.Add(header);

            var title = new Label("GRAVITY FIELD");
            title.style.flexGrow = 1;
            title.style.fontSize = 8;
            title.style.letterSpacing = 1.1f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextMuted);
            title.pickingMode = PickingMode.Ignore;
            header.Add(title);

            var moduleId = new Label("G-06");
            moduleId.style.fontSize = 7;
            moduleId.style.letterSpacing = 0.7f;
            moduleId.style.unityFontStyleAndWeight = FontStyle.Bold;
            moduleId.style.color = new StyleColor(T.TextMuted);
            moduleId.pickingMode = PickingMode.Ignore;
            header.Add(moduleId);

            _gravityLcdBezel = new VisualElement { name = "CockpitGravityLcdBezel" };
            _gravityLcdBezel.style.height = 38;
            _gravityLcdBezel.style.paddingLeft = 5;
            _gravityLcdBezel.style.paddingRight = 5;
            _gravityLcdBezel.style.paddingTop = 3;
            _gravityLcdBezel.style.paddingBottom = 3;
            _gravityLcdBezel.style.backgroundColor = new StyleColor(new Color(0.018f, 0.022f, 0.019f, 0.98f));
            _gravityLcdBezel.pickingMode = PickingMode.Ignore;
            T.Radius(_gravityLcdBezel, 1f);
            T.Border(_gravityLcdBezel, 1f, GravityLcdFrame);
            module.Add(_gravityLcdBezel);

            var lcd = new VisualElement { name = "CockpitGravityLcd" };
            lcd.style.flexGrow = 1;
            lcd.style.backgroundColor = new StyleColor(GravityLcdGlass);
            lcd.style.flexDirection = FlexDirection.Row;
            lcd.style.alignItems = Align.Center;
            lcd.style.overflow = Overflow.Hidden;
            lcd.pickingMode = PickingMode.Ignore;
            T.Radius(lcd, 1f);
            _gravityLcdBezel.Add(lcd);

            for (int i = 0; i < 2; i++)
            {
                var line = new VisualElement();
                line.style.position = Position.Absolute;
                line.style.left = 2;
                line.style.right = 2;
                line.style.top = 8 + i * 15;
                line.style.height = 1;
                line.style.backgroundColor = new StyleColor(new Color(0.77f, 0.88f, 0.48f, 0.06f));
                line.pickingMode = PickingMode.Ignore;
                lcd.Add(line);
            }

            _gravityGLabel = new Label("1.00G");
            _gravityGLabel.style.width = 69;
            _gravityGLabel.style.marginLeft = 5;
            _gravityGLabel.style.fontSize = 18;
            _gravityGLabel.style.letterSpacing = 0.55f;
            _gravityGLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _gravityGLabel.style.color = new StyleColor(GravityLcdInk);
            _gravityGLabel.pickingMode = PickingMode.Ignore;
            lcd.Add(_gravityGLabel);

            _gravityDetailLabel = new Label("09.81 m/s² · COREWARD");
            _gravityDetailLabel.style.flexGrow = 1;
            _gravityDetailLabel.style.marginTop = 1;
            _gravityDetailLabel.style.fontSize = 8;
            _gravityDetailLabel.style.letterSpacing = 0.35f;
            _gravityDetailLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _gravityDetailLabel.style.color = new StyleColor(new Color(GravityLcdInk.r, GravityLcdInk.g, GravityLcdInk.b, 0.82f));
            _gravityDetailLabel.pickingMode = PickingMode.Ignore;
            lcd.Add(_gravityDetailLabel);

            var referenceRow = new VisualElement { name = "CockpitGravityReference" };
            referenceRow.style.flexDirection = FlexDirection.Row;
            referenceRow.style.alignItems = Align.Center;
            referenceRow.style.marginTop = 5;
            referenceRow.pickingMode = PickingMode.Ignore;
            module.Add(referenceRow);

            var referenceCaption = new Label("SFC REF");
            referenceCaption.style.width = 39;
            referenceCaption.style.fontSize = 7;
            referenceCaption.style.letterSpacing = 0.9f;
            referenceCaption.style.unityFontStyleAndWeight = FontStyle.Bold;
            referenceCaption.style.color = new StyleColor(T.TextMuted);
            referenceCaption.pickingMode = PickingMode.Ignore;
            referenceRow.Add(referenceCaption);

            var segmentTrack = new VisualElement { name = "CockpitGravitySegments" };
            segmentTrack.style.flexDirection = FlexDirection.Row;
            segmentTrack.style.flexGrow = 1;
            segmentTrack.style.height = 11;
            segmentTrack.style.paddingLeft = 2;
            segmentTrack.style.paddingRight = 2;
            segmentTrack.style.paddingTop = 2;
            segmentTrack.style.paddingBottom = 2;
            segmentTrack.style.backgroundColor = new StyleColor(new Color(0.018f, 0.022f, 0.019f, 0.98f));
            segmentTrack.pickingMode = PickingMode.Ignore;
            T.Radius(segmentTrack, 1f);
            T.Border(segmentTrack, 1f, new Color(GravityLcdFrame.r, GravityLcdFrame.g, GravityLcdFrame.b, 0.72f));
            referenceRow.Add(segmentTrack);

            _gravitySegments = new VisualElement[GravitySegmentCount];
            for (int i = 0; i < GravitySegmentCount; i++)
            {
                var segment = new VisualElement { name = "CockpitGravitySegment" + i };
                segment.style.flexGrow = 1;
                segment.style.marginRight = i < GravitySegmentCount - 1 ? 1 : 0;
                segment.style.backgroundColor = new StyleColor(GravityLcdOff);
                segment.pickingMode = PickingMode.Ignore;
                T.Radius(segment, 1f);
                _gravitySegments[i] = segment;
                segmentTrack.Add(segment);
            }

            _gravityReferenceLabel = new Label("100%");
            _gravityReferenceLabel.style.width = 35;
            _gravityReferenceLabel.style.marginLeft = 6;
            _gravityReferenceLabel.style.fontSize = 9;
            _gravityReferenceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _gravityReferenceLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _gravityReferenceLabel.style.color = new StyleColor(GravityLcdInk);
            _gravityReferenceLabel.pickingMode = PickingMode.Ignore;
            referenceRow.Add(_gravityReferenceLabel);

            return module;
        }

        private static VisualElement BuildTrajectoryModule()
        {
            _trajectoryModule = new VisualElement { name = "CockpitTrajectoryComputer" };
            _trajectoryModule.style.marginTop = 8;
            _trajectoryModule.style.marginBottom = 10;
            _trajectoryModule.style.paddingLeft = 7;
            _trajectoryModule.style.paddingRight = 7;
            _trajectoryModule.style.paddingTop = 6;
            _trajectoryModule.style.paddingBottom = 6;
            _trajectoryModule.style.backgroundColor = new StyleColor(new Color(0.028f, 0.034f, 0.035f, 0.96f));
            _trajectoryModule.style.display = DisplayStyle.None;
            _trajectoryModule.pickingMode = PickingMode.Ignore;
            T.Radius(_trajectoryModule, 2f);
            T.Border(_trajectoryModule, 1f, new Color(0.25f, 0.29f, 0.34f, 0.90f));

            var header = new VisualElement { name = "TrajectoryHeader" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.marginBottom = 4;
            header.pickingMode = PickingMode.Ignore;
            _trajectoryModule.Add(header);

            var title = new Label("COAST PATH");
            title.style.flexGrow = 1;
            title.style.fontSize = 8;
            title.style.letterSpacing = 1.1f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new StyleColor(T.TextMuted);
            title.pickingMode = PickingMode.Ignore;
            header.Add(title);

            _trajectoryStatusLabel = new Label("SUBORBITAL");
            _trajectoryStatusLabel.style.fontSize = 7;
            _trajectoryStatusLabel.style.letterSpacing = 0.7f;
            _trajectoryStatusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _trajectoryStatusLabel.style.color = new StyleColor(T.AccentAmber);
            _trajectoryStatusLabel.pickingMode = PickingMode.Ignore;
            header.Add(_trajectoryStatusLabel);

            _trajectoryLcdBezel = new VisualElement { name = "TrajectoryLcdBezel" };
            _trajectoryLcdBezel.style.paddingLeft = 5;
            _trajectoryLcdBezel.style.paddingRight = 5;
            _trajectoryLcdBezel.style.paddingTop = 4;
            _trajectoryLcdBezel.style.paddingBottom = 4;
            _trajectoryLcdBezel.style.backgroundColor = new StyleColor(new Color(0.018f, 0.022f, 0.019f, 0.98f));
            _trajectoryLcdBezel.pickingMode = PickingMode.Ignore;
            T.Radius(_trajectoryLcdBezel, 1f);
            T.Border(_trajectoryLcdBezel, 1f, GravityLcdFrame);
            _trajectoryModule.Add(_trajectoryLcdBezel);

            var lcd = new VisualElement { name = "TrajectoryLcd" };
            lcd.style.paddingLeft = 5;
            lcd.style.paddingRight = 5;
            lcd.style.paddingTop = 3;
            lcd.style.paddingBottom = 3;
            lcd.style.backgroundColor = new StyleColor(GravityLcdGlass);
            lcd.style.overflow = Overflow.Hidden;
            lcd.pickingMode = PickingMode.Ignore;
            T.Radius(lcd, 1f);
            _trajectoryLcdBezel.Add(lcd);

            var scanLine = new VisualElement();
            scanLine.style.position = Position.Absolute;
            scanLine.style.left = 2;
            scanLine.style.right = 2;
            scanLine.style.top = 12;
            scanLine.style.height = 1;
            scanLine.style.backgroundColor = new StyleColor(new Color(0.77f, 0.88f, 0.48f, 0.06f));
            scanLine.pickingMode = PickingMode.Ignore;
            lcd.Add(scanLine);

            _trajectorySpeedLabel = new Label("TAN 0.0 · CIRC 0.0 m/s");
            _trajectorySpeedLabel.style.fontSize = 8;
            _trajectorySpeedLabel.style.letterSpacing = 0.35f;
            _trajectorySpeedLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _trajectorySpeedLabel.style.color = new StyleColor(GravityLcdInk);
            _trajectorySpeedLabel.pickingMode = PickingMode.Ignore;
            lcd.Add(_trajectorySpeedLabel);

            _trajectoryApsisLabel = new Label("PE — · AP —");
            _trajectoryApsisLabel.style.marginTop = 2;
            _trajectoryApsisLabel.style.fontSize = 8;
            _trajectoryApsisLabel.style.letterSpacing = 0.35f;
            _trajectoryApsisLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _trajectoryApsisLabel.style.color = new StyleColor(new Color(GravityLcdInk.r, GravityLcdInk.g, GravityLcdInk.b, 0.82f));
            _trajectoryApsisLabel.pickingMode = PickingMode.Ignore;
            lcd.Add(_trajectoryApsisLabel);

            return _trajectoryModule;
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
            var screen = new VisualElement { name = "FlightBatteryLcd" };
            screen.style.marginTop = 8;
            screen.style.marginBottom = 7;
            screen.style.paddingLeft = 7;
            screen.style.paddingRight = 7;
            screen.style.paddingTop = 5;
            screen.style.paddingBottom = 5;
            screen.pickingMode = PickingMode.Ignore;
            LcdHudTheme.ApplyScreen(screen, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.90f), 1f);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.pickingMode = PickingMode.Ignore;
            screen.Add(header);
            var caption = LcdHudTheme.CaptionLabel("GRID BATTERY");
            caption.style.flexGrow = 1;
            header.Add(caption);
            _batteryValueLabel = new Label("NO BATTERY");
            _batteryValueLabel.style.fontSize = 8;
            _batteryValueLabel.style.letterSpacing = 0.55f;
            _batteryValueLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            _batteryValueLabel.style.color = new StyleColor(LcdHudTheme.PhosphorDim);
            _batteryValueLabel.pickingMode = PickingMode.Ignore;
            header.Add(_batteryValueLabel);

            var track = new VisualElement();
            track.style.height = 11;
            track.style.marginTop = 4;
            track.style.backgroundColor = new StyleColor(LcdHudTheme.GlassDark);
            track.style.overflow = Overflow.Hidden;
            track.pickingMode = PickingMode.Ignore;
            T.Radius(track, 1f);
            T.Border(track, 1f, new Color(LcdHudTheme.Bezel.r, LcdHudTheme.Bezel.g, LcdHudTheme.Bezel.b, 0.82f));
            screen.Add(track);

            _batteryGaugeFill = new VisualElement();
            _batteryGaugeFill.style.position = Position.Absolute;
            _batteryGaugeFill.style.left = 0;
            _batteryGaugeFill.style.top = 0;
            _batteryGaugeFill.style.bottom = 0;
            _batteryGaugeFill.style.width = new StyleLength(new Length(0f, LengthUnit.Percent));
            _batteryGaugeFill.style.backgroundColor = new StyleColor(LcdHudTheme.Phosphor);
            _batteryGaugeFill.pickingMode = PickingMode.Ignore;
            track.Add(_batteryGaugeFill);
            return screen;
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

            // Use the rigidbody's physical centre for every flight value. The old
            // smoothed transform sample could keep climbing after a craft had stopped
            // in vacuum; altitude is now a direct, stable body-relative measurement.
            Vector3 telemetryPosition = grid.Body != null ? grid.Body.worldCenterOfMass : grid.transform.position;
            Vector3 telemetryVelocity = grid.Body != null ? grid.Body.linearVelocity : Vector3.zero;
            float targetSpeed = telemetryVelocity.magnitude;
            _smoothSpeed = Mathf.Lerp(_smoothSpeed, targetSpeed, Mathf.Clamp01(dt * 5f));
            AtmosphereSample environment = AtmosphereManager.Sample(telemetryPosition);
            _smoothAlt = Mathf.Max(0f, environment.Altitude);

            _speedLabel.text = $"{_smoothSpeed:0.0} m/s";
            _altLabel.text = float.IsFinite(_smoothAlt) ? FormatFlightAltitude(_smoothAlt) : "DEEP SPACE";
            if (_verticalSpeedLabel != null)
            {
                Vector3 radialUp = GravityProvider.GetUp(telemetryPosition);
                float verticalSpeed = radialUp.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(telemetryVelocity, radialUp.normalized)
                    : telemetryVelocity.y;
                _verticalSpeedLabel.text = $"V/S {verticalSpeed:+0.0;-0.0;0.0} m/s";
                _verticalSpeedLabel.style.color = new StyleColor(
                    Mathf.Abs(verticalSpeed) < 0.05f ? LcdHudTheme.PhosphorDim
                    : verticalSpeed > 0f ? LcdHudTheme.Phosphor : T.AccentAmber);
            }
            if (_environmentLabel != null)
            {
                bool deepSpace = GravityProvider.IsDeepSpace;
                var season = VoxelEngine.Weather.PlanetarySeasons.GetCurrentSeasonInfo();
                string sign = season.effectiveTemperature >= 0f ? "+" : "";
                string tempStr = $"{sign}{season.effectiveTemperature:F1}°C";
                Color environmentColor = deepSpace
                    ? new Color(0.70f, 0.52f, 1.00f)
                    : environment.Band switch
                    {
                        AtmosphereBand.DenseAir => T.AccentCyan,
                        AtmosphereBand.UpperAtmosphere => T.AccentAmber,
                        _ => new Color(0.70f, 0.52f, 1.00f),
                    };
                _environmentLabel.text = deepSpace
                    ? "DEEP SPACE · 0% AIR"
                    : $"{environment.Label} · {environment.Density01 * 100f:0}% AIR · {tempStr}";
                _environmentLabel.style.color = new StyleColor(environmentColor);
            }
            UpdateGravityReadout(grid);
            UpdateTrajectoryReadout(grid);

            float powerBal = grid.PowerBalance;
            float powerLoad = grid.PowerGenerated > 0.1f ? grid.PowerConsumed / grid.PowerGenerated : (grid.PowerConsumed > 0 ? 1f : 0f);
            _smoothPower = Mathf.Lerp(_smoothPower, powerLoad, dt * 5f);
            
            Color busColor = powerBal >= 0 ? LcdHudTheme.Phosphor : T.AccentRed;
            _powerLabel.text = $"{PowerFormat.Watts(grid.PowerConsumed)} / {PowerFormat.Watts(grid.PowerGenerated)}";
            _powerLabel.style.color = new StyleColor(busColor);
            _powerFill.style.width = new StyleLength(new Length(Mathf.Clamp01(_smoothPower) * 100, LengthUnit.Percent));
            _powerFill.style.backgroundColor = new StyleColor(busColor);

            float h2Fill = grid.HydrogenCapacity > 0 ? grid.HydrogenStored / grid.HydrogenCapacity : 0;
            _h2Label.text = $"{grid.HydrogenStored:0} / {grid.HydrogenCapacity:0}";
            _h2Fill.style.width = new StyleLength(new Length(Mathf.Clamp01(h2Fill) * 100, LengthUnit.Percent));

            UpdateBatteryGauge(grid);

            _dampLabel.text = !grid.DampenersOn
                ? "DAMPENERS · OFFLINE"
                : grid.PilotDampenerHoldActive
                    ? "DAMPENERS · HOLDING"
                    : "DAMPENERS · ARMED";
            _dampLabel.style.color = new StyleColor(!grid.DampenersOn ? T.AccentRed
                : grid.PilotDampenerHoldActive ? LcdHudTheme.Phosphor : LcdHudTheme.PhosphorDim);
        }

        private static void UpdateGravityReadout(GridEntity grid)
        {
            if (_gravityGLabel == null || _gravityDetailLabel == null || _gravityReferenceLabel == null
                || _gravitySegments == null || grid == null) return;

            GravityFieldSample gravity = GravityProvider.Sample(grid.transform.position, grid.gravityScale);
            Color ink = ResolveCockpitLcdInk(gravity);
            string direction = gravity.IsRadial ? "COREWARD" : "DOWNWARD";
            int litSegments = Mathf.Clamp(Mathf.RoundToInt(gravity.SurfaceFraction * GravitySegmentCount), 0, GravitySegmentCount);

            _gravityGLabel.text = $"{gravity.Gees:0.00}G";
            _gravityDetailLabel.text = $"{gravity.Magnitude:00.00} m/s² · {direction}";
            _gravityReferenceLabel.text = gravity.IsRadial ? $"{gravity.SurfaceFraction * 100f:0}%" : "100%";
            _gravityGLabel.style.color = new StyleColor(ink);
            _gravityDetailLabel.style.color = new StyleColor(new Color(ink.r, ink.g, ink.b, 0.82f));
            _gravityReferenceLabel.style.color = new StyleColor(ink);
            if (_gravityModule != null)
                T.Border(_gravityModule, 1f, new Color(ink.r, ink.g, ink.b, 0.36f));
            if (_gravityLcdBezel != null)
                T.Border(_gravityLcdBezel, 1f, new Color(ink.r, ink.g, ink.b, 0.70f));

            for (int i = 0; i < _gravitySegments.Length; i++)
            {
                var segment = _gravitySegments[i];
                if (segment == null) continue;
                segment.style.backgroundColor = new StyleColor(i < litSegments
                    ? new Color(ink.r, ink.g, ink.b, 0.90f)
                    : GravityLcdOff);
            }
        }

        private static Color ResolveCockpitLcdInk(GravityFieldSample gravity)
        {
            if (gravity.Gees >= 1.75f) return new Color(0.98f, 0.71f, 0.24f);
            if (gravity.Gees <= 0.20f || gravity.SurfaceFraction <= 0.15f) return new Color(0.45f, 0.74f, 0.90f);
            if (gravity.Gees <= 0.70f || gravity.SurfaceFraction <= 0.50f) return new Color(0.56f, 0.82f, 0.72f);
            return GravityLcdInk;
        }

        private static void UpdateTrajectoryReadout(GridEntity grid)
        {
            if (_trajectoryModule == null || _trajectoryStatusLabel == null || _trajectorySpeedLabel == null
                || _trajectoryApsisLabel == null || grid == null) return;

            Vector3 velocity = grid.Body != null ? grid.Body.linearVelocity : Vector3.zero;
            Vector3 telemetryPosition = grid.Body != null ? grid.Body.worldCenterOfMass : grid.transform.position;
            OrbitalTelemetrySample trajectory = OrbitalTelemetry.Sample(telemetryPosition, velocity, grid.gravityScale);
            var body = GravityProvider.ActiveBody;
            float displayAltitude = body != null
                ? Mathf.Max(25f, body.AtmosphereHeight * 0.35f)
                : float.PositiveInfinity;
            bool show = trajectory.IsAvailable && (trajectory.State == OrbitalFlightState.Orbiting
                || trajectory.State == OrbitalFlightState.Escape
                || trajectory.State == OrbitalFlightState.DeepSpace
                || trajectory.Altitude >= displayAltitude);
            _trajectoryModule.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            Color statusColor = trajectory.State switch
            {
                OrbitalFlightState.Orbiting => T.AccentGreen,
                OrbitalFlightState.DeepSpace => new Color(0.70f, 0.52f, 1.00f),
                OrbitalFlightState.Escape => new Color(0.70f, 0.52f, 1.00f),
                OrbitalFlightState.Suborbital => T.AccentAmber,
                OrbitalFlightState.Atmospheric => T.AccentAmber,
                _ => T.TextMuted,
            };
            Color ink = trajectory.State == OrbitalFlightState.Orbiting ? new Color(0.72f, 0.84f, 0.42f)
                : trajectory.State == OrbitalFlightState.DeepSpace ? new Color(0.70f, 0.58f, 1.00f)
                : trajectory.State == OrbitalFlightState.Escape ? new Color(0.70f, 0.58f, 1.00f)
                : new Color(0.98f, 0.71f, 0.24f);
            string motion = Mathf.Abs(trajectory.RadialSpeed) < 0.5f ? "COAST"
                : trajectory.RadialSpeed > 0f ? "RISING" : "FALLING";

            if (trajectory.State == OrbitalFlightState.DeepSpace)
            {
                // Real-space coast: show frame + nearest body instead of a two-body solution.
                string nearest = "—";
                var reg = VoxelEngine.Cosmos.CosmicRegistry.Instance;
                var origin = VoxelEngine.Cosmos.SpaceOrigin.Instance;
                if (reg != null && origin != null)
                {
                    var near = reg.FindNearestBodyKm(origin.GetCosmicKm(telemetryPosition));
                    if (near != null) nearest = near.DisplayName;
                }
                string coast = Mathf.Abs(trajectory.TangentialSpeed) < 0.5f ? "DRIFTING" : "COASTING";
                _trajectoryStatusLabel.text = "DEEP SPACE · " + coast;
                _trajectoryStatusLabel.style.color = new StyleColor(statusColor);
                _trajectorySpeedLabel.text = $"SPD {trajectory.TangentialSpeed:0.0} m/s · SOL FRAME";
                _trajectoryApsisLabel.text = $"NEAREST BODY · {nearest}";
                _trajectorySpeedLabel.style.color = new StyleColor(ink);
                _trajectoryApsisLabel.style.color = new StyleColor(new Color(ink.r, ink.g, ink.b, 0.82f));
                T.Border(_trajectoryModule, 1f, new Color(statusColor.r, statusColor.g, statusColor.b, 0.38f));
                if (_trajectoryLcdBezel != null)
                    T.Border(_trajectoryLcdBezel, 1f, new Color(ink.r, ink.g, ink.b, 0.70f));
                return;
            }

            string state = trajectory.State switch
            {
                OrbitalFlightState.Orbiting => "ORBITAL",
                OrbitalFlightState.Escape => "ESCAPE",
                OrbitalFlightState.Suborbital => "SUBORBITAL",
                OrbitalFlightState.Atmospheric => "ATMOSPHERIC",
                _ => "SURFACE",
            };

            _trajectoryStatusLabel.text = state + " · " + motion;
            _trajectoryStatusLabel.style.color = new StyleColor(statusColor);
            _trajectorySpeedLabel.text = $"TAN {trajectory.TangentialSpeed:0.0} · CIRC {trajectory.CircularSpeed:0.0} m/s";
            _trajectoryApsisLabel.text = trajectory.IsEscaping
                ? $"PE {FormatTrajectoryDistance(trajectory.PeriapsisAltitude)} · ESC {trajectory.EscapeSpeed:0.0} m/s"
                : $"PE {FormatTrajectoryDistance(trajectory.PeriapsisAltitude)} · AP {FormatTrajectoryDistance(trajectory.ApoapsisAltitude)}";
            _trajectorySpeedLabel.style.color = new StyleColor(ink);
            _trajectoryApsisLabel.style.color = new StyleColor(new Color(ink.r, ink.g, ink.b, 0.82f));
            T.Border(_trajectoryModule, 1f, new Color(statusColor.r, statusColor.g, statusColor.b, 0.38f));
            if (_trajectoryLcdBezel != null)
                T.Border(_trajectoryLcdBezel, 1f, new Color(ink.r, ink.g, ink.b, 0.70f));
        }

        private static string FormatFlightAltitude(float altitude)
        {
            float safeAltitude = Mathf.Max(0f, altitude);
            return safeAltitude >= 1000f
                ? $"{safeAltitude / 1000f:0.0} km"
                : $"{safeAltitude:0} m";
        }

        private static string FormatTrajectoryDistance(float altitude)
        {
            if (float.IsNaN(altitude) || float.IsInfinity(altitude)) return "—";
            string sign = altitude >= 0f ? "+" : "−";
            float abs = Mathf.Abs(altitude);
            return abs >= 1000f ? $"{sign}{abs / 1000f:0.0} km" : $"{sign}{abs:0} m";
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
            _batteryGaugeFill.style.width = new StyleLength(new Length(fill * 100f, LengthUnit.Percent));
            Color batteryColor = fill > 0.2f ? LcdHudTheme.Phosphor : T.AccentAmber;
            _batteryGaugeFill.style.backgroundColor = new StyleColor(batteryColor);
            _batteryValueLabel.text = capacity > 0.01f ? $"{fill * 100f:0}%" : "NO BATTERY";
            _batteryValueLabel.style.color = new StyleColor(capacity > 0.01f ? batteryColor : T.TextMuted);
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
