// Assets/Scripts/VoxelEngine/GridSystem/GridSeasonMonitor.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║        PLANETARY SEASON MONITOR — Screen Data Telemetry Block        ║
// ║                                                                      ║
// ║  A powered grid block that tracks planetary orbital seasons,        ║
// ║  temperature shifts, and climate forecasts for any specified planet   ║
// ║  or the local world, exposing live data to Grid Screens and HUDs.   ║
// ║                                                                      ║
// ║   • AUTO mode tracks the local planet/moon the ship is orbiting or  ║
// ║     landed on.                                                       ║
// ║   • SPECIFIC mode locks a selected planet in the solar system        ║
// ║     (Earth, Titan, Ice Moon, Pirate World, etc.).                    ║
// ║   • Implements IGridDataProvider: attached GridScreenBlocks display  ║
// ║     rich live climate charts, season progress bars, temperature      ║
// ║     readouts, solar/wind modifiers, and weather forecasts.           ║
// ║   • Draws 45 W while online.                                         ║
// ╚══════════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Weather;

namespace VoxelEngine.GridSystem
{
    public class GridSeasonMonitor : GridBlock, IGridDataProvider
    {
        public enum MonitorMode
        {
            AutoCurrentPlanet,
            SpecificPlanet
        }

        [Header("Telemetry Settings")]
        [Tooltip("Power draw (W) while monitor is online.")]
        public float powerDrawWatts = 45f;

        [Tooltip("Auto = tracks current planet/moon; Specific = tracks selected body.")]
        public MonitorMode mode = MonitorMode.AutoCurrentPlanet;

        [Tooltip("Selected target body index when mode is Specific.")]
        public int selectedTargetBodyIndex = 0;

        [Tooltip("Optional Screen Data Object asset reference for authored telemetry binding.")]
        public PlanetSeasonData screenDataObject;

        public override float PowerDraw => Enabled ? powerDrawWatts : 0f;

        // ── Live Telemetry ───────────────────────────────────────────
        public bool IsOnline => Enabled && Grid != null && Grid.HasPower;

        public string TargetName
        {
            get
            {
                if (mode == MonitorMode.AutoCurrentPlanet)
                {
                    var active = GravityProvider.ActiveBody;
                    if (active != null && !string.IsNullOrEmpty(active.DisplayName))
                        return active.DisplayName;
                    return "Home Planet";
                }

                var names = PlanetarySeasons.GetAllMonitoredBodyNames();
                if (names.Count > 0 && selectedTargetBodyIndex >= 0 && selectedTargetBodyIndex < names.Count)
                    return names[selectedTargetBodyIndex];

                return "Specified Planet";
            }
        }

        public PlanetSeasonInfo CurrentSeasonInfo
        {
            get
            {
                if (screenDataObject != null)
                    return screenDataObject.GetLiveSeasonInfo();

                if (mode == MonitorMode.AutoCurrentPlanet)
                    return PlanetarySeasons.GetCurrentSeasonInfo();

                return PlanetarySeasons.GetSeasonInfo(TargetName);
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Season Monitor";
        }

        public void CycleTarget(int delta)
        {
            var names = PlanetarySeasons.GetAllMonitoredBodyNames();
            int count = names.Count;
            if (count == 0) return;

            mode = MonitorMode.SpecificPlanet;
            int cur = selectedTargetBodyIndex < 0 ? 0 : selectedTargetBodyIndex;
            selectedTargetBodyIndex = (cur + delta + count) % count;
        }

        public void SetTargetIndex(int index)
        {
            var names = PlanetarySeasons.GetAllMonitoredBodyNames();
            if (names.Count == 0) return;
            mode = MonitorMode.SpecificPlanet;
            selectedTargetBodyIndex = Mathf.Clamp(index, 0, names.Count - 1);
        }

        // ── IGridDataProvider for GridScreenBlock ────────────────────
        public string SourceName => string.IsNullOrEmpty(TargetName) ? "Season Monitor" : $"Season Monitor ({TargetName})";
        public string DataCategory => "Climate";

        public string GetDisplayData()
        {
            if (!Enabled) return "DISABLED";
            if (!IsOnline) return "OFFLINE";

            var info = CurrentSeasonInfo;
            return info.FormattedSummary();
        }
    }
}
