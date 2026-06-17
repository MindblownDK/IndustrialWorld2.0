// Assets/Scripts/VoxelEngine/Maritime/GridTurbocharger.cs
//
//  Turbocharger — boosts adjacent engines. Two tiers:
//
//    Small (1×1×1) — fits named turbo attachment points. Can fit 1 on a Crude Engine,
//                    2 on a Heavy Fuel Oil Engine and 4 on an MGO Engine.
//    Large (2×2×2) — fits HFO/MGO attachment points. Can fit 4 on an MGO Engine,
//                    2 on a Heavy Fuel Oil Engine.
//
//  Boost is granted only when the turbo occupies a named engine attachment slot.
//  Free-grid adjacency does not count, so turbos must be deliberately mounted.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    /// <summary>Turbocharger size tier.</summary>
    public enum TurboTier : byte
    {
        /// <summary>1×1×1 — fits any engine attachment point. Boost ×1.15 each.</summary>
        Small = 0,
        /// <summary>2×2×2 — for HFO/MGO attachment points. Boost ×1.25 each.</summary>
        Large = 1,
    }

    public class GridTurbocharger : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Turbocharger;

        [Header("Turbocharger")]
        public TurboTier tier = TurboTier.Small;

        [Tooltip("Torque multiplier applied per turbo (stacks additively with other turbos).")]
        public float boostPerUnit = 0.15f; // 15% per small, 25% per large

        /// <summary>True when adjacent to at least one compatible engine (for VFX / UI).</summary>
        public bool IsConnected { get; private set; }

        /// <summary>Number of turbos connected to the SAME engine (for stacking).</summary>
        public int ConnectedTurboCount { get; private set; }

        /// <summary>Boost pressure (bar) — derived from connected engine RPM.</summary>
        public float BoostPressure { get; private set; }
        /// <summary>Turbo rotations (RPM) — visualized as a spinning compressor.</summary>
        public float TurboRPM { get; private set; }

        /// <summary>Total boost multiplier this turbo contributes.</summary>
        public float EffectiveBoost
        {
            get
            {
                float per = tier == TurboTier.Large ? 0.25f : 0.15f;
                return 1f + per;
            }
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            boostPerUnit = tier == TurboTier.Large ? 0.25f : 0.15f;
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = tier == TurboTier.Large ? "Large Turbocharger" : "Small Turbocharger";
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            GridMaritimeEngine attachedEngine = FindAttachedEngine();
            IsConnected = attachedEngine != null;

            if (IsConnected)
            {
                ConnectedTurboCount = attachedEngine.ConnectedTurboCount;
                float engineRPM = attachedEngine.IsRunning ? attachedEngine.CurrentRPM : 0f;

                // Turbo spins much faster than the engine (typical ratio ~20:1).
                TurboRPM = engineRPM * 20f * EffectiveBoost;
                // Boost pressure scales with engine RPM.
                BoostPressure = Mathf.Lerp(0f, tier == TurboTier.Large ? 5.5f : 3.5f,
                    Mathf.Clamp01(engineRPM / 1200f)) * EffectiveBoost;
            }
            else
            {
                TurboRPM = 0f;
                BoostPressure = 0f;
                ConnectedTurboCount = 0;
            }
        }

        /// <summary>Find the engine whose named turbo attachment point this block occupies.</summary>
        private GridMaritimeEngine FindAttachedEngine()
        {
            if (Grid == null) return null;

            var faces = new[]
            {
                new Vector3Int( 1,0,0), new(-1,0,0),
                new( 0,1,0), new( 0,-1,0),
                new( 0,0,1), new( 0,0,-1),
            };

            foreach (var off in faces)
            {
                if (Grid.GetBlock(GridPos + off) is GridMaritimeEngine eng && eng.CanAttachTurboAt(GridPos, tier))
                    return eng;
            }

            return null;
        }
    }
}
