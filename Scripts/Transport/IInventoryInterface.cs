// Assets/Scripts/VoxelEngine/Transport/IInventoryInterface.cs
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// Interface for any block/machine that can send or receive items through
    /// the pipe transport system.
    ///
    /// Implemented by Quarry (output only), and can be implemented by furnaces,
    /// assemblers, chests, etc. to allow pipes to push/pull items automatically.
    /// </summary>
    public interface IInventoryInterface
    {
        /// <summary>
        /// Returns the container used for item output (pipe pulls from here).
        /// Return null if this machine has no output.
        /// </summary>
        ItemContainer GetOutputContainer();

        /// <summary>
        /// Returns the container used for item input (pipe pushes into here).
        /// Return null if this machine does not accept input via pipes.
        /// </summary>
        ItemContainer GetInputContainer();

        /// <summary>
        /// Whether this machine currently has items available for pipe extraction.
        /// </summary>
        bool HasOutputReady { get; }

        /// <summary>
        /// Whether this machine can currently accept items via pipe insertion.
        /// </summary>
        bool CanAcceptInput { get; }
    }
}
