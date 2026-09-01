// Assets/Scripts/Weather/PlanetSeasonData.cs
//
// Screen Data Object for Planetary Seasons.
// Can be created via Create ▸ Voxel Engine ▸ Planets ▸ Season Data Object
// or used at runtime by Grid Screens and telemetry monitors to bind to a
// specific planetary body's seasonal climate telemetry.

using System;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// Screen data object that configures and provides live seasonal telemetry
    /// for a target celestial planet or moon.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Planets/Season Data Object", fileName = "ScreenData_Seasons_")]
    public class PlanetSeasonData : ScriptableObject
    {
        public enum TargetPlanetMode
        {
            CurrentLocalWorld,
            SpecifiedPlanetTemplate,
            SpecifiedPlanetName
        }

        [Header("Target Planet")]
        [Tooltip("How the target planet is resolved.")]
        public TargetPlanetMode targetMode = TargetPlanetMode.CurrentLocalWorld;

        [Tooltip("Target planet template asset when targetMode is SpecifiedPlanetTemplate.")]
        public PlanetTemplate targetPlanetTemplate;

        [Tooltip("Target body name (e.g. 'Earth', 'Planet_Titan', 'Pirate') when targetMode is SpecifiedPlanetName.")]
        public string targetBodyName = "Earth";

        [Header("Display Configuration")]
        [Tooltip("Title shown on screens.")]
        public string displayTitle = "PLANETARY CLIMATE TELEMETRY";

        [Tooltip("Format mode: Summary, Bars, Detailed.")]
        public GridSystem.ScreenDataMode preferredDisplayMode = GridSystem.ScreenDataMode.Summary;

        /// <summary>
        /// Query the current live season info for the configured target planet.
        /// </summary>
        public PlanetSeasonInfo GetLiveSeasonInfo()
        {
            switch (targetMode)
            {
                case TargetPlanetMode.SpecifiedPlanetTemplate:
                    if (targetPlanetTemplate != null && targetPlanetTemplate.body != null)
                        return PlanetarySeasons.GetSeasonInfo(targetPlanetTemplate.body);
                    break;

                case TargetPlanetMode.SpecifiedPlanetName:
                    if (!string.IsNullOrEmpty(targetBodyName))
                        return PlanetarySeasons.GetSeasonInfo(targetBodyName);
                    break;

                case TargetPlanetMode.CurrentLocalWorld:
                default:
                    return PlanetarySeasons.GetCurrentSeasonInfo();
            }

            return PlanetarySeasons.GetCurrentSeasonInfo();
        }

        /// <summary>
        /// Formatted string ready to be rendered onto any Grid Screen or HUD.
        /// </summary>
        public string GetFormattedScreenText(GridSystem.ScreenDataMode mode)
        {
            var info = GetLiveSeasonInfo();
            return mode switch
            {
                GridSystem.ScreenDataMode.Bars => info.FormattedBars(),
                GridSystem.ScreenDataMode.System => info.FormattedDetailed(),
                _ => info.FormattedSummary()
            };
        }
    }
}
