// Assets/Scripts/VoxelEngine/Maritime/IMechanicalBlock.cs
//
// Contract implemented by every grid block that participates in the maritime
// mechanical network (Engine, Shaft, Gearbox, Propeller, Waterwheel, Generator,
// Turbocharger, Electrical Propeller …). Defined in Part 1 so the core engine
// can drive Part-2 blocks without knowing their concrete types.
//
// The split into Populate (static) + Refresh (dynamic) keeps the expensive
// network-graph rebuild rare while still letting fuel/throttle/breakage change
// every frame.

using System;

namespace VoxelEngine.Maritime
{
    /// <summary>
    /// Implemented by grid blocks that take part in torque propagation and/or
    /// water interaction. The <see cref="MaritimePropulsionSystem"/> queries the
    /// grid for these and feeds them into the Burst job pipeline.
    /// </summary>
    public interface IMechanicalBlock
    {
        /// <summary>The role this block plays in the mechanical network.</summary>
        MechanicalNodeType NodeType { get; }

        /// <summary>
        /// Fill the STATIC, type-specific fields of a MechanicalNode (MaxTorque,
        /// MaxRPM, GearRatio, PropellerSize, ThrustCoefficient, Giant/Stationary
        /// flags …). Geometry (position, mass, volume) and Id/ChainIndex are
        /// already set by the system before this is called.
        /// Called once whenever the propulsion graph is rebuilt.
        /// </summary>
        void PopulateMaritimeNode(ref MechanicalNode node);

        /// <summary>
        /// Update the DYNAMIC fields that can change between rebuilds — most
        /// importantly <see cref="MechanicalNode.FuelAvailable01"/> (derived from
        /// the block's fuel state and the pilot throttle) and the Broken flag.
        /// Called every FixedUpdate, just before the propagation job runs.
        /// </summary>
        /// <param name="throttle">Pilot throttle 0..1 from the Helm/Cockpit.</param>
        void RefreshMaritimeNode(ref MechanicalNode node, float throttle);

        /// <summary>
        /// Read back the COMPUTED results from the job pipeline (RPM, electricity
        /// output, submergence, force …) and cache them on the live block so other
        /// systems (grid power, audio, UI) can query them. Called every FixedUpdate
        /// AFTER both jobs have completed.
        /// </summary>
        void ApplyResults(in MechanicalNode node);
    }
}
