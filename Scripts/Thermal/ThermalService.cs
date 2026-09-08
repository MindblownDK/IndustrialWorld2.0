// Assets/Scripts/VoxelEngine/Thermal/ThermalService.cs
//
// Global lookup over every active grid thermal system, mirroring how
// RoomAtmosphereService fronts the pressure systems. Lets the HUD, the player
// hazard model and FX ask "how hot is it where I'm standing" without holding a
// reference to any particular grid.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Thermal
{
    public static class ThermalService
    {
        private static readonly List<GridThermalSystem> Systems = new();

        public static void Register(GridThermalSystem system)
        {
            if (system != null && !Systems.Contains(system)) Systems.Add(system);
        }

        public static void Unregister(GridThermalSystem system)
        {
            if (system != null) Systems.Remove(system);
        }

        /// <summary>Every live thermal system, for HUD/debug enumeration.</summary>
        public static IReadOnlyList<GridThermalSystem> All => Systems;

        /// <summary>The hottest grid currently burning, or null when nothing is hot.</summary>
        public static GridThermalSystem HottestBurning()
        {
            GridThermalSystem best = null;
            float peak = 0f;
            for (int i = 0; i < Systems.Count; i++)
            {
                var s = Systems[i];
                if (s == null || !s.IsBurning) continue;
                if (best == null || s.PeakTemperatureC > peak)
                {
                    best = s;
                    peak = s.PeakTemperatureC;
                }
            }
            return best;
        }

        /// <summary>
        /// The thermal system of the grid nearest this position, within range. Used to
        /// decide whether a standing player is aboard a hull that is currently burning.
        /// </summary>
        public static GridThermalSystem NearestTo(Vector3 worldPosition, float maxDistance = 60f)
        {
            GridThermalSystem best = null;
            float bestSqr = maxDistance * maxDistance;

            for (int i = 0; i < Systems.Count; i++)
            {
                var s = Systems[i];
                if (s == null) continue;
                float sqr = (s.transform.position - worldPosition).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = s;
                }
            }
            return best;
        }

        /// <summary>Ambient temperature (°C) a player at this position is exposed to.</summary>
        public static float AmbientAt(Vector3 worldPosition)
            => ThermalRules.AmbientTemperatureC(worldPosition);

        /// <summary>
        /// Peak exhaust-plume heat (°C) at a world position — how hot it is to
        /// stand in a live thruster flame right now. 0 when no plume reaches.
        /// </summary>
        public static float ExhaustHeatAt(Vector3 worldPosition)
        {
            float best = 0f;
            for (int i = 0; i < Systems.Count; i++)
            {
                var s = Systems[i];
                if (s == null) continue;

                var cells = s.WorldPlumeCells;
                for (int c = 0; c < cells.Count; c++)
                {
                    var cell = cells[c];
                    float r = cell.Radius;
                    if ((cell.Pos - worldPosition).sqrMagnitude <= r * r && cell.HeatC > best)
                        best = cell.HeatC;
                }
            }
            return best;
        }
    }
}
