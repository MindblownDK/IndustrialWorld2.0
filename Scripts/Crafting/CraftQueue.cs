// Assets/Scripts/VoxelEngine/Crafting/CraftQueue.cs
//
// Per-CraftingStation queue of in-progress crafts. Recipes with craftSeconds > 0 will
// queue up; recipes with craftSeconds <= 0 still craft instantly (existing behaviour).
//
// Each tick, the head of the queue accumulates progress. On completion the output is
// inserted into the destination container (typically the player inventory).

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    [RequireComponent(typeof(CraftingStation))]
    public class CraftQueue : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public RecipeDefinition recipe;
            [NonSerialized] public IItemContainer destination;
            public float            progressSeconds;
        }

        // Runtime-only queue: destination is an interface and intentionally does not participate
        // in Unity serialization. Craft state is owned by the live station/session.
        [NonSerialized] public List<Entry> entries = new();
        public event Action OnChanged;

        public Entry Head => entries.Count > 0 ? entries[0] : null;
        public bool  HasWork => entries.Count > 0;

        private void Update()
        {
            if (entries.Count == 0) return;
            var head = entries[0];
            head.progressSeconds += Time.deltaTime;
            if (head.progressSeconds >= head.recipe.craftSeconds)
            {
                // Output the result; ingredients were already consumed when queued.
                if (head.recipe.outputItem != null)
                    head.destination?.Insert(new ItemStack(head.recipe.outputItem, head.recipe.outputCount));
                entries.RemoveAt(0);
                OnChanged?.Invoke();
            }
            else
            {
                // Only fire OnChanged occasionally so UI doesn't rebuild every frame.
                if (Time.frameCount % 10 == 0) OnChanged?.Invoke();
            }
        }

        /// <summary>Enqueue a new craft. Returns true if added.</summary>
        public bool Enqueue(RecipeDefinition recipe, IItemContainer destination)
        {
            if (recipe == null || destination == null) return false;
            entries.Add(new Entry { recipe = recipe, destination = destination, progressSeconds = 0f });
            OnChanged?.Invoke();
            return true;
        }

        /// <summary>Cancel a queued entry. Refunds the ingredients to the destination.</summary>
        public void Cancel(int index)
        {
            if (index < 0 || index >= entries.Count) return;
            var e = entries[index];
            // Refund ingredients (since they were paid up-front).
            foreach (var ing in e.recipe.inputs)
                if (ing.item != null && ing.count > 0)
                    e.destination.Insert(new ItemStack(ing.item, ing.count));
            entries.RemoveAt(index);
            OnChanged?.Invoke();
        }
    }
}
