// Assets/Scripts/VoxelEngine/Building/Tiered/BuildToken.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// An item that, when held in the active hotbar slot, lets the player place
    /// a tiered building piece of `family`. Crafted via the player inventory or
    /// the Crafting Bench. Stackable — placing consumes the cost from inventory,
    /// not the token itself (so one Foundation token can place dozens as long as
    /// the player has the materials).
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Building/Build Token", fileName = "Token_New")]
    public class BuildToken : ItemDefinition
    {
        public BuildFamily family = BuildFamily.Foundation;
    }
}
