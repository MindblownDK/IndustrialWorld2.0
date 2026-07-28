// Assets/Scripts/VoxelEngine/Farming/WildCrop.cs
//
// A wild crop found in the world (spawned by biome scatter).
// The player can break it (LMB) to get seeds + some food items,
// then replant those seeds on a FarmPlot for proper farming.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    /// <summary>
    /// Wild crop component. Attach to scatter prefabs that represent
    /// wild wheat, berries, wild corn, etc. Hit with LMB to harvest
    /// and get seeds for planting.
    /// </summary>
    public class WildCrop : MonoBehaviour
    {
        [Header("Crop")]
        public CropDefinition crop;

        [Header("Harvest")]
        [Tooltip("Seeds the player gets when breaking this wild crop.")]
        public int seedYield = 2;
        [Tooltip("Food items the player gets.")]
        public int foodYield = 1;

        [Header("Health")]
        public int hp = 30;

        /// <summary>Hit by the player's tool. Returns true when destroyed.</summary>
        public bool Hit(int damage, Inventory inv)
        {
            hp -= damage;
            if (hp <= 0)
            {
                if (inv != null && crop != null)
                {
                    if (crop.seedItem != null)
                        inv.Add(crop.seedItem, seedYield);
                    if (crop.harvestItem != null)
                        inv.Add(crop.harvestItem, foodYield);

                    VoxelEngine.UI.BuildFeedbackHud.Show(
                        $"Wild {crop.cropName}",
                        $"+{seedYield} seeds, +{foodYield} {crop.harvestItem?.displayName ?? "food"}",
                        crop.icon,
                        new Color(0.50f, 0.80f, 0.30f));
                }
                Destroy(gameObject);
                return true;
            }
            return false;
        }
    }
}
