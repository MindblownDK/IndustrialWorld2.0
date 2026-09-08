// Assets/Scripts/VoxelEngine/Thermal/IHeatSourceBlock.cs
//
// Opt-in contract for any grid block that generates heat while it works: hydrogen
// engines, maritime diesels and generators, reactors, furnaces, exhaust stacks.
// GridThermalSystem asks every source once per thermal tick and conducts the
// answer into the block itself and its face neighbours; the block never touches
// the thermal tables directly, so machines stay free of thermal bookkeeping.
//
// Roadmap 5.1 item 8: "Engines, thrusters, reactors, and exhaust pipes generate
// heat. Maritime engines produce significant heat."

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
}
