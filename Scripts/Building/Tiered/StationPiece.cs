// Assets/Scripts/VoxelEngine/Building/Tiered/StationPiece.cs
//
// Marks a placed hammer piece as part of an orbital station.
//
// WHY THIS EXISTS SEPARATELY FROM PlacedTieredBlock
// Every hammer piece is a `PlacedTieredBlock`. This component says the extra things that
// are only true of station pieces: that it is meant to hold pressure, and which family
// it came from. Keeping it separate means an ordinary wooden wall carries none of this
// and the everyday building path is completely unchanged.
//
// A NOTE ON PRESSURE, HONESTLY SCOPED
// The roadmap says airtight station pieces integrate with room pressure and oxygen. The
// pressure simulation in this codebase (`PressureRules`, `GridRoom`) operates on
// `GridBlock` - it is a SHIP system, and it has no concept of world-placed blocks at
// all. Wiring hammer pieces into it needs a world-side room solver that does not exist
// yet, and inventing half of one here would produce rooms that look sealed and behave
// like open vacuum, which is worse than not claiming the feature.
//
// So this component records the sealing intent and exposes it through `SealsPressure`,
// ready for that solver. What ships today is the buildable family, its research gate and
// its geometry. The roadmap entry is marked partial accordingly rather than ticked.

using UnityEngine;

namespace VoxelEngine.Building.Tiered
{
    [DisallowMultipleComponent]
    public class StationPiece : MonoBehaviour
    {
        [Tooltip("Which orbital station family this piece was placed from.")]
        public BuildFamily family = BuildFamily.StationHull;

        [Tooltip("Whether this piece is intended to hold atmosphere. Read by the world " +
                 "room solver once one exists; a dock collar is deliberately open.")]
        public bool sealsPressure = true;

        /// <summary>True when this piece should count as an airtight wall.</summary>
        public bool SealsPressure => sealsPressure;

        /// <summary>Registry of every placed station piece, for a future room solver.</summary>
        private static readonly System.Collections.Generic.List<StationPiece> s_all = new();
        public static System.Collections.Generic.IReadOnlyList<StationPiece> All => s_all;

        private void OnEnable()
        {
            if (!s_all.Contains(this)) s_all.Add(this);
            // Any change to the set of station pieces can open or close a compartment,
            // so both ends of the lifecycle re-solve. The solver coalesces these, so a
            // burst of placements still costs one fill.
            VoxelEngine.Pressure.StationRoomSolver.MarkDirty();
        }

        private void OnDisable()
        {
            s_all.Remove(this);
            VoxelEngine.Pressure.StationRoomSolver.MarkDirty();
        }

        /// <summary>
        /// Applies the family's authored sealing rule. Called at placement so a piece is
        /// correct immediately rather than depending on a prefab field being right.
        /// </summary>
        public void Configure(BuildFamily placedFamily)
        {
            family = placedFamily;
            sealsPressure = BuildFamilyInfo.SealsPressure(placedFamily);
        }
    }
}
