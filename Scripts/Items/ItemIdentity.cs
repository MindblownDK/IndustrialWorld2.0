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
    /// case-insensitive <see cref="ItemDefinition.itemId"/> match. A blank id never matches
    /// anything: an asset whose id was never authored has no identity to compare, so it is
    /// only ever equal to itself by reference.
    /// </summary>
    public static class ItemIdentity
    {
        /// <summary>
        /// The id older assets were left holding when theirs was never authored. It is no
        /// longer a field default, but assets serialized before that change still carry it,
        /// so step 79 uses this to recognise and repair them.
        /// </summary>
        public const string LegacyDefaultId = "iron_ore";

        /// <summary>True when <paramref name="id"/> is a real id rather than blank.</summary>
        public static bool IsAuthoredId(string id) => !string.IsNullOrWhiteSpace(id);

        /// <summary>
        /// True when the two references mean the same item. Reference equality wins
        /// immediately; otherwise both must carry the same authored id.
        /// </summary>
        public static bool Same(ItemDefinition a, ItemDefinition b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a == b) return true;

            // A blank id carries no identity, so such an asset is only ever equal to itself.
            if (!IsAuthoredId(a.itemId) || !IsAuthoredId(b.itemId)) return false;
            return string.Equals(a.itemId, b.itemId, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
