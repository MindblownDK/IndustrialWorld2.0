// Assets/Scripts/VoxelEngine/Pressure/GridRoom.cs
//
// One sealed volume inside a grid. Holds its cell set, its oxygen charge and the
// derived pressure. Rooms are rebuilt by GridPressureSystem whenever the hull or a
// door changes; their oxygen charge is carried over so pressurising a base is not
// undone by opening a hatch somewhere else on the ship.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Pressure
{
    public sealed class GridRoom
    {
        /// <summary>Stable identity across rebuilds: the lowest cell in the volume.</summary>
        public Vector3Int Anchor { get; private set; }

        /// <summary>Empty grid cells enclosed by this room.</summary>
        public readonly HashSet<Vector3Int> Cells = new();

        /// <summary>Room volume in cubic metres.</summary>
        public float VolumeM3 { get; private set; }

        /// <summary>Oxygen currently held, in litres.</summary>
        public float OxygenLitres;

        /// <summary>True while the flood fill never escaped to open space.</summary>
        public bool IsSealed { get; private set; } = true;

        /// <summary>Litres needed for a full 1.0 atm charge.</summary>
        public float CapacityLitres => Mathf.Max(1f, VolumeM3 * PressureRules.LitresPerCubicMetre);

        /// <summary>Current pressure in atmospheres (0 = vacuum, 1 = nominal).</summary>
        public float PressureAtm => IsSealed
            ? Mathf.Clamp(OxygenLitres / CapacityLitres, 0f, 1.5f)
            : 0f;

        public float Fill01 => Mathf.Clamp01(PressureAtm / PressureRules.NominalPressureAtm);

        /// <summary>True when a player can breathe here without a sealed suit.</summary>
        public bool IsBreathable => IsSealed && PressureAtm >= PressureRules.BreathablePressureAtm;

        public string StatusLabel => !IsSealed
            ? "BREACHED"
            : IsBreathable ? "PRESSURISED" : PressureAtm > 0.02f ? "LOW PRESSURE" : "VACUUM";

        public void Reset(bool sealedRoom, float cellSize)
        {
            IsSealed = sealedRoom;
            float cell = Mathf.Max(0.01f, cellSize);
            VolumeM3 = Cells.Count * cell * cell * cell;
            Anchor = ComputeAnchor();
            if (!IsSealed) OxygenLitres = 0f;
            else OxygenLitres = Mathf.Clamp(OxygenLitres, 0f, CapacityLitres);
        }

        private Vector3Int ComputeAnchor()
        {
            bool first = true;
            Vector3Int best = Vector3Int.zero;
            foreach (var c in Cells)
            {
                if (first) { best = c; first = false; continue; }
                if (c.y < best.y
                    || (c.y == best.y && c.x < best.x)
                    || (c.y == best.y && c.x == best.x && c.z < best.z))
                    best = c;
            }
            return best;
        }

        /// <summary>Adds oxygen, returns the litres actually accepted.</summary>
        public float AddOxygen(float litres)
        {
            if (!IsSealed || litres <= 0f) return 0f;
            float room = Mathf.Max(0f, CapacityLitres - OxygenLitres);
            float taken = Mathf.Min(room, litres);
            OxygenLitres += taken;
            return taken;
        }

        /// <summary>Removes oxygen, returns the litres actually recovered.</summary>
        public float RemoveOxygen(float litres)
        {
            if (litres <= 0f) return 0f;
            float taken = Mathf.Min(Mathf.Max(0f, OxygenLitres), litres);
            OxygenLitres -= taken;
            return taken;
        }

        public bool Contains(Vector3Int cell) => Cells.Contains(cell);
    }
}
