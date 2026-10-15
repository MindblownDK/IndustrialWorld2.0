// Assets/Scripts/Farming/WildTreeSaplingDrop.cs
//
// Awards 1-2 Tree Sapling items when a wild scatter tree is chopped down,
// kept in a dedicated helper class so VoxelEngine.Trees.Tree remains untouched.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Farming
{
    public static class WildTreeSaplingDrop
    {
        private static ItemDefinition _cachedSaplingItem;

        public static void AwardSaplings(Inventory inv)
        {
            if (inv == null) inv = Object.FindAnyObjectByType<Inventory>();
            if (inv == null) return;

            var sapling = ResolveSaplingItem();
            if (sapling == null) return;

            int count = Random.Range(1, 3);
            inv.Add(sapling, count);
        }

        public static ItemDefinition ResolveSaplingItem()
        {
            if (_cachedSaplingItem != null) return _cachedSaplingItem;
            var all = Resources.FindObjectsOfTypeAll<ItemDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                var it = all[i];
                if (it == null) continue;
                if (it.itemId == "seed_tree_sapling" || it.itemId == "item_tree_sapling" || it.displayName == "Tree Sapling")
                {
                    _cachedSaplingItem = it;
                    return it;
                }
            }
            return null;
        }
    }
}
