// Assets/Scripts/VoxelEngine/Exploration/RuinBlockDrop.cs
//
// Mining a ruin block yields a small themed salvage resource (stone / iron / etc.)
// instead of a placeable block. This lets players harvest ruins for materials and
// demolish them when they're in the way of a base. Implements the same
// ICustomBlockDrop contract the PlacedBlock system already consults.

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Building;

namespace VoxelEngine.Exploration
{
    public class RuinBlockDrop : MonoBehaviour, ICustomBlockDrop
    {
        [Tooltip("Resource dropped when this ruin block is mined.")]
        public ItemDefinition salvage;

        [Tooltip("How many to drop per block.")]
        public int count = 1;

        public ItemStack CreateBlockDrop(BlockItem originalItem)
            => salvage != null ? new ItemStack(salvage, Mathf.Max(1, count)) : null;
    }
}
