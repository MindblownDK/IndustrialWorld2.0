// Assets/Scripts/VoxelEngine/Pressure/GridRoom.cs
//
// One volume inside a grid. Holds its cell set, its oxygen charge, the derived
// pressure and — since 9.32.0 — its own atmosphere: trapped heat and accumulated
// exhaust. Rooms are rebuilt by GridPressureSystem whenever the hull or a door
// changes; their oxygen charge, heat and exhaust are carried over so pressurising
// a base is not undone by opening a hatch somewhere else on the ship.
//
// An UNSEALED room is not "vacuum" — it is simply open to the sky, so it holds
// exactly whatever the planet outside holds. On an oxygen world that means a
// half-built room, or a room with its door open, is immediately breathable; in
// space or on an airless moon the same room reads hard vacuum. The same logic
// governs heat: an open compartment cannot hold a breath of exhaust or a degree
// of waste heat, which is precisely why a blocked-in engine room is dangerous
// and one with an open hatch is not.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Thermal;

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

        // ── Concealed-space atmosphere (roadmap 5.1 item 14, 9.32.0) ──────────
        // A volume that cannot exchange gas with the sky accumulates what is pumped
        // into it: waste heat from machinery and the exhaust of anything venting
        // inside it. Both are °C above ambient and both only survive while the room
        // is sealed.

        /// <summary>Waste heat trapped in this volume, expressed as °C above the outside
        /// air. This is what survives on a save: the cooked-in state of the space.</summary>
        public float HeatLoadC;

        /// <summary>°C of hot gas currently accumulated here (how foul the air has become).</summary>
        public float ExhaustHeatC;

        /// <summary>Waste power (kJ/s) every machine in this volume reported last round.</summary>
        public float PendingWasteKJperS;

        /// <summary>Waste power (kJ/s) reported by running machinery since the last thermal tick.</summary>
        public float ReportedWasteKJperS;

        /// <summary>Sum of the stream temperatures reported here this round (averaged on solve).</summary>
        public float ExhaustSumC;
        /// <summary>How many sources restated their exhaust this round.</summary>
        public int ExhaustReporters;
        /// <summary>How many sources restated their waste heat this round.</summary>
        public int ReportedSources;

        /// <summary>0..1 how foul the air has become. Reported on panels and drives crew comfort.</summary>
        public float ExhaustLoad01 => Mathf.Clamp01(ExhaustHeatC / ThermalRules.RoomFoulAirReferenceC);

        /// <summary>True once the trapped air is hot enough to start hurting what is inside it.</summary>
        public bool IsOverheating => IsSealed && RoomRiseC >= ThermalRules.RoomDamageHeatC;

        /// <summary>The temperature the trapped heat has baked into this space, in °C above outside air.</summary>
        public float RoomRiseC => IsSealed ? Mathf.Max(HeatLoadC, TemperatureC - ExteriorTemperatureC) : 0f;

        /// <summary>Room air temperature in °C. An open compartment simply mirrors the planet.</summary>
        public float TemperatureC { get; internal set; } = ThermalRules.FallbackAmbientC;

        /// <summary>Outside air the volume was last measured against, in °C.</summary>
        public float ExteriorTemperatureC { get; internal set; } = ThermalRules.FallbackAmbientC;

        /// <summary>
        /// Air temperature the crew and the machinery inside this volume are actually
        /// standing in: the room's own atmosphere when sealed, the planet when open.
        /// </summary>
        public float AirTemperatureC => IsSealed ? TemperatureC : ExteriorTemperatureC;

        /// <summary>
        /// Air pressure available for combustion in this volume. A sealed room offers
        /// its own charge; an open one offers the planet, exactly as breathing works.
        /// </summary>
        public float CombustionAirAtm => IsSealed ? PressureAtm : Ambient.PressureAtm;

        /// <summary>Coarse thermal band for HUD copy and colours.</summary>
        public VoxelEngine.Thermal.ThermalBand Band =>
            ThermalRules.RoomBand(RoomRiseC, ExhaustLoad01);

        /// <summary>Volume heat capacity in kJ/K — how hard it is to cook this space.</summary>
        public float HeatCapacityKJperK => Mathf.Max(1f, VolumeM3 * ThermalRules.RoomHeatKJPerKPerM3);

        /// <summary>
        /// Waste power a machine is pushing into this volume. Sources restate their
        /// contribution every round — including with zero when they have stopped — so a
        /// shut-down engine drops out of the average instead of lingering at its peak.
        /// </summary>
        public void ReportWasteHeat(float kilojoulesPerSecond)
        {
            if (!IsSealed) return;
            ReportedWasteKJperS += Mathf.Max(0f, kilojoulesPerSecond);
            ReportedSources++;
        }

        /// <summary>
        /// Hot gas a stack is releasing in here. Exhaust never accumulates as room heat
        /// directly — that would let a hot room cook its own machinery and feed itself —
        /// it saturates against a reference stream temperature and is read as foul air.
        /// Averaged across the stacks present, so one idle funnel genuinely tells the
        /// room the stream has stopped.
        /// </summary>
        public void ReportExhaust(float streamTemperatureC)
        {
            if (!IsSealed) return;
            float value = Mathf.Min(Mathf.Max(0f, streamTemperatureC), ThermalRules.RoomExhaustReferenceC);
            ExhaustSumC += value;
            ExhaustReporters++;
        }

        /// <summary>
        /// Removes trapped heat and foul gas. Returns the heat actually extracted, so a
        /// scrubber can meter its work against what the room could give up.
        /// </summary>
        public float ExtractAtmosphere(float heatDeltaC, float exhaustDeltaC)
        {
            if (!IsSealed) return 0f;

            float heatRemoved = Mathf.Min(HeatLoadC, Mathf.Max(0f, heatDeltaC));
            HeatLoadC -= heatRemoved;

            float exhaustRemoved = Mathf.Min(ExhaustHeatC, Mathf.Max(0f, exhaustDeltaC));
            ExhaustHeatC -= exhaustRemoved;

            // Whatever the scrubber pulled out of the air, the room can no longer radiate back.
            TemperatureC = Mathf.Max(ExteriorTemperatureC + HeatLoadC, TemperatureC - heatRemoved);
            return heatRemoved;
        }

        /// <summary>
        /// Draws the combustion air an engine needs straight out of the room. Refuses
        /// to take the last breath of a crew compartment: life support outranks power.
        /// </summary>
        public float DrawCombustionOxygen(float litres)
        {
            if (litres <= 0f || !IsSealed) return 0f;

            float keep = ThermalRules.CombustionAirReserveAtm * CapacityLitres;
            float spare = Mathf.Max(0f, OxygenLitres - keep);
            float taken = Mathf.Min(spare, litres);
            if (taken <= 0f) return 0f;

            OxygenLitres -= taken;
            return taken;
        }

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
                if (IsOverheating) return "OVERHEATING";
                if (ExhaustLoad01 >= 0.6f) return "FOUL AIR";
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
                // Heat and foul gas behave exactly the same way — an open hatch is a
                // real, immediate relief for a cooked engine room.
                OxygenLitres = 0f;
                // Trapped heat and foul air are not banked either: GridPressureSystem
                // only carries them forward for volumes that are still sealed, so
                // cracking a hatch on a cooked engine room vents it for real.
            }
            else
            {
                OxygenLitres = Mathf.Clamp(OxygenLitres, 0f, CapacityLitres);
                HeatLoadC = Mathf.Clamp(HeatLoadC, 0f, ThermalRules.RoomMaxRiseC);
                ExhaustHeatC = Mathf.Clamp(ExhaustHeatC, 0f, ThermalRules.RoomExhaustReferenceC);
                TemperatureC = Mathf.Max(TemperatureC, ExteriorTemperatureC + HeatLoadC);
                PendingWasteKJperS = 0f;
                ReportedWasteKJperS = 0f;
                ReportedSources = 0;
                ExhaustSumC = 0f;
                ExhaustReporters = 0;
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
