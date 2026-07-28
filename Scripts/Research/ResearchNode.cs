// Assets/Scripts/VoxelEngine/Research/ResearchNode.cs
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Crafting;
using VoxelEngine.Items;

namespace VoxelEngine.Research
{
    public enum ResearchCategory { Environment, PlayerUpgrades }

    /// <summary>
    /// Sub-category used by the factory research ResearchUI to filter Environment
    /// nodes into thematic columns (Logistics, Production, Power, etc.).
    /// PlayerUpgrades nodes ignore this field and always live in the
    /// "Player Upgrades" tab.
    /// </summary>
    public enum ResearchSubCategory
    {
        General,
        Logistics,
        Production,
        Power,
        Chemistry,
        Storage,
        Building,
        Military
    }

    /// <summary>
    /// What a research node DOES when researched. Environment nodes unlock recipes.
    /// PlayerUpgrade nodes apply a stat modifier (and can be repeatable up to maxRanks).
    /// </summary>
    public enum PlayerUpgradeKind
    {
        None,
        BonusMaxHealth,        // +N max HP per rank
        BonusInventorySlots,   // +N backpack slots per rank
        BonusDamage,           // +N flat damage per rank
        BonusMaxStamina,       // +N max stamina per rank
        BonusSprintMultiplier, // +N to sprint multiplier per rank (capped to 5x)
        UnlockFlight           // single-rank toggle: enables fly mode permanently
    }

    [CreateAssetMenu(menuName = "Voxel Engine/Research/Node", fileName = "Research_New")]
    public class ResearchNode : ScriptableObject
    {
        [Header("Identity")]
        public string nodeId = "research_id";
        public string displayName = "New Research";
        [TextArea] public string description;
        public Color iconTint = Color.white;

    [Header("Category & Tree placement")]
    public ResearchCategory category = ResearchCategory.Environment;
    [Tooltip("Sub-category used to filter the factory research UI into thematic tabs.")]
    public ResearchSubCategory subCategory = ResearchSubCategory.General;
    [Range(1, 10)] public int tier = 1;
    public int column = 0;

    [Header("Visual")]
    [Tooltip("Optional era label shown above the tier (e.g. 'Era 1: Mechanized'). Auto-assigned if empty.")]
    public string eraLabel = "";

        [Header("Prerequisites")]
        public ResearchNode[] prerequisites = new ResearchNode[0];

        [Header("Cost")]
        [Tooltip("Seconds spent at a Research Lab. 0 = instant (cost paid from inventory).")]
        public float researchSeconds = 30f;
        public ScienceCost[] cost = new ScienceCost[0];

        [Header("Unlocks (Environment nodes)")]
        public RecipeDefinition[] unlocksRecipes = new RecipeDefinition[0];

        [Header("Player Upgrades")]
        public PlayerUpgradeKind upgradeKind = PlayerUpgradeKind.None;
        [Tooltip("Magnitude of the effect per rank. E.g. 25 for +25 HP per rank, 0.25 for +0.25 sprint multiplier.")]
        public float upgradePerRankAmount = 0f;
        [Tooltip("How many times this node can be researched. 1 = single-rank.")]
        public int   maxRanks = 1;
        [Tooltip("If true the cost MULTIPLIES by current rank+1 each time (Tier-1 = 5 packs, Tier-2 = 10 packs, etc.).")]
        public bool  costScalesWithRank = true;

        [System.Serializable]
        public struct ScienceCost
        {
            public ScienceItem pack;
            public int count;
        }

        public bool IsRepeatable => maxRanks > 1;
    }
}
