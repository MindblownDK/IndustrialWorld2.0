// Assets/Scripts/VoxelEngine/Building/Tiered/BuildToken.cs
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Legacy item that, when held in the active hotbar slot, selects one
    /// building family. New placement uses the hammer wheel. Tokens are not
    /// crafted. Placing still spends the piece cost from inventory, not the token.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Building/Build Token", fileName = "Token_New")]
    public class BuildToken : ItemDefinition
    {
        public BuildFamily family = BuildFamily.Foundation;
    }
}
