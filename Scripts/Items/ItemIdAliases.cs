// Assets/Scripts/VoxelEngine/Items/ItemIdAliases.cs
using System.Collections.Generic;

namespace VoxelEngine.Items
{
    /// <summary>
    /// Retired item ids and the item that replaced them.
    ///
    /// When two assets described the same logical item, one of them is retired. A save
    /// written before that consolidation still names the retired id, and the asset behind
    /// it no longer exists — without a bridge those stacks would deserialize as empty and
    /// the player would silently lose them.
    ///
    /// The persistence layer consults this map after building its item cache: a retired id
    /// resolves to its replacement, so an old save loads as though it had always named the
    /// surviving item. An alias is never allowed to shadow a live id, so re-introducing an
    /// asset under a retired id simply takes precedence again.
    /// </summary>
    public static class ItemIdAliases
    {
        /// <summary>Retired id -> surviving id.</summary>
        public static readonly IReadOnlyDictionary<string, string> Retired =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                // 11.3.0-dev: the ore consolidation. Two assets described each of these
                // ores; the Resource-typed Industrial asset (with its icon and category)
                // survives, and the bare duplicate under Items/ is retired.
                { "iron",   "iron_ore"   },
                { "copper", "copper_ore" },
            };

        /// <summary>The surviving id for <paramref name="itemId"/>, or the id itself when it was never retired.</summary>
        public static string Resolve(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return itemId;
            return Retired.TryGetValue(itemId, out var replacement) ? replacement : itemId;
        }
    }
}
