// Assets/Scripts/VoxelEngine/Building/Tiered/TieredBlockDefinition.cs
using System;
using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Building.Tiered
{
    /// <summary>
    /// Tiered building family. Holds 4 prefab variants (Wood/Stone/Iron/Steel) plus
    /// the upgrade-cost lists for each tier transition.
    /// </summary>
    [CreateAssetMenu(menuName = "Voxel Engine/Building/Tiered Block", fileName = "TBlock_New")]
    public class TieredBlockDefinition : ScriptableObject
    {
        [Header("Identity")]
        public BuildFamily family = BuildFamily.Foundation;
        public string      displayName = "Foundation";

        [Header("Visuals (one prefab per tier)")]
        public GameObject woodPrefab;
        public GameObject stonePrefab;
        public GameObject ironPrefab;
        public GameObject steelPrefab;

        [Header("Per-tier stats")]
        public TierStats wood   = new TierStats { hp = 200,  miningTier = 1 };
        public TierStats stone  = new TierStats { hp = 500,  miningTier = 2 };
        public TierStats iron   = new TierStats { hp = 1500, miningTier = 3 };
        public TierStats steel  = new TierStats { hp = 4000, miningTier = 4 };

        [Header("Costs to PLACE the wood tier (initial build)")]
        public TierCost placeCost = new TierCost();

        [Header("Upgrade costs (paid in addition to having the lower tier already placed)")]
        public TierCost woodToStone  = new TierCost();
        public TierCost stoneToIron  = new TierCost();
        public TierCost ironToSteel  = new TierCost();

        // ---- Helpers ----
        public GameObject GetPrefab(BuildTier t) => t switch
        {
            BuildTier.Wood  => woodPrefab,
            BuildTier.Stone => stonePrefab,
            BuildTier.Iron  => ironPrefab,
            BuildTier.Steel => steelPrefab,
            _ => null
        };

        public TierStats GetStats(BuildTier t) => t switch
        {
            BuildTier.Wood  => wood,
            BuildTier.Stone => stone,
            BuildTier.Iron  => iron,
            BuildTier.Steel => steel,
            _ => default
        };

        public TierCost GetUpgradeCost(BuildTier from) => from switch
        {
            BuildTier.Wood  => woodToStone,
            BuildTier.Stone => stoneToIron,
            BuildTier.Iron  => ironToSteel,
            _ => new TierCost()
        };

        public static BuildTier NextTier(BuildTier t) => t switch
        {
            BuildTier.Wood  => BuildTier.Stone,
            BuildTier.Stone => BuildTier.Iron,
            BuildTier.Iron  => BuildTier.Steel,
            _ => BuildTier.Steel
        };
    }

    [Serializable]
    public struct TierStats
    {
        public int hp;
        [Range(0,4)] public int miningTier;
    }

    /// <summary>
    /// A small (up to 4) ingredient list for placing or upgrading a block.
    /// </summary>
    [Serializable]
    public class TierCost
    {
        public Ingredient[] items = new Ingredient[0];
    }

    [Serializable]
    public struct Ingredient
    {
        public ItemDefinition item;
        public int            count;
    }
}
