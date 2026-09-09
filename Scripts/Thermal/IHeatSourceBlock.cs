// Assets/Scripts/VoxelEngine/Thermal/IHeatSourceBlock.cs
//
// Opt-in contract for any grid block that generates heat while it works: hydrogen
// engines, maritime diesels and generators, reactors, furnaces, exhaust stacks.
// GridThermalSystem asks every source once per thermal tick and conducts the
// answer into the block itself and its face neighbours; the block never touches
// the thermal tables directly, so machines stay free of thermal bookkeeping.
//
// Roadmap 5.1 item 8: "Engines, thrusters, reactors, and exhaust pipes generate
// heat. Maritime engines produce significant heat." Since 9.32.0 the same sources
// also feed the CONCEALED SPACE around them through ThermalService.ReportWasteHeat,
// which is what makes an unventilated engine room a real hazard (item 14).

using UnityEngine;

namespace VoxelEngine.Thermal
{
    public interface IHeatSourceBlock
    {
        /// <summary>
        /// Steady-state temperature (degrees C above ambient) this block drives ITSELF
        /// toward while working. Return 0 when idle. Values are peak surface heat, not
        /// internal process heat: a furnace core is far hotter than its casing.
        /// </summary>
        float SelfHeatC { get; }

        /// <summary>
        /// Temperature (degrees C above ambient) conducted into each block in a
        /// directly adjacent cell. Typically a third to a half of <see cref="SelfHeatC"/>.
        /// </summary>
        float NeighbourHeatC { get; }
    }

    /// <summary>
    /// Optional extension for sources that also emit a directed hot gas stream
    /// (exhaust stacks). The stream is modelled with the same plume cone as a thruster
    /// nozzle, so it heats whatever it blows on: hull plates above a funnel, a parked
    /// ship, the player on deck.
    /// </summary>
    public interface IExhaustPlumeSource : IHeatSourceBlock
    {
        /// <summary>0..1 how hard the stack is venting right now (0 = no plume).</summary>
        float PlumeLoad01 { get; }

        /// <summary>World-space exit point of the gas stream.</summary>
        UnityEngine.Vector3 PlumeOrigin { get; }

        /// <summary>World-space direction the gas travels.</summary>
        UnityEngine.Vector3 PlumeDirection { get; }

        /// <summary>Plume temperature relative to a thruster core at the same load (thruster = 1).</summary>
        float PlumeScale { get; }
    }

    // ══════════════════════════════════════════════════════════════════
    //  COMBUSTION AIR — one rule, three sources (9.32.0)
    //
    //  A diesel can take its oxidiser from exactly one place at a time, and the
    //  choice is mechanical, not a menu:
    //
    //    1. PIPED O₂     the instant a gas line is plumbed to the engine's oxygen
    //                    port, that line owns the intake completely. The engine
    //                    never sips the room or the sky to make up for a pipe that
    //                    has run dry — a starved supply line is a fault to fix,
    //                    not a licence to eat the engine room's air.
    //    2. ROOM AIR     no pipe, and the compartment around the engine still has
    //                    burnable air: breathe it, and pay for it in pressure,
    //                    foul gas and heat.
    //    3. ATMOSPHERE   no pipe, no usable room, and the engine is not walled in:
    //                    the open cell on the engine's intake side IS the intake.
    //                    Leave a hole in the hull and the engine runs on the
    //                    planet; wall it up and it does not.
    //
    //  Roadmap 5.1 item 14 with 9.32.0's concealed-space model: an enclosed volume
    //  is a resource with a cost, and the cheapest way out of it is an opening.
    // ══════════════════════════════════════════════════════════════════

    /// <summary>Where a combustion machine is taking its oxidiser from right now.</summary>
    public enum AirSource : byte
    {
        None       = 0,
        PipedOxygen = 1,
        RoomAir    = 2,
        Atmosphere = 3,
        ClosedCycle = 4,
    }

    /// <summary>The single resolved air situation for one machine, one tick.</summary>
    public readonly struct CombustionAir
    {
        public readonly AirSource Source;
        /// <summary>The sealed volume the machine stands in, when there is one.</summary>
        public readonly VoxelEngine.Pressure.GridRoom Room;
        /// <summary>True when the compartment still has burnable air.</summary>
        public readonly bool RoomHasAir;
        /// <summary>True when the intake side of the machine opens on breathable planet air.</summary>
        public readonly bool AtmosphereHasAir;
        /// <summary>True when a gas line is plumbed to the engine's oxygen port at all.</summary>
        public readonly bool PipeConnected;
        /// <summary>0..1 output multiplier: piped air is perfect, other sources pay.</summary>
        public readonly float Quality01;
        /// <summary>True when a plumbed line is connected and simply has nothing to give, with
        /// the fallback switch off: the engine is not short of air in general, it is short of
        /// the ONE supply it was wired to. The panel words that differently.</summary>
        public readonly bool StarvedOnLine;

        public CombustionAir(AirSource source, VoxelEngine.Pressure.GridRoom room, bool roomHasAir,
            bool atmosphereHasAir, bool pipeConnected, float quality01, bool starvedOnLine = false)
        {
            Source = source; Room = room; RoomHasAir = roomHasAir;
            AtmosphereHasAir = atmosphereHasAir; PipeConnected = pipeConnected;
            Quality01 = quality01; StarvedOnLine = starvedOnLine;
        }

        public static CombustionAir Fail()
            => new(AirSource.None, null, false, false, false, 0f);
        public static CombustionAir Closed()
            => new(AirSource.ClosedCycle, null, true, true, false, 1f);

        public string Label => StarvedOnLine ? "PIPED LINE EMPTY" : Source switch
        {
            AirSource.PipedOxygen  => "PIPED O₂",
            AirSource.RoomAir      => "ENGINE ROOM AIR",
            AirSource.Atmosphere   => "ATMOSPHERE INTAKE",
            AirSource.ClosedCycle  => "CLOSED CYCLE",
            _ => "NO AIR",
        };
    }

    public static class CombustionAirRules
    {
        /// <summary>Room air burns fine, but it is lean and it costs the compartment: -10% output.</summary>
        public const float RoomAirQuality01 = 0.90f;
        /// <summary>An atmosphere intake needs the open intake side and thin air costs power: -25% at worst.</summary>
        public const float AtmosphereQualityFloor01 = 0.75f;

        /// <summary>
        /// Which cell counts as "the hole for air". The engine's intake side is its
        /// forward-facing cell: an opening anywhere else is a hole in the room, not in
        /// the intake, so the player has to leave the RIGHT gap.
        /// </summary>
        public static UnityEngine.Vector3Int IntakeCellOf(UnityEngine.Transform root)
        {
            if (root == null) return UnityEngine.Vector3Int.zero;
            var f = root.forward;
            int x = Mathf.RoundToInt(f.x), y = Mathf.RoundToInt(f.y), z = Mathf.RoundToInt(f.z);
            if (x == 0 && y == 0 && z == 0)
            {
                // Diagonal facing: pick the dominant axis so the rule never becomes "any side".
                float ax = Mathf.Abs(f.x), ay = Mathf.Abs(f.y), az = Mathf.Abs(f.z);
                if (ax >= ay && ax >= az) x = (int)UnityEngine.Mathf.Sign(f.x);
                else if (ay >= az) y = (int)UnityEngine.Mathf.Sign(f.y);
                else z = (int)UnityEngine.Mathf.Sign(f.z);
            }
            return new UnityEngine.Vector3Int(x, y, z);
        }

        /// <summary>
        /// Resolves the one and only air source for a machine. <paramref name="pipeConnected"/>
        /// is "a gas line reaches this block's oxygen port", <paramref name="pipeCanFeed"/> is
        /// "that line can actually deliver right now" — the difference is what makes a starved
        /// supply line a fault instead of a free pass to the room's air.
        /// <paramref name="allowFallback"/> is the player's own switch on that behaviour: with
        /// it on, an engine whose line has run dry goes back to the room or the sky; with it
        /// off the line owns the intake absolutely and the engine stalls when it is empty.
        /// </summary>
        public static CombustionAir Resolve(VoxelEngine.GridSystem.GridBlock block,
            bool airIndependent, bool pipeConnected, bool pipeCanFeed, bool bufferHasOxygen,
            bool allowFallback = true)
        {
            var room = ThermalService.ConcealedSpaceOf(block);
            bool roomSealed = room != null && room.IsSealed;
            bool roomAir = roomSealed
                && VoxelEngine.Pressure.PressureRules.SupportsCombustion(room);

            // Atmosphere: the intake side must open on a cell that is neither blocked by
            // structure nor inside somebody's pressure hull, and the planet must have air.
            bool atmosphere = false;
            float density01 = 0f;
            if (block != null && block.Grid != null)
            {
                var cell = block.GridPos + IntakeCellOf(block.transform);
                bool blocked = block.Grid.Blocks.ContainsKey(cell);
                var pressure = block.Grid.GetComponent<VoxelEngine.Pressure.GridPressureSystem>();
                bool inRoom = pressure != null && pressure.RoomAtCell(cell) != null;
                if (!blocked && !inRoom)
                {
                    var air = VoxelEngine.Pressure.PressureRules.SampleAmbient(block.transform.position);
                    atmosphere = air.IsOxygenBearing && air.PressureAtm > 0.05f;
                    density01 = UnityEngine.Mathf.Clamp01(air.PressureAtm);
                }
            }

            if (airIndependent) return CombustionAir.Closed();

            // 1 — a plumbed line owns the intake, working or not. A line that is plumbed
            // but empty is a fault to fix, so by default the engine holds the player to the
            // arrangement they built; with the fallback switch on it is instead allowed to go
            // back to the room or the sky at that source's own cost.
            if (pipeConnected)
            {
                bool fed = pipeCanFeed || bufferHasOxygen;
                if (fed) return new CombustionAir(AirSource.PipedOxygen, room, roomAir, atmosphere, true, 1f);
                if (!allowFallback) return new CombustionAir(AirSource.None, room, roomAir, atmosphere, true, 0f,
                    starvedOnLine: true);
                return FallbackAir(room, roomSealed, roomAir, atmosphere, density01);
            }

            // 2 — the compartment's own air.
            if (roomSealed)
                return new CombustionAir(roomAir ? AirSource.RoomAir : AirSource.None,
                    room, roomAir, atmosphere, false,
                    roomAir ? RoomAirQuality01 : 0f);

            // 3 — the open intake side.
            if (atmosphere)
            {
                float quality = UnityEngine.Mathf.Lerp(AtmosphereQualityFloor01, 1f, density01);
                return new CombustionAir(AirSource.Atmosphere, room, roomAir, true, false, quality);
            }

            return CombustionAir.Fail();
        }

        /// <summary>The intake a machine gets with no piped supply at all: the compartment, then
        /// the open intake side, then nothing. Shared with the starved-line fallback so both
        /// paths agree on what "unplumbed" costs.</summary>
        private static CombustionAir FallbackAir(VoxelEngine.Pressure.GridRoom room, bool roomSealed,
            bool roomAir, bool atmosphere, float density01)
        {
            if (roomSealed)
                return new CombustionAir(roomAir ? AirSource.RoomAir : AirSource.None,
                    room, roomAir, atmosphere, false,
                    roomAir ? RoomAirQuality01 : 0f);

            if (atmosphere)
            {
                float quality = UnityEngine.Mathf.Lerp(AtmosphereQualityFloor01, 1f, density01);
                return new CombustionAir(AirSource.Atmosphere, room, roomAir, true, false, quality);
            }

            return CombustionAir.Fail();
        }
    }

}