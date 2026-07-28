// Assets/Scripts/VoxelEngine/Crafting/ProcessingExecutor.cs
//
// Single source of truth for running a ProcessingRecipe against item containers
// + an IFluidStore. Used by every processing machine (Oil Refinery, Chemical
// Plant — stationary and grid) so behaviour and recipe-compatibility are
// identical everywhere.

using System.Collections.Generic;
using VoxelEngine.Items;

namespace VoxelEngine.Crafting
{
    public static class ProcessingExecutor
    {
        /// <summary>Can this recipe run right now given the items + fluids available and output space?</summary>
        public static bool CanRun(ProcessingRecipe r,
            IList<ItemContainer> itemInputs, IList<ItemContainer> itemOutputs, IFluidStore fluids)
        {
            if (r == null) return false;
            return HasItemInputs(r, itemInputs)
                && HasItemSpace(r, itemOutputs)
                && HasFluidInputs(r, fluids)
                && HasFluidSpace(r, fluids);
        }

        /// <summary>Consume inputs + produce outputs. Returns false (changes nothing) if it can't complete.</summary>
        public static bool Run(ProcessingRecipe r,
            IList<ItemContainer> itemInputs, IList<ItemContainer> itemOutputs, IFluidStore fluids)
        {
            if (!CanRun(r, itemInputs, itemOutputs, fluids)) return false;

            if (r.HasItemInputs)
                foreach (var ing in r.inputs)
                    if (ing.item != null && ing.count > 0) RemoveItems(itemInputs, ing.item, ing.count);

            if (r.HasFluidInputs && fluids != null)
                foreach (var f in r.fluidInputs)
                    if (f.litres > 0) fluids.Draw(f.liquid, f.litres);

            if (r.HasItemOutputs)
                foreach (var o in r.outputs)
                    if (o.item != null && o.count > 0) InsertItems(itemOutputs, new ItemStack(o.item, o.count));

            if (r.HasFluidOutputs && fluids != null)
                foreach (var f in r.fluidOutputs)
                    if (f.litres > 0) fluids.Fill(f.liquid, f.litres);

            return true;
        }

        // ── checks ───────────────────────────────────────────────────────────
        private static bool HasItemInputs(ProcessingRecipe r, IList<ItemContainer> inputs)
        {
            if (!r.HasItemInputs) return true;
            if (inputs == null) return false;
            foreach (var ing in r.inputs)
            {
                if (ing.item == null || ing.count <= 0) continue;
                int have = 0;
                foreach (var c in inputs) if (c != null) have += c.CountOf(ing.item);
                if (have < ing.count) return false;
            }
            return true;
        }

        private static bool HasItemSpace(ProcessingRecipe r, IList<ItemContainer> outputs)
        {
            if (!r.HasItemOutputs) return true;
            if (outputs == null) return false;
            foreach (var o in r.outputs)
            {
                if (o.item == null || o.count <= 0) continue;
                bool ok = false;
                foreach (var c in outputs) if (c != null && c.HasSpace(o.item, o.count)) { ok = true; break; }
                if (!ok) return false;
            }
            return true;
        }

        private static bool HasFluidInputs(ProcessingRecipe r, IFluidStore fluids)
        {
            if (!r.HasFluidInputs) return true;
            if (fluids == null) return false;
            foreach (var f in r.fluidInputs)
                if (f.litres > 0 && fluids.Available(f.liquid) < f.litres) return false;
            return true;
        }

        private static bool HasFluidSpace(ProcessingRecipe r, IFluidStore fluids)
        {
            if (!r.HasFluidOutputs) return true;
            if (fluids == null) return false;
            foreach (var f in r.fluidOutputs)
                if (f.litres > 0 && fluids.SpaceFor(f.liquid) < f.litres) return false;
            return true;
        }

        // ── item helpers ──────────────────────────────────────────────────────
        private static void RemoveItems(IList<ItemContainer> from, ItemDefinition item, int count)
        {
            int remaining = count;
            foreach (var c in from)
            {
                if (remaining <= 0) break;
                if (c != null) remaining -= c.Remove(item, remaining);
            }
        }

        private static void InsertItems(IList<ItemContainer> into, ItemStack stack)
        {
            foreach (var c in into)
            {
                if (stack == null || stack.IsEmpty) return;
                if (c != null) stack = c.Insert(stack);
            }
        }
    }
}
