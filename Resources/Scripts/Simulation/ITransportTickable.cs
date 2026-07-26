// Assets/Scripts/VoxelEngine/Simulation/ITransportTickable.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — SIMULATION CONTRACT: TRANSPORT TICKABLE     ║
// ║  Lightweight contract for transport blocks (conveyors, chutes,  ║
// ║  funnels) that need a fixed-interval simulation tick without    ║
// ║  the full IMachine metadata (recipe, wattage, progress bar).    ║
// ╚══════════════════════════════════════════════════════════════════╝

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Lightweight simulation-tick contract for transport blocks.
    /// Unlike <see cref="IMachine"/>, this interface does not require
    /// recipe, wattage, progress, or user-enabled metadata — only a
    /// fixed-interval tick callback so transport blocks avoid per-frame
    /// Update() overhead.
    /// </summary>
    public interface ITransportTickable
    {
        /// <summary>
        /// Called by SimulationTickManager on a fixed interval.
        /// The transport block should advance item movement, scan for
        /// connections, pull from upstream, and hand off to downstream.
        /// </summary>
        /// <param name="dt">Tick interval in seconds.</param>
        void TransportTick(float dt);
    }
}
