// Assets/Scripts/VoxelEngine/Maritime/GridTurbocharger.cs
//
//  Turbocharger — boosts adjacent engines. Two tiers:
//
//    Small (1×1×1) — boosts Small/Heavy-Fuel engines. Can fit 1 on a Crude Engine,
//                    2 on a Heavy Fuel Oil Engine.
//    Large (2×2×2) — boosts MGO/Giant engines. Can fit 4 on an MGO Engine,
//                    2 on a Heavy Fuel Oil Engine.
//
//  The actual ×boost multiplication happens inside MechanicalPropagationJob.
//  This block's job is to EXIST next to an engine — the graph rebuild detects
//  adjacency and sets the TurboBoosted flag. Each turbo stacks additively.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    /// <summary>Turbocharger size tier.</summary>
    public enum TurboTier : byte
    {
        /// <summary>1×1×1 — for small/medium engines. Boost ×1.15 each.</summary>
        Small = 0,
        /// <summary>2×2×2 — for MGO/Giant engines. Boost ×1.25 each.</summary>
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
            IsConnected = HasAdjacentEngine();

            if (IsConnected && Grid != null)
            {
                float maxEngineRPM = 0f;
                ConnectedTurboCount = 0;
                var faces = new[]
                {
                    new Vector3Int( 1,0,0), new(-1,0,0),
                    new( 0,1,0), new( 0,-1,0),
                    new( 0,0,1), new( 0,0,-1),
                };
                foreach (var off in faces)
                {
                    var nb = Grid.GetBlock(GridPos + off);
                    if (nb is GridMaritimeEngine eng && eng.IsRunning)
                    {
                        maxEngineRPM = Mathf.Max(maxEngineRPM, eng.CurrentRPM);
                        ConnectedTurboCount++;
                    }
                }
                // Turbo spins much faster than the engine (typical ratio ~20:1).
                TurboRPM = maxEngineRPM * 20f * EffectiveBoost;
                // Boost pressure scales with engine RPM.
                BoostPressure = Mathf.Lerp(0f, tier == TurboTier.Large ? 5.5f : 3.5f,
                    Mathf.Clamp01(maxEngineRPM / 1200f)) * EffectiveBoost;
            }
            else
            {
                TurboRPM = 0f;
                BoostPressure = 0f;
                ConnectedTurboCount = 0;
            }
        }

        private bool HasAdjacentEngine()
        {
            if (Grid == null) return false;
            var faces = new[]
            {
                new Vector3Int( 1,0,0), new(-1,0,0),
                new( 0,1,0), new( 0,-1,0),
                new( 0,0,1), new( 0,0,-1),
            };
            foreach (var off in faces)
            {
                var nb = Grid.GetBlock(GridPos + off);
                if (nb is GridMaritimeEngine) return true;
            }
            return false;
        }
    }
}
