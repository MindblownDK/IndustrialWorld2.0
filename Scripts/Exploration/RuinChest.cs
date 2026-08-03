// Assets/Scripts/VoxelEngine/Exploration/RuinChest.cs
//
// Loot container found in ruins of dead civilizations.
// Visual: rusted, overgrown, damaged version of real player blocks.
// Behaviour: a normal slot-based chest. Walk inside, open it, and take what you
// want (shift-click / drag-drop). Loot is rolled into the slots the first time
// it is opened.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Exploration
{
    [RequireComponent(typeof(Collider))]
    public class RuinChest : MonoBehaviour
    {
        [Header("Ruin Chest")]
        public string ruinName = "Ruin";
        [Tooltip("Inventory slot count shown when the chest is opened.")]
        public int slots = 12;
        [Tooltip("True once the chest has been generated (loot is inside).")]
        public bool isLooted = false;

        [Header("Loot")]
        [Tooltip("Possible component items to roll into the chest.")]
        public ItemDefinition[] possibleComponents;
        [Tooltip("Fuel items.")]
        public ItemDefinition[] possibleFuel;
        [Tooltip("Blueprint cores that can be found here.")]
        public BlueprintDataCoreItem[] possibleBlueprints;

        [Header("Rare Find")]
        [Tooltip("Optional rare components, rolled independently after normal loot. Used for Pirate ruin relic parts.")]
        public ItemDefinition[] rareComponents;
        [Range(0f, 1f)] public float rareComponentChance = 0f;

        [Tooltip("Min/max components to roll.")]
        public int minComponents = 2;
        public int maxComponents = 5;

        /// <summary>Slot-based contents. Opened like any other container.</summary>
        public ItemContainer container;

        private bool _populated;

        private void Awake()
        {
            if (container == null)
                container = new ItemContainer(string.IsNullOrEmpty(ruinName) ? "Ruin Cache" : ruinName + " Cache", Mathf.Max(6, slots));
        }

        /// <summary>Called by PlayerInteractionTool RMB — opens the slot UI (rolls loot the first time).</summary>
        public void Open()
        {
            PopulateOnce();
            VoxelEngine.UI.GameUIController.Instance?.OpenContainer(container, null);
        }

        private void PopulateOnce()
        {
            if (_populated) return;
            _populated = true;
            if (container == null)
                container = new ItemContainer(string.IsNullOrEmpty(ruinName) ? "Ruin Cache" : ruinName + " Cache", Mathf.Max(6, slots));

            if (possibleComponents != null && possibleComponents.Length > 0)
            {
                int compCount = Random.Range(minComponents, maxComponents + 1);
                for (int i = 0; i < compCount; i++)
                {
                    var item = possibleComponents[Random.Range(0, possibleComponents.Length)];
                    if (item != null) container.Insert(new ItemStack(item, Random.Range(1, 4)));
                }
            }

            if (possibleFuel != null && possibleFuel.Length > 0 && Random.value < 0.6f)
            {
                var fuel = possibleFuel[Random.Range(0, possibleFuel.Length)];
                if (fuel != null) container.Insert(new ItemStack(fuel, Random.Range(1, 3)));
            }

            if (rareComponents != null && rareComponents.Length > 0 && Random.value < Mathf.Clamp01(rareComponentChance))
            {
                var rare = rareComponents[Random.Range(0, rareComponents.Length)];
                if (rare != null) container.Insert(new ItemStack(rare, 1));
            }

            if (possibleBlueprints != null && possibleBlueprints.Length > 0 && Random.value < 0.4f)
            {
                var bp = possibleBlueprints[Random.Range(0, possibleBlueprints.Length)];
                if (bp != null) container.Insert(new ItemStack(bp, 1));
            }

            isLooted = true;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.35f);
            Gizmos.DrawWireCube(transform.position, Vector3.one * 2f);
        }
    }
}
