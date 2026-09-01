// Assets/Scripts/VoxelEngine/Weather/SeasonMonitorUI.cs
//
// Interactive UI panel for the Grand Planetary Observatory / Season Monitor Station.

using UnityEngine;
using UnityEngine.UIElements;
using VoxelEngine.UI;
using T = VoxelEngine.UI.UITheme;

namespace VoxelEngine.Weather
{
    public static class SeasonMonitorUI
    {
        public static VisualElement BuildPanel(StaticSeasonMonitor sm)
        {
            var p = T.MachinePanel();
            p.style.width = 470;

            bool online = sm != null && sm.IsOnline;
            var (hdr, _, _, _) = T.HeaderRow("🔭 Planetary Observatory",
                online ? "ACTIVE" : "OFFLINE",
                online ? T.AccentGreen : T.AccentAmber);
            p.Add(hdr);
            p.Add(T.AccentDivider(T.AccentCyan));
            p.Add(T.Spacer(6));

            if (sm == null) return p;

            // Target Planet section
            p.Add(SectionTitle("Target Celestial Body"));
            var modeRow = new VisualElement();
            modeRow.style.flexDirection = FlexDirection.Row;
            modeRow.style.marginBottom = 6;

            var autoBtn = T.SmallButton("◎ AUTO / LOCAL", () =>
            {
                sm.mode = StaticSeasonMonitor.MonitorMode.AutoCurrentPlanet;
                GameUIController.Instance?.RefreshCurrentPanel();
            }, sm.mode == StaticSeasonMonitor.MonitorMode.AutoCurrentPlanet ? T.AccentGreen : T.AccentDim);
            autoBtn.style.marginRight = 6;
            modeRow.Add(autoBtn);

            var specBtn = T.SmallButton("◈ SPECIFIC PLANET", () =>
            {
                sm.mode = StaticSeasonMonitor.MonitorMode.SpecificPlanet;
                GameUIController.Instance?.RefreshCurrentPanel();
            }, sm.mode == StaticSeasonMonitor.MonitorMode.SpecificPlanet ? T.AccentCyan : T.AccentDim);
            modeRow.Add(specBtn);
            p.Add(modeRow);

            // Planet Cycle Row
            var cycleRow = new VisualElement();
            cycleRow.style.flexDirection = FlexDirection.Row;
            cycleRow.style.alignItems = Align.Center;
            cycleRow.style.marginBottom = 8;

            var prevBtn = T.SmallButton("◀", () => { sm.CycleTarget(-1); GameUIController.Instance?.RefreshCurrentPanel(); }, T.AccentCyan);
            prevBtn.style.marginRight = 8;
            cycleRow.Add(prevBtn);

            var targetLabel = new Label(sm.TargetName);
            targetLabel.style.flexGrow = 1;
            targetLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            targetLabel.style.fontSize = 12;
            targetLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            targetLabel.style.color = new StyleColor(new Color(0.9f, 0.95f, 1f));
            cycleRow.Add(targetLabel);

            var nextBtn = T.SmallButton("▶", () => { sm.CycleTarget(1); GameUIController.Instance?.RefreshCurrentPanel(); }, T.AccentCyan);
            nextBtn.style.marginLeft = 8;
            cycleRow.Add(nextBtn);
            p.Add(cycleRow);

            // Live Season Card
            var info = sm.GetSeasonInfo();
            var seasonCard = new VisualElement();
            seasonCard.style.backgroundColor = new StyleColor(new Color(0.08f, 0.10f, 0.14f, 0.90f));
            seasonCard.style.borderTopWidth = 1;
            seasonCard.style.borderBottomWidth = 1;
            seasonCard.style.borderLeftWidth = 1;
            seasonCard.style.borderRightWidth = 1;
            seasonCard.style.borderTopColor = new StyleColor(new Color(0.18f, 0.40f, 0.60f, 0.45f));
            seasonCard.style.borderBottomColor = new StyleColor(new Color(0.18f, 0.40f, 0.60f, 0.45f));
            seasonCard.style.borderLeftColor = new StyleColor(new Color(0.18f, 0.40f, 0.60f, 0.45f));
            seasonCard.style.borderRightColor = new StyleColor(new Color(0.18f, 0.40f, 0.60f, 0.45f));
            seasonCard.style.borderTopLeftRadius = 6;
            seasonCard.style.borderTopRightRadius = 6;
            seasonCard.style.borderBottomLeftRadius = 6;
            seasonCard.style.borderBottomRightRadius = 6;
            seasonCard.style.paddingLeft = 10;
            seasonCard.style.paddingRight = 10;
            seasonCard.style.paddingTop = 8;
            seasonCard.style.paddingBottom = 8;
            seasonCard.style.marginBottom = 8;

            var seasonHeaderRow = new VisualElement();
            seasonHeaderRow.style.flexDirection = FlexDirection.Row;
            seasonHeaderRow.style.justifyContent = Justify.SpaceBetween;
            seasonHeaderRow.style.alignItems = Align.Center;

            var seasonTitle = new Label($"{info.SeasonIcon} {info.SeasonName.ToUpperInvariant()}");
            seasonTitle.style.fontSize = 14;
            seasonTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            seasonTitle.style.color = new StyleColor(info.currentSeason switch
            {
                Season.Summer => new Color(1f, 0.85f, 0.35f),
                Season.Winter => new Color(0.45f, 0.85f, 1f),
                Season.Autumn => new Color(1f, 0.60f, 0.30f),
                _ => new Color(0.40f, 0.95f, 0.55f)
            });
            seasonHeaderRow.Add(seasonTitle);

            var yearBadge = new Label($"Year {info.currentYear} • Day {info.dayOfYear}/{info.totalDaysInYear}");
            yearBadge.style.fontSize = 10;
            yearBadge.style.color = new StyleColor(new Color(0.65f, 0.70f, 0.80f));
            seasonHeaderRow.Add(yearBadge);
            seasonCard.Add(seasonHeaderRow);

            // Progress bar
            var barTrack = new VisualElement();
            barTrack.style.height = 8;
            barTrack.style.backgroundColor = new StyleColor(new Color(0.04f, 0.05f, 0.07f));
            barTrack.style.borderTopLeftRadius = 4;
            barTrack.style.borderTopRightRadius = 4;
            barTrack.style.borderBottomLeftRadius = 4;
            barTrack.style.borderBottomRightRadius = 4;
            barTrack.style.marginTop = 6;
            barTrack.style.marginBottom = 4;
            barTrack.style.overflow = Overflow.Hidden;

            var barFill = new VisualElement();
            barFill.style.height = new StyleLength(new Length(100, LengthUnit.Percent));
            barFill.style.width = new StyleLength(new Length(info.seasonProgress * 100f, LengthUnit.Percent));
            barFill.style.backgroundColor = new StyleColor(new Color(0.25f, 0.75f, 1f));
            barTrack.Add(barFill);
            seasonCard.Add(barTrack);

            var progLabel = new Label($"Season Progress: {info.seasonDay}/{info.daysInSeason} days ({Mathf.RoundToInt(info.seasonProgress * 100f)}%) • {info.daysRemainingInSeason} days until {info.NextSeasonName}");
            progLabel.style.fontSize = 9;
            progLabel.style.color = new StyleColor(new Color(0.60f, 0.65f, 0.75f));
            seasonCard.Add(progLabel);
            p.Add(seasonCard);

            // Climate Telemetry Grid
            p.Add(SectionTitle("Orbital Climate Telemetry"));
            var teleGrid = new VisualElement();
            teleGrid.style.flexDirection = FlexDirection.Row;
            teleGrid.style.flexWrap = Wrap.Wrap;
            teleGrid.style.justifyContent = Justify.SpaceBetween;

            string tempSign = info.effectiveTemperature >= 0f ? "+" : "";
            string offsetSign = info.seasonalTemperatureOffset >= 0f ? "+" : "";
            Color tempColor = info.isFreezing ? new Color(0.45f, 0.85f, 1f) : new Color(1f, 0.60f, 0.35f);

            teleGrid.Add(StatBox("Surface Temp", $"{tempSign}{info.effectiveTemperature:F1}°C", $"Base {info.baseTemperature:F0}°C ({offsetSign}{info.seasonalTemperatureOffset:F1}°C)", tempColor));
            teleGrid.Add(StatBox("Solar Irradiance", $"{Mathf.RoundToInt(info.solarMultiplier * 100f)}%", "Annual solar flux multiplier", new Color(1f, 0.88f, 0.40f)));
            teleGrid.Add(StatBox("Wind Factor", $"{Mathf.RoundToInt(info.windMultiplier * 100f)}%", "Seasonal gale power boost", new Color(0.50f, 0.90f, 0.70f)));
            teleGrid.Add(StatBox("Precipitation", info.forecastPrecipitation, $"Orbital Phase {info.orbitalPhaseDegrees:F0}°", new Color(0.70f, 0.85f, 1f)));
            p.Add(teleGrid);

            p.Add(T.Spacer(6));
            var pwrRow = new VisualElement();
            pwrRow.style.flexDirection = FlexDirection.Row;
            pwrRow.style.justifyContent = Justify.SpaceBetween;
            pwrRow.style.alignItems = Align.Center;

            var pwrLabel = new Label("⚡ Power Draw: 40 W");
            pwrLabel.style.fontSize = 10;
            pwrLabel.style.color = new StyleColor(new Color(0.60f, 0.65f, 0.75f));
            pwrRow.Add(pwrLabel);

            var statusLabel = new Label("METEOROLOGICAL DOPPLER RADAR ONLINE");
            statusLabel.style.fontSize = 9;
            statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusLabel.style.color = new StyleColor(T.AccentGreen);
            pwrRow.Add(statusLabel);
            p.Add(pwrRow);

            return p;
        }

        private static VisualElement SectionTitle(string title)
        {
            var l = new Label(title);
            l.style.fontSize = 10;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.color = new StyleColor(T.AccentCyan);
            l.style.marginBottom = 4;
            return l;
        }

        private static VisualElement StatBox(string title, string value, string subtitle, Color valColor)
        {
            var box = new VisualElement();
            box.style.width = new StyleLength(new Length(48, LengthUnit.Percent));
            box.style.backgroundColor = new StyleColor(new Color(0.06f, 0.08f, 0.11f, 0.90f));
            box.style.borderTopLeftRadius = 4;
            box.style.borderTopRightRadius = 4;
            box.style.borderBottomLeftRadius = 4;
            box.style.borderBottomRightRadius = 4;
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.marginBottom = 6;

            var t = new Label(title);
            t.style.fontSize = 9;
            t.style.color = new StyleColor(new Color(0.60f, 0.65f, 0.75f));
            box.Add(t);

            var v = new Label(value);
            v.style.fontSize = 13;
            v.style.unityFontStyleAndWeight = FontStyle.Bold;
            v.style.color = new StyleColor(valColor);
            v.style.marginTop = 1;
            v.style.marginBottom = 1;
            box.Add(v);

            var s = new Label(subtitle);
            s.style.fontSize = 8;
            s.style.color = new StyleColor(new Color(0.45f, 0.50f, 0.60f));
            box.Add(s);

            return box;
        }
    }
}
