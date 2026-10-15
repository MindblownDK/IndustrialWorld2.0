// Assets/Scripts/VoxelEngine/Items/ScienceItem.cs
using System;
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// A "science pack" item consumed by the Research Lab. Each tier represents how
    /// advanced the research it enables is. Tier 1 packs are craftable at the inventory,
    /// Tier 2 at the Crafting Bench, Tier 3 at the Assembler.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Science Pack", fileName = "Science_New")]
    public class ScienceItem : ResourceItem
    {
        [Range(1, 3)] public int tier = 1;

        public int EffectiveTier
        {
            get
            {
                if (!string.IsNullOrEmpty(itemId))
                {
                    if (itemId.EndsWith("t1", StringComparison.OrdinalIgnoreCase)) return 1;
                    if (itemId.EndsWith("t2", StringComparison.OrdinalIgnoreCase)) return 2;
                    if (itemId.EndsWith("t3", StringComparison.OrdinalIgnoreCase)) return 3;
                }
                if (!string.IsNullOrEmpty(displayName))
                {
                    if (displayName.EndsWith("III", StringComparison.OrdinalIgnoreCase)) return 3;
                    if (displayName.EndsWith("II", StringComparison.OrdinalIgnoreCase)) return 2;
                    if (displayName.EndsWith("I", StringComparison.OrdinalIgnoreCase)) return 1;
                }
                return Mathf.Clamp(tier, 1, 3);
            }
        }

        public static Color GetTierColor(int scienceTier) => Mathf.Clamp(scienceTier, 1, 3) switch
        {
            1 => new Color(0.28f, 0.80f, 0.38f), // Tier I: Green
            2 => new Color(0.25f, 0.62f, 0.96f), // Tier II: Blue
            3 => new Color(0.68f, 0.36f, 0.92f), // Tier III: Purple
            _ => new Color(0.28f, 0.80f, 0.38f)
        };

        public static string GetTierRomanNumeral(int scienceTier) => Mathf.Clamp(scienceTier, 1, 3) switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            _ => "I"
        };

        public Color GetTierColor() => GetTierColor(EffectiveTier);

        public string GetTierRomanNumeral() => GetTierRomanNumeral(EffectiveTier);

        private void OnEnable()
        {
            tier = EffectiveTier;
            iconTint = GetTierColor(tier);
        }

        private void OnValidate()
        {
            tier = EffectiveTier;
            iconTint = GetTierColor(tier);
        }
    }
}
