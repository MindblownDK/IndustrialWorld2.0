// Assets/Scripts/VoxelEngine/Items/ScienceItem.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// A "science pack" item consumed by the Research Lab. Each tier represents how
    /// advanced the research it enables is. Tier 1 packs are craftable at the inventory,
    /// Tier 2 at the Crafting Bench, Tier 3 at the Assembler.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Science Pack", fileName = "Science_New")]
    public class ScienceItem : ResourceItem
    {
        [Range(1, 3)] public int tier = 1;
    }
}
