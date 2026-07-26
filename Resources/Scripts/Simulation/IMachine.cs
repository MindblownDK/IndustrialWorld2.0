// Assets/Scripts/VoxelEngine/Simulation/IMachine.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — SIMULATION CONTRACT: MACHINE               ║
// ║  Every processing block (furnace, crusher, assembler, chemical  ║
// ║  plant) implements this so the SimulationTickManager can drive  ║
// ║  them on a unified fixed-tick loop without Update() spam.       ║
// ╚══════════════════════════════════════════════════════════════════╝

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Core contract for any block that processes items, fluids, or energy.
    /// The <see cref="SimulationTickManager"/> calls <see cref="SimulationTick"/>
    /// on a fixed interval so machines avoid per-frame Update() overhead.
    /// </summary>
    public interface IMachine
    {
        /// <summary>Display name shown in the machine panel header.</summary>
        string MachineName { get; }

        /// <summary>True when the machine is actively processing a recipe.</summary>
        bool IsActive { get; }

        /// <summary>True when the machine has power and is not user-disabled.</summary>
        bool IsOnline { get; }

        /// <summary>
        /// 0-1 progress of the current operation. 0 when idle.
        /// Drives the progress bar in the shared MachinePanel UI.
        /// </summary>
        float Progress01 { get; }

        /// <summary>Current power draw in watts. 0 when idle or disabled.</summary>
        float CurrentWattage { get; }

        /// <summary>
        /// Called by SimulationTickManager on a fixed interval.
        /// The machine should advance its internal state, consume inputs,
        /// produce outputs, and update power draw.
        /// </summary>
        /// <param name="dt">Tick interval in seconds.</param>
        void SimulationTick(float dt);

        /// <summary>
        /// Player-controlled hard-disable toggle. When false, the machine
        /// stops drawing power and pauses all processing.
        /// </summary>
        bool UserEnabled { get; set; }
    }
}
