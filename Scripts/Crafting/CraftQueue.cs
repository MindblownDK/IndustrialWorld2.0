// Assets/Scripts/VoxelEngine/Crafting/CraftQueue.cs
//
// Per-station or per-player-inventory queue of in-progress crafts. Recipes with
// craftSeconds > 0 queue up; recipes with craftSeconds <= 0 still craft instantly.
//
// Each tick, the head of the queue accumulates progress. On completion the output is
// inserted into the destination container (typically the player inventory). If the
// destination is temporarily full, the completed output stays queued until it fits.

using System;
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    public class CraftQueue : MonoBehaviour
    {
        private const float CapacityRetrySeconds = 0.5f;

        [Serializable]
        public class Entry
        {
            public RecipeDefinition recipe;
            [NonSerialized] public IItemContainer destination;
            public float progressSeconds;
            public bool completionReached;
            public bool refundPending;
            [NonSerialized] public ItemStack pendingOutput;
            [NonSerialized] public List<ItemStack> pendingRefunds;
            [NonSerialized] public float capacityRetryTimer;
            [NonSerialized] public bool destinationErrorLogged;

            public bool IsWaitingForSpace => completionReached && !refundPending
                && pendingOutput != null && !pendingOutput.IsEmpty;
            public bool CanCancel => !completionReached && !refundPending;
        }

        // Runtime-only queue: destination is an interface and intentionally does not participate
        // in Unity serialization. Craft state is owned by the live station/session.
        [NonSerialized] public List<Entry> entries = new();
        public event Action OnChanged;

        public Entry Head => entries.Count > 0 ? entries[0] : null;
        public bool HasWork => entries.Count > 0;

        private void Update()
        {
            if (entries.Count == 0) return;

            // Canceled jobs keep any unreturned ingredients until the destination has
            // room. Process these independently so a later canceled job cannot block
            // the active head of the craft queue.
            bool removedRefund = false;
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    entries.RemoveAt(i);
                    removedRefund = true;
                    continue;
                }
                if (!entry.refundPending) continue;

                entry.capacityRetryTimer -= Time.deltaTime;
                if (entry.capacityRetryTimer > 0f) continue;
                entry.capacityRetryTimer = CapacityRetrySeconds;
                if (TryReturnIngredients(entry))
                {
                    entries.RemoveAt(i);
                    removedRefund = true;
                }
                else if (Time.frameCount % 10 == 0)
                {
                    OnChanged?.Invoke();
                }
            }
            if (removedRefund) OnChanged?.Invoke();
            if (entries.Count == 0) return;

            var head = entries[0];
            if (head.refundPending) return;
            if (head.recipe == null)
            {
                if (Time.frameCount % 10 == 0) OnChanged?.Invoke();
                return;
            }

            if (!head.completionReached)
            {
                head.progressSeconds += Time.deltaTime;
                if (head.progressSeconds < head.recipe.craftSeconds)
                {
                    // Only fire OnChanged occasionally so UI doesn't rebuild every frame.
                    if (Time.frameCount % 10 == 0) OnChanged?.Invoke();
                    return;
                }

                head.progressSeconds = Mathf.Max(head.progressSeconds, head.recipe.craftSeconds);
                head.completionReached = true;

                if (head.recipe.outputItem == null || head.recipe.outputCount <= 0)
                {
                    // A malformed recipe must not consume its inputs and then vanish.
                    QueueIngredientRefund(head);
                    if (TryReturnIngredients(head)) entries.RemoveAt(0);
                    else head.capacityRetryTimer = CapacityRetrySeconds;
                    OnChanged?.Invoke();
                    return;
                }

                head.pendingOutput = new ItemStack(head.recipe.outputItem, head.recipe.outputCount);
            }

            head.capacityRetryTimer -= Time.deltaTime;
            if (head.capacityRetryTimer > 0f)
            {
                if (Time.frameCount % 10 == 0) OnChanged?.Invoke();
                return;
            }

            head.capacityRetryTimer = CapacityRetrySeconds;
            head.pendingOutput = TryInsertRetainingRemainder(head, head.pendingOutput);
            if (head.pendingOutput == null || head.pendingOutput.IsEmpty)
            {
                entries.RemoveAt(0);
                OnChanged?.Invoke();
            }
            else if (Time.frameCount % 10 == 0)
            {
                OnChanged?.Invoke();
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

        /// <summary>
        /// Cancel an unfinished queued entry. Ingredients are returned to the destination;
        /// if it is full, the refund remains pending instead of being discarded.
        /// Completed crafts cannot be canceled, preventing a blocked output from being
        /// converted into a free ingredient refund.
        /// </summary>
        public void Cancel(int index)
        {
            if (index < 0 || index >= entries.Count) return;
            var entry = entries[index];
            if (entry == null || !entry.CanCancel) return;

            QueueIngredientRefund(entry);
            if (TryReturnIngredients(entry))
                entries.RemoveAt(index);
            else
                entry.capacityRetryTimer = CapacityRetrySeconds;
            OnChanged?.Invoke();
        }

        private static void QueueIngredientRefund(Entry entry)
        {
            entry.refundPending = true;
            entry.pendingOutput = null;
            entry.pendingRefunds = new List<ItemStack>();
            if (entry.recipe == null || entry.recipe.inputs == null) return;

            foreach (var ingredient in entry.recipe.inputs)
            {
                if (ingredient.item == null || ingredient.count <= 0) continue;
                entry.pendingRefunds.Add(new ItemStack(ingredient.item, ingredient.count));
            }
        }

        /// <summary>Insert every queued refund, retaining any remainder that cannot fit.</summary>
        private static bool TryReturnIngredients(Entry entry)
        {
            if (entry.pendingRefunds == null || entry.pendingRefunds.Count == 0) return true;

            for (int i = entry.pendingRefunds.Count - 1; i >= 0; i--)
            {
                ItemStack remainder = TryInsertRetainingRemainder(entry, entry.pendingRefunds[i]);
                if (remainder == null || remainder.IsEmpty)
                    entry.pendingRefunds.RemoveAt(i);
                else
                    entry.pendingRefunds[i] = remainder;
            }
            return entry.pendingRefunds.Count == 0;
        }

        /// <summary>
        /// Container insertion is allowed to be partial. Retain the exact remainder so a
        /// timed craft or cancellation refund can retry after capacity becomes available.
        /// </summary>
        private static ItemStack TryInsertRetainingRemainder(Entry entry, ItemStack stack)
        {
            if (stack == null || stack.IsEmpty) return null;
            if (entry == null || entry.destination == null) return stack;

            try
            {
                ItemStack remainder = entry.destination.Insert(stack);
                return remainder == null || remainder.IsEmpty ? null : remainder;
            }
            catch (Exception exception)
            {
                if (!entry.destinationErrorLogged)
                {
                    entry.destinationErrorLogged = true;
                    Debug.LogError("[CraftQueue] Could not deliver queued items; the remainder is being retained. "
                        + exception.Message);
                }
                return stack.IsEmpty ? null : stack;
            }
        }
    }
}
