// Assets/Scripts/VoxelEngine/Transport/IItemPortHost.cs
using System.Collections.Generic;
using VoxelEngine.Items;

namespace VoxelEngine.Transport
{
    /// <summary>
    /// One named item container a machine exposes to the port system, plus the
    /// roles it can play. A furnace, for example, publishes:
    ///     Input  (canInput = true,  canOutput = false)
    ///     Fuel   (canInput = true,  canOutput = false)
    ///     Output (canInput = false, canOutput = true)
    /// A chest publishes a single "Storage" container that can do both.
    /// </summary>
    public readonly struct ItemPortContainer
    {
        public readonly string Name;
        public readonly ItemContainer Container;
        public readonly bool CanInput;   // pipes may PUSH items into this container
        public readonly bool CanOutput;  // pipes may PULL items out of this container

        public ItemPortContainer(string name, ItemContainer container, bool canInput, bool canOutput)
        {
            Name = name;
            Container = container;
            CanInput = canInput;
            CanOutput = canOutput;
        }
    }

    /// <summary>
    /// Any machine/block that exposes configurable item ports. The shared
    /// <see cref="ItemPortRouting"/> component and <see cref="VoxelEngine.UI.PortConfigHud"/>
    /// widget drive every host through this single interface, so adding ports to
    /// a new machine is just: implement this + add an ItemPortRouting component.
    /// </summary>
    /// <summary>
    /// How a host's faces may be configured by the player.
    /// <para><b>Free</b> — every face cycles None / Input / Output as usual.</para>
    /// <para><b>Provider</b> — an active face is always an Output: the block only ever feeds the network.</para>
    /// <para><b>Requester</b> — an active face is always an Input: the block only ever receives.</para>
    /// </summary>
    public enum PortLockMode { Free = 0, Provider = 1, Requester = 2 }

    /// <summary>
    /// Optional companion to <see cref="IItemPortHost"/>. A host that implements it declares
    /// that its faces are direction-locked, so the port UI offers only ON / OFF per face and
    /// the routing layer can never end up with a contradictory direction.
    /// </summary>
    public interface IPortLockedHost
    {
        /// <summary>The direction every active face is pinned to.</summary>
        PortLockMode PortLock { get; }

        /// <summary>Re-pin every face to <see cref="PortLock"/>. Safe to call repeatedly.</summary>
        void EnforcePortLock();
    }

    public interface IItemPortHost
    {
        /// <summary>The six-face direction/enable config (None / Input / Output).</summary>
        PortConfig PortConfig { get; }

        /// <summary>Every internal container the ports can route to/from.</summary>
        IReadOnlyList<ItemPortContainer> GetPortContainers();
    }
}
