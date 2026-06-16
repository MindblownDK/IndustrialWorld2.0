// Assets/Scripts/VoxelEngine/Maritime/GridTurbocharger.cs
//
// Turbocharger — boosts any adjacent Giant Diesel Engine's torque by 40%.
//
// The actual ×1.40 multiplication happens inside MechanicalPropagationJob
// (via the TurboBoosted flag). This block's only job is to EXIST next to a
// Giant Diesel — the graph rebuild in MaritimePropulsionSystem detects the
// adjacency and sets the flag. No per-frame work.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridTurbocharger : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.Turbocharger;

        [Header("Turbocharger")]
        [Tooltip("Torque multiplier applied to the boosted Giant Diesel.")]
        public float boostMultiplier = MechanicalNode.TurboBoost; // 1.40

        /// <summary>True when adjacent to at least one Giant Diesel (for VFX / UI).</summary>
        public bool IsConnected { get; private set; }

        /// <summary>Boost pressure (bar) — derived from connected engine RPM.</summary>
        public float BoostPressure { get; private set; }
        /// <summary>Turbo rotations (RPM) — visualized as a spinning compressor.</summary>
        public float TurboRPM { get; private set; }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Turbocharger";
        }

        public override void RefreshMaritimeNode(ref MechanicalNode node, float throttle)
        {
            // Detect adjacency for the visual "glowing core" state.
            IsConnected = HasAdjacentEngine();

            // Estimate boost pressure + turbo RPM from any connected engine's state.
            if (IsConnected && Grid != null)
            {
                float maxEngineRPM = 0f;
                var faces = new[]
                {
                    new Vector3Int( 1,0,0), new(-1,0,0),
                    new( 0,1,0), new( 0,-1,0),
                    new( 0,0,1), new( 0,0,-1),
                };
                foreach (var off in faces)
                {
                    var nb = Grid.GetBlock(GridPos + off);
                    if (nb is GridMaritimeEngine eng && eng.tier == EngineTier.Giant && eng.IsRunning)
                        maxEngineRPM = Mathf.Max(maxEngineRPM, eng.CurrentRPM);
                }
                // Turbo spins much faster than the engine (typical ratio ~20:1).
                TurboRPM = maxEngineRPM * 20f * boostMultiplier;
                // Boost pressure scales with engine RPM (1-4 bar typical for marine turbos).
                BoostPressure = Mathf.Lerp(0f, 3.5f, Mathf.Clamp01(maxEngineRPM / 1200f)) * boostMultiplier;
            }
            else
            {
                TurboRPM = 0f;
                BoostPressure = 0f;
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
                if (nb is GridMaritimeEngine eng && eng.tier == EngineTier.Giant)
                    return true;
            }
            return false;
        }
    }
}
