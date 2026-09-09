// Assets/Scripts/VoxelEngine/Thermal/ThermalService.cs
//
// Global lookup over every active grid thermal system, mirroring how
// RoomAtmosphereService fronts the pressure systems. Lets the HUD, the player
// hazard model and FX ask "how hot is it where I'm standing" without holding a
// reference to any particular grid.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

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
        /// The sealed volume a block is standing in, or null when it can see the sky.
        /// Sources ask this to decide whether their waste heat has anywhere to go.
        /// </summary>
        public static VoxelEngine.Pressure.GridRoom ConcealedSpaceOf(GridBlock block)
        {
            if (block == null || block.Grid == null) return null;
            var thermal = block.Grid.GetComponent<GridThermalSystem>();
            return thermal != null ? thermal.ConcealedSpaceOf(block) : null;
        }

        /// <summary>
        /// Reports waste power (kJ/s) from a running machine into the compartment around
        /// it. Outdoors this costs nothing at all — the sky is a free heatsink, which is
        /// the whole reason ventilation matters (roadmap 5.1 item 14).
        /// </summary>
        public static void ReportWasteHeat(GridBlock block, float kilojoulesPerSecond)
        {
            if (block == null || block.Grid == null || kilojoulesPerSecond <= 0f) return;
            var room = ConcealedSpaceOf(block);
            if (room == null) return;
            var pressure = block.Grid.GetComponent<VoxelEngine.Pressure.GridPressureSystem>();
            if (pressure != null) pressure.InjectWasteHeat(block.transform.position, kilojoulesPerSecond);
        }
    }
}
