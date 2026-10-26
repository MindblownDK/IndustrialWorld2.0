// Assets/Scripts/VoxelEngine/Items/ScienceItem.cs
using System;
using UnityEngine;

namespace VoxelEngine.Items
{
    /// <summary>
    /// A science pack consumed by the Research Lab.
    /// Pack I (Red) is craftable in hand by default; Packs II–VI (Green, Blue, Purple,
    /// Amber/Gold, Cyan/White) are unlocked through dedicated Science Pack research nodes.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Items/Science Pack", fileName = "Science_New")]
    public class ScienceItem : ResourceItem
    {
        public const int MaxTier = 6;

        [Range(1, MaxTier)] public int tier = 1;

        public int EffectiveTier
        {
            get
            {
                if (!string.IsNullOrEmpty(itemId))
                {
                    if (itemId.EndsWith("t6", StringComparison.OrdinalIgnoreCase)) return 6;
                    if (itemId.EndsWith("t5", StringComparison.OrdinalIgnoreCase)) return 5;
                    if (itemId.EndsWith("t4", StringComparison.OrdinalIgnoreCase)) return 4;
                    if (itemId.EndsWith("t3", StringComparison.OrdinalIgnoreCase)) return 3;
                    if (itemId.EndsWith("t2", StringComparison.OrdinalIgnoreCase)) return 2;
                    if (itemId.EndsWith("t1", StringComparison.OrdinalIgnoreCase)) return 1;
                }
                if (!string.IsNullOrEmpty(displayName))
                {
                    string trimmed = displayName.TrimEnd();
                    if (trimmed.EndsWith(" VI", StringComparison.OrdinalIgnoreCase))  return 6;
                    if (trimmed.EndsWith(" IV", StringComparison.OrdinalIgnoreCase))  return 4;
                    if (trimmed.EndsWith(" V", StringComparison.OrdinalIgnoreCase))   return 5;
                    if (trimmed.EndsWith(" III", StringComparison.OrdinalIgnoreCase)) return 3;
                    if (trimmed.EndsWith(" II", StringComparison.OrdinalIgnoreCase))  return 2;
                    if (trimmed.EndsWith(" I", StringComparison.OrdinalIgnoreCase))   return 1;
                }
                return Mathf.Clamp(tier, 1, MaxTier);
            }
        }

        public static Color GetTierColor(int scienceTier) => Mathf.Clamp(scienceTier, 1, MaxTier) switch
        {
            1 => new Color(0.92f, 0.24f, 0.24f), // Pack I: Red
            2 => new Color(0.24f, 0.84f, 0.42f), // Pack II: Green
            3 => new Color(0.24f, 0.58f, 0.96f), // Pack III: Blue
            4 => new Color(0.68f, 0.34f, 0.94f), // Pack IV: Purple
            5 => new Color(0.96f, 0.72f, 0.18f), // Pack V: Amber / Gold
            6 => new Color(0.56f, 0.95f, 1.00f), // Pack VI: Cyan / White
            _ => new Color(0.92f, 0.24f, 0.24f)
        };

        public static string GetTierRomanNumeral(int scienceTier) => Mathf.Clamp(scienceTier, 1, MaxTier) switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
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
