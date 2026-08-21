// Assets/Scripts/VoxelEngine/Items/ItemDefinition.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// Base item asset. Use one of the subclasses (ResourceItem, ToolItem, BlockItem)
    /// for specialised behaviour, or this class directly for plain stackables.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Item Definition", fileName = "Item_New")]
    public class ItemDefinition : ScriptableObject
    {
        public string itemId = "iron_ore";
        public string displayName = "Iron Ore";
        [TextArea] public string description;
        public Sprite icon;
        [Tooltip("Color used for icon fallback when no sprite is assigned.")]
        public Color iconTint = Color.white;
        public int   maxStack    = 900;
        public float massPerUnit = 1f;
        [Tooltip("Free-form category for filtering in the crafting list. Examples: Resources, Tools, Building, Power, Stations.")]
        public string category    = "Misc";

        /// <summary>
        /// True for exotic matter that MUST live in containment-grade storage on grids:
        /// plain cargo containers refuse it (ItemContainer.Allowed gate); the Containment
        /// Vault, machines and the player inventory accept it.
        /// </summary>
        public bool requiresContainment = false;

        /// <summary>
        /// True for raw exotic matter that may NEVER be carried directly — it only
        /// travels inside pressurized canisters. The player inventory refuses it
        /// (ItemContainer.allowPlayerCarry gate).
        /// </summary>
        public bool cannotBeCarried = false;

        /// <summary>
        /// True for pressurized canisters: they hold their exotic payload under a
        /// decaying field. In the player inventory the pressure bleeds down; at zero
        /// the canister collapses — and kills whoever carries it.
        /// </summary>
        public bool isPressurizedCanister = false;

        [Header("Visuals")]
        [Tooltip("Optional 3D prefab to show when this item is held in hand (viewmodel).")]
        public GameObject viewmodelPrefab;

        /// <summary>Tools and blocks override this to be unique-per-instance (no stacking).</summary>
        public virtual bool IsStackable => maxStack > 1;
    }
}
