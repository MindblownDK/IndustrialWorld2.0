// Assets/Scripts/VoxelEngine/Maritime/GridExhaustPipe.cs
//
// Exhaust Pipe — venting / cooling. Every engine MUST have at least one
// adjacent exhaust pipe or it chokes (zero torque). The pipe itself does
// nothing mechanically — its mere presence satisfies the engine's check.
//
// Aesthetically it should vent smoke; that VFX is added via the setup wizard
// prefab (Part 5). Here it's a pure passive block.

using UnityEngine;

namespace VoxelEngine.Maritime
{
    public class GridExhaustPipe : MaritimeBlockBase
    {
        public override MechanicalNodeType NodeType => MechanicalNodeType.ExhaustPipe;

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrEmpty(blockName) || blockName == "Armor Block")
                blockName = "Exhaust Pipe";
        }

        // No mechanical behaviour — PopulateMaritimeNode / RefreshMaritimeNode
        // use the defaults (all-zero, which is correct for a passive block).
    }
}
