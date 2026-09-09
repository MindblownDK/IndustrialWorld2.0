// Assets/Scripts/VoxelEngine/Pressure/VentilationRules.cs
//
// VENTILATION RULES — the shared contract for how much air a ventilation block may move
// through a compartment of a given size.
//
// Every airflow number in this game was authored as an absolute litres per second, which is
// fine on a three-by-three engine closet and useless on a converted hangar: the same 24 L/s
// air vent that pressurises a small compartment in a minute or two takes the best part of an
// hour on a room four hundred times the size, because the room's litres scale and the fan's
// do not. The scrubber went the other way and was authored in air changes per minute, which
// scales perfectly and then outruns the oxygen a supply line can actually deliver.
//
// This class is where both directions are reconciled. A block keeps the litres-per-second
// figure the author set — it is still the ceiling, still what a tooltip promises, still what
// a prefab can pin — and the rules add one floor and one clamp on top of it:
//
//   floor   an automatic unit never drops below a trickle of air changes per minute, so a
//           big room is still being ventilated on a scale a player can perceive;
//   clamp   an automatic unit never moves more air per second than the room's whole charge
//           can be replaced in the authored time, and never scales at all with no feed line
//           behind it, so scaling never turns a grille into a free atmosphere generator.
//
// Hand-authored blocks (autoScale off) are left exactly as written. Nothing here invents a
// new resource: it only decides how hard the litres the author already chose get pushed.

using UnityEngine;

namespace VoxelEngine.Pressure
{
    /// <summary>
    /// Volume-aware flow tuning for <see cref="GridAirVent"/>, <see cref="GridExhaustScrubber"/>
    /// and the hull gas vent. Static, allocation-free and safe to call every tick.
    /// </summary>
    public static class VentilationRules
    {
        /// <summary>Complete air changes per minute an automatic unit maintains at minimum,
        /// whatever its authored litres-per-second figure. Half an air change per minute means a
        /// room's whole volume turns over in two minutes, which is fast enough for a player to
        /// see a compartment come up to pressure without a hangar becoming a wind tunnel.</summary>
        public const float FloorAirChangesPerMinute = 0.5f;

        /// <summary>The fastest any automatic unit may turn a compartment over, in air changes
        /// per minute. Past this the room model stops meaning anything: the solve runs a few
        /// times a second and an instant exchange would read as the wall simply deleting air.</summary>
        public const float CeilingAirChangesPerMinute = 6f;

        /// <summary>The only supply figure that matters to the scaling decision: a unit with a
        /// feed line may scale up to the room's ceiling, a unit without one may not scale at all.
        /// It is deliberately not a rate — the room model already tolerates a refill that comes
        /// up short, so starving a promise would be a worse outcome than a slower one.</summary>
        public const float MinimumSupplyLitres = 0.0001f;

        /// <summary>Litres in a fully charged compartment per cubic metre of volume — the same
        /// constant the room itself uses, repeated here only so callers do not need a room.</summary>
        public static float LitresPerM3 => PressureRules.LitresPerCubicMetre;

        /// <summary>Total charge, in litres, of everything the block services right now.</summary>
        public static float CapacityLitres(GridRoom room)
            => room != null && room.IsSealed ? Mathf.Max(0f, room.CapacityLitres) : 0f;

        /// <summary>Total charge of several compartments: a full-block plant moves air for all
        /// of them, so its rating has to be judged against all of them.</summary>
        public static float CapacityLitres(System.Collections.Generic.IReadOnlyList<GridRoom> rooms)
        {
            if (rooms == null) return 0f;
            float total = 0f;
            for (int i = 0; i < rooms.Count; i++) total += CapacityLitres(rooms[i]);
            return total;
        }

        /// <summary>
        /// Litres per second the unit may move through a compartment of
        /// <paramref name="capacityLitres"/>. <paramref name="authored"/> is the block's own
        /// figure and always remains the ceiling when <paramref name="autoScale"/> is off; with
        /// scaling on it becomes the floor-to-ceiling band described above.
        /// </summary>
        public static float ResolveFlow(float authored, float capacityLitres, bool autoScale, float supplyLimit)
        {
            float baseFlow = Mathf.Max(0f, authored);
            if (!autoScale || capacityLitres <= 0.0001f) return baseFlow;

            // No feed line: the authored figure is the whole story. Scaling exists to keep up
            // with a big room, not to conjure air out of a wall.
            if (supplyLimit <= MinimumSupplyLitres) return baseFlow;

            // Floor: never slower than a perceivable air change, however big the room is.
            float floorFlow = capacityLitres * (FloorAirChangesPerMinute / 60f);
            // Ceiling: never so fast that the room model stops being able to follow. Note what
            // is NOT a ceiling here: the feed line. A room whose refill comes up short simply
            // charges slower and says so ("No Piped O2"); a promise narrowed to the pipe would
            // have turned the whole exercise into a no-op, which is the mistake the first cut
            // of this file made by clamping the target to seconds-of-authored-flow.
            float ceilingFlow = capacityLitres * (CeilingAirChangesPerMinute / 60f);

            return Mathf.Clamp(Mathf.Max(baseFlow, floorFlow), baseFlow, Mathf.Max(baseFlow, ceilingFlow));
        }

        /// <summary>
        /// Air changes per minute a given litres-per-second flow represents for a compartment of
        /// <paramref name="capacityLitres"/>. What the panels show, so the number on the plate and
        /// the number in the tooltip are the same number.
        /// </summary>
        public static float AirChangesPerMinute(float flowLitresPerSecond, float capacityLitres)
            => capacityLitres > 0.0001f ? flowLitresPerSecond * 60f / capacityLitres : 0f;

        /// <summary>
        /// Minutes for the compartment's whole charge to pass through the unit once. The
        /// human-scale version of the same figure: a hangar at 40 minutes is honest, and it is
        /// what a player needs to hear before they fit a second unit.
        /// </summary>
        public static float MinutesPerAirChange(float flowLitresPerSecond, float capacityLitres)
            => flowLitresPerSecond > 0.0001f ? capacityLitres / (flowLitresPerSecond * 60f) : float.PositiveInfinity;

        /// <summary>
        /// Turns "is there a feed line at all" into the presence value <see cref="ResolveFlow"/>
        /// asks for. A block with no line gets zero, and zero means: do not scale.
        /// </summary>
        public static float SupplyLimit(float authored, bool hasSupply)
            => hasSupply ? Mathf.Max(1f, authored) : 0f;
    }
}
