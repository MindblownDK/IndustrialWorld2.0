// Assets/Scripts/VoxelEngine/GridSystem/ProcessingRecipeRunner.cs
//
// Shared helper that lets grid machine blocks (Refinery, Chemical Plant…) run
// the SAME ProcessingRecipe assets the stationary machines use, sourcing inputs
// from and depositing outputs into the GridCargoContainer blocks on a grid.
//
// Keeping this logic in one place means the grid + stationary versions of a
// machine stay behaviourally identical and recipe-compatible.

using System.Collections.Generic;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public static class ProcessingRecipeRunner
    {
        /// <summary>Returns the first recipe whose inputs are all present in grid cargo
        /// AND whose outputs have somewhere to go. Null if none can run right now.</summary>
        public static ProcessingRecipe FindRunnable(List<ProcessingRecipe> recipes, GridEntity grid)
        {
            if (recipes == null || grid == null) return null;
            var cargo = CollectCargo(grid);
            if (cargo.Count == 0) return null;

            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r == null) continue;
                if (HasAllInputs(r, cargo) && HasOutputSpace(r, cargo)) return r;
            }
            return null;
        }

        /// <summary>Consumes the recipe's inputs from grid cargo and inserts its outputs.
        /// Returns false (and changes nothing) if it cannot complete cleanly.</summary>
        public static bool RunBatch(ProcessingRecipe r, GridEntity grid)
        {
            if (r == null || grid == null) return false;
            var cargo = CollectCargo(grid);
            if (!HasAllInputs(r, cargo) || !HasOutputSpace(r, cargo)) return false;

            // Consume inputs.
            if (r.inputs != null)
                foreach (var ing in r.inputs)
                {
                    if (ing.item == null || ing.count <= 0) continue;
                    RemoveAcross(cargo, ing.item, ing.count);
                }

            // Produce outputs.
            if (r.outputs != null)
                foreach (var o in r.outputs)
                {
                    if (o.item == null || o.count <= 0) continue;
                    InsertAcross(cargo, new ItemStack { item = o.item, count = o.count });
                }
            return true;
        }

        // ── internals ───────────────────────────────────────────────────────
        private static List<ItemContainer> CollectCargo(GridEntity grid)
        {
            var list = new List<ItemContainer>();
            foreach (var kv in grid.Blocks)
                if (kv.Value is GridCargoContainer c && c.container != null)
                    list.Add(c.container);
            return list;
        }

        private static bool HasAllInputs(ProcessingRecipe r, List<ItemContainer> cargo)
        {
            if (r.inputs == null) return false;
            foreach (var ing in r.inputs)
            {
                if (ing.item == null || ing.count <= 0) continue;
                int have = 0;
                foreach (var c in cargo) have += c.CountOf(ing.item);
                if (have < ing.count) return false;
            }
            return true;
        }

        private static bool HasOutputSpace(ProcessingRecipe r, List<ItemContainer> cargo)
        {
            if (r.outputs == null) return true;
            foreach (var o in r.outputs)
            {
                if (o.item == null || o.count <= 0) continue;
                int space = 0;
                foreach (var c in cargo) if (c.HasSpace(o.item, o.count - space)) { space = o.count; break; }
                if (space < o.count) return false;
            }
            return true;
        }

        private static void RemoveAcross(List<ItemContainer> cargo, ItemDefinition item, int count)
        {
            int remaining = count;
            foreach (var c in cargo)
            {
                if (remaining <= 0) break;
                remaining -= c.Remove(item, remaining);
            }
        }

        private static void InsertAcross(List<ItemContainer> cargo, ItemStack stack)
        {
            foreach (var c in cargo)
            {
                if (stack == null || stack.IsEmpty) return;
                stack = c.Insert(stack);
            }
        }
    }
}
