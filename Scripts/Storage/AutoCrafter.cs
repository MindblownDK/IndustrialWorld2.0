// Assets/Scripts/VoxelEngine/Storage/AutoCrafter.cs
//
// Auto-crafting engine for the storage network. Processes crafting patterns
// stored in RAM. Speed depends on CPU tier.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    [RequireComponent(typeof(ServerRack))]
    public class AutoCrafter : MonoBehaviour
    {
        [Header("Patterns")]
        public List<CraftingPattern> patterns = new();

        [Header("Queue")]
        public List<CraftJob> craftQueue = new();
        public int maxQueueSize = 8;

        private ServerRack _rack;
        private float _craftTimer;

        private void Awake() { _rack = GetComponent<ServerRack>(); }

        private void Update()
        {
            if (_rack == null || !_rack.IsOnline) return;
            if (craftQueue.Count == 0) return;

            _craftTimer += Time.deltaTime * _rack.CraftSpeedMultiplier;
            if (_craftTimer < craftQueue[0].timeRemaining) return;

            // Complete the first job.
            var job = craftQueue[0];
            _craftTimer = 0;

            // Output the result into storage.
            if (job.recipe != null && job.recipe.outputItem != null)
            {
                _rack.NetworkInsert(job.recipe.outputItem, job.recipe.outputCount * job.count);
            }

            craftQueue.RemoveAt(0);
        }

        /// <summary>Request auto-craft of an item. Returns true if queued.</summary>
        public bool RequestCraft(RecipeDefinition recipe, int count = 1)
        {
            if (_rack == null || !_rack.IsOnline) return false;
            if (craftQueue.Count >= maxQueueSize) return false;
            if (patterns.Count >= _rack.PatternSlots) return false;

            // Check if we have the ingredients in storage.
            if (recipe.inputs != null)
            {
                foreach (var ing in recipe.inputs)
                {
                    if (ing.item == null) continue;
                    int have = _rack.NetworkCount(ing.item.itemId);
                    if (have < ing.count * count) return false; // not enough
                }

                // Consume ingredients.
                foreach (var ing in recipe.inputs)
                {
                    if (ing.item == null) continue;
                    _rack.NetworkExtract(ing.item.itemId, ing.count * count);
                }
            }

            craftQueue.Add(new CraftJob
            {
                recipe = recipe,
                count = count,
                timeRemaining = recipe.craftSeconds > 0 ? recipe.craftSeconds : 1f
            });

            return true;
        }

        /// <summary>Add a crafting pattern (if RAM has space).</summary>
        public bool AddPattern(RecipeDefinition recipe)
        {
            if (patterns.Count >= _rack.PatternSlots) return false;
            patterns.Add(new CraftingPattern { recipe = recipe });
            return true;
        }
    }

    [System.Serializable]
    public class CraftJob
    {
        public RecipeDefinition recipe;
        public int count;
        public float timeRemaining;
    }
}
