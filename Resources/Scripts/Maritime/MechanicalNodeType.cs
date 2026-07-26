// Assets/Scripts/VoxelEngine/Maritime/MechanicalNodeType.cs
//
//  ╔══════════════════════════════════════════════════════════════════╗
//  ║   INDUSTRIALWORLD — MARITIME PROPULSION & MECHANICAL NETWORK      ║
//  ║   Part 1 — Core Burst propulsion engine (v2.18.0)                 ║
//  ╚══════════════════════════════════════════════════════════════════╝
//
//  Identifiers for every block that participates in the mechanical /
//  propulsion simulation. Kept as a `byte`-backed enum so the whole
//  MechanicalNode struct stays blittable and Burst-friendly.
//
//  Power flow model (see MechanicalPropagationJob):
//
//     Engine ──► Shaft ──► Gearbox ──► Shaft ──► Propeller  (→ thrust)
//                                   └─► Generator           (→ electricity)
//     Waterwheel ──► (stationary + flow = torque source)
//                └─► (on a powered ship = paddle thrust)
//     Turbocharger:  any Giant Diesel in the same chain → ×1.40 torque
//     Gearbox:       RPM × ratio, torque ÷ ratio, clamped to maxSpeed
//
//  All of this runs in Burst jobs over flat NativeArrays — zero GC,
//  zero per-block MonoBehaviour Update loops.

namespace VoxelEngine.Maritime
{
    /// <summary>
    /// What role a block plays in the mechanical network. Ordered roughly by
    /// the torque-flow direction (source → conduit → consumer) for readability.
    /// </summary>
    public enum MechanicalNodeType : byte
    {
        /// <summary>Uninitialised.</summary>
        None = 0,

        // ── Torque sources ───────────────────────────────────────────
        /// <summary>Small / Medium / Giant Diesel engine — burns fuel → torque.</summary>
        Engine = 1,
        /// <summary>Cast-iron waterwheel — generates torque from water flow,
        /// or produces paddle thrust when driven by a shaft.</summary>
        Waterwheel = 2,

        // ── Torque conduits / converters ─────────────────────────────
        /// <summary>Drive shaft — passes torque linearly. If severed, the chain dies.</summary>
        Shaft = 3,
        /// <summary>Gearbox — trades torque for RPM in all directions, speed clamped.</summary>
        Gearbox = 4,

        // ── Torque consumers → motion / power ────────────────────────
        /// <summary>Solid propeller — RPM × submergence × size → thrust.</summary>
        Propeller = 5,
        /// <summary>Torpedo pod propeller driven by electricity (fast spin-up).</summary>
        ElectricalPropeller = 6,
        /// <summary>Generator — spinning shaft → electricity for the grid.</summary>
        Generator = 7,

        // ── Auxiliaries ──────────────────────────────────────────────
        /// <summary>Boosts a Giant Diesel's torque by 40%.</summary>
        Turbocharger = 8,
        /// <summary>Exhaust vent — required for engines to breathe.</summary>
        ExhaustPipe = 9,

        // ── Buoyancy-only participants ───────────────────────────────
        /// <summary>Any non-mechanical block that still displaces water (hull).</summary>
        Hull = 10,
    }
}
