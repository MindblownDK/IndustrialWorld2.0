// Assets/Scripts/VoxelEngine/Items/ItemIdentity.cs
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// One place that answers "are these two item references the same item?".
    ///
    /// Recipes store a direct asset reference, so every machine used to compare with
    /// <c>==</c>. That is exact but brittle: the project carries more than one asset for
    /// the same logical item (an ore authored under <c>Items/</c> and again under
    /// <c>Industrial/Items/</c>, for instance). A stack that came from the second asset
    /// could never satisfy a recipe that points at the first, so the machine reported
    /// that the item could not be processed while the player was looking straight at it.
    ///
    /// Identity is therefore reference first — the fast, exact path — and falls back to a
    /// case-insensitive <see cref="ItemDefinition.itemId"/> match. The fallback is
    /// deliberately strict about what counts as an id: the asset default
    /// (<c>"iron_ore"</c>) is rejected, because an asset that never had its id authored
    /// still carries it and would otherwise collide with real ore.
    /// </summary>
    public static class ItemIdentity
    {
        /// <summary>The <see cref="ItemDefinition"/> field default. An asset still carrying
        /// it never had its id authored, so it cannot be used to identify anything.</summary>
        public const string UnauthoredId = "iron_ore";

        /// <summary>True when <paramref name="id"/> is a real authored id rather than blank
        /// or the untouched asset default.</summary>
        public static bool IsAuthoredId(string id) =>
            !string.IsNullOrWhiteSpace(id) &&
            !string.Equals(id, UnauthoredId, System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True when the two references mean the same item. Reference equality wins
        /// immediately; otherwise both must carry the same authored id.
        /// </summary>
        public static bool Same(ItemDefinition a, ItemDefinition b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a == b) return true;

            // The id fallback only applies to the genuine ore/ingot duplicates. An asset
            // that still carries the unauthored default is matched by reference alone,
            // so a mis-authored tool or block can never masquerade as iron ore.
            if (!IsAuthoredId(a.itemId) || !IsAuthoredId(b.itemId)) return false;
            return string.Equals(a.itemId, b.itemId, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
