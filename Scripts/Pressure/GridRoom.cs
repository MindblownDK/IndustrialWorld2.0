// Assets/Scripts/VoxelEngine/Pressure/GridRoom.cs
//
// One volume inside a grid. Holds its cell set, its oxygen charge and the derived
// pressure. Rooms are rebuilt by GridPressureSystem whenever the hull or a door
// changes; their oxygen charge is carried over so pressurising a base is not undone
// by opening a hatch somewhere else on the ship.
//
// An UNSEALED room is not "vacuum" — it is simply open to the sky, so it holds
// exactly whatever the planet outside holds. On an oxygen world that means a
// half-built room, or a room with its door open, is immediately breathable; in
// space or on an airless moon the same room reads hard vacuum.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Pressure
{
    public sealed class GridRoom
    {
        /// <summary>Stable identity across rebuilds: the lowest cell in the volume.</summary>
        public Vector3Int Anchor { get; private set; }

        /// <summary>Grid cells enclosed by this room.</summary>
        public readonly HashSet<Vector3Int> Cells = new();

        /// <summary>Room volume in cubic metres.</summary>
        public float VolumeM3 { get; private set; }

        /// <summary>Oxygen currently held by the SEAL, in litres. Unsealed rooms hold none
        /// of their own — they borrow the planet's air instead.</summary>
        public float OxygenLitres;

        /// <summary>True while the flood fill never escaped to open space.</summary>
        public bool IsSealed { get; private set; } = true;

        /// <summary>The planet's air at this room, refreshed each solve/tick.</summary>
        public AmbientAir Ambient { get; internal set; }

        /// <summary>How many players are currently breathing in this room.</summary>
        public int Occupants { get; internal set; }

        /// <summary>Litres needed for a full 1.0 atm charge.</summary>
        public float CapacityLitres => Mathf.Max(1f, VolumeM3 * PressureRules.LitresPerCubicMetre);

        /// <summary>
        /// Current pressure in atmospheres. A sealed room reports its own charge; an
        /// open room simply reports the planet outside.
        /// </summary>
        public float PressureAtm => IsSealed
            ? Mathf.Clamp(OxygenLitres / CapacityLitres, 0f, 1.5f)
            : Ambient.PressureAtm;

        public float Fill01 => Mathf.Clamp01(PressureAtm / PressureRules.NominalPressureAtm);

        /// <summary>
        /// True when a player can breathe here without a sealed suit. A sealed room
        /// needs its own oxygen charge; an open room needs the planet to be supplying
        /// breathable air at sufficient pressure.
        /// </summary>
        public bool IsBreathable => IsSealed
            ? PressureAtm >= PressureRules.BreathablePressureAtm
            : Ambient.IsOxygenBearing && Ambient.PressureAtm >= PressureRules.BreathablePressureAtm;

        public string StatusLabel
        {
            get
            {
                if (!IsSealed)
                    return IsBreathable ? "OPEN · AMBIENT" : "OPEN · NO AIR";
                if (IsBreathable) return "PRESSURISED";
                return PressureAtm > 0.02f ? "LOW PRESSURE" : "VACUUM";
            }
        }

        public void Reset(bool sealedRoom, float cellSize, AmbientAir ambient)
        {
            IsSealed = sealedRoom;
            Ambient = ambient;
            float cell = Mathf.Max(0.01f, cellSize);
            VolumeM3 = Cells.Count * cell * cell * cell;
            Anchor = ComputeAnchor();

            if (!IsSealed)
            {
                // Open to the sky: the room does not bank any oxygen of its own. It
                // reads the planet directly, so sealing it later starts from ambient.
                OxygenLitres = 0f;
            }
            else
            {
                OxygenLitres = Mathf.Clamp(OxygenLitres, 0f, CapacityLitres);
            }
        }

        /// <summary>
        /// Seeds a newly sealed room with the air that was trapped inside it when the
        /// hull closed. Building a room on an oxygen world therefore starts breathable
        /// at planetary pressure; sealing one in vacuum starts empty.
        /// </summary>
        public void SeedFromAmbient()
        {
            if (!IsSealed) return;
            if (!Ambient.IsOxygenBearing) return;
            OxygenLitres = Mathf.Clamp(Ambient.PressureAtm * CapacityLitres, 0f, CapacityLitres);
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
