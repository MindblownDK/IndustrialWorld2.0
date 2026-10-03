// Assets/Scripts/VoxelEngine/Storage/CraftingPattern.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║              ENCODED CRAFTING PATTERNS (14.43.0)                 ║
// ║  A pattern is a PHYSICAL item. Craft a Blank Pattern, encode a   ║
// ║  recipe onto it at a Pattern Terminal, then file it in the       ║
// ║  Server Controller's pattern bank. Bank capacity = installed     ║
// ║  RAM units (RAM 4 = 4 patterns, RAM 16 = 16). Encoded patterns   ║
// ║  can be ejected and carried to another network like any item.    ║
// ╚══════════════════════════════════════════════════════════════════╝
//
// The file keeps its historical name (CraftingPattern.cs) so the Unity
// GUID stays stable; the legacy in-RAM CraftingPattern class it carried
// was replaced by the physical-item flow below.

using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Storage
{
    /// <summary>
    /// Stack payload of an encoded pattern. <see cref="recipeId"/> is the
    /// stable RecipeDefinition asset name; <see cref="outputItemId"/> lets a
    /// save rebuild the item's look even before the recipe registry resolves.
    /// Payload stacks never merge (ItemStack rule), so every encoded pattern
    /// stays a distinct physical object.
    /// </summary>
    [System.Serializable]
    public class PatternData
    {
        public string recipeId = "";
        public string outputItemId = "";
    }

    /// <summary>Factory + classification helpers for pattern items.</summary>
    public static class PatternItems
    {
        /// <summary>Stable item id of the craftable blank (authored by setup step 111).</summary>
        public const string BlankItemId = "blank_pattern";

        public static readonly Color PatternTint = new(0.55f, 0.85f, 0.70f);

        public static bool IsBlank(ItemStack s) =>
            s != null && !s.IsEmpty && s.item != null &&
            s.item.itemId == BlankItemId && !(s.payload is PatternData);

        public static bool IsEncoded(ItemStack s) =>
            s != null && !s.IsEmpty && s.payload is PatternData;

        public static PatternData DataOf(ItemStack s) => s?.payload as PatternData;

        /// <summary>
        /// Build the encoded-pattern stack for a recipe. The item is a runtime
        /// clone of the blank (same trick as packed drawers): unique id, the
        /// output's icon, stack of one - and the recipe id rides the payload.
        /// </summary>
        public static ItemStack CreateEncoded(ItemDefinition blankBase, RecipeDefinition recipe)
        {
            if (recipe == null)
                return blankBase != null ? new ItemStack(blankBase, 1) : new ItemStack();
            var data = new PatternData
            {
                recipeId = recipe.name,
                outputItemId = recipe.outputItem != null ? recipe.outputItem.itemId : ""
            };
            return Rebuild(blankBase, data, recipe.outputItem, recipe.GetName());
        }

        /// <summary>
        /// Rebuild an encoded-pattern stack from payload data. Used by the
        /// encode flow, persistence and container sync; never returns null.
        /// </summary>
        public static ItemStack Rebuild(ItemDefinition blankBase, PatternData data,
            ItemDefinition outputItem, string recipeDisplayName = null)
        {
            if (data == null || string.IsNullOrEmpty(data.recipeId))
                return blankBase != null ? new ItemStack(blankBase, 1) : new ItemStack();

            string shownName = !string.IsNullOrEmpty(recipeDisplayName)
                ? recipeDisplayName
                : (outputItem != null ? outputItem.displayName : data.recipeId);

            var def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId      = BlankItemId + "_enc_" + data.recipeId;
            def.displayName = "Pattern: " + shownName;
            def.description = "Encoded crafting pattern. File it in a Server Controller's " +
                              "pattern bank and the network can auto-craft " + shownName +
                              " on request. Eject it to carry the recipe to another network.";
            def.icon        = outputItem != null && outputItem.icon != null
                                ? outputItem.icon
                                : (blankBase != null ? blankBase.icon : null);
            def.iconTint    = outputItem != null ? outputItem.iconTint : PatternTint;
            def.maxStack    = 1;
            def.massPerUnit = blankBase != null ? blankBase.massPerUnit : 0.1f;
            def.category    = blankBase != null ? blankBase.category : "Storage";
            return new ItemStack(def, 1) { payload = data };
        }
    }
}
