// Assets/Scripts/VoxelEngine/GridSystem/GridLiquidTank.cs
//
// Unified liquid tank for grids — replaces the old GridWaterTank and
// GridLiquidFuelTank. The liquid type is configurable from the tank UI (only
// while empty), it can be drained (voids its contents), and its stored liquid
// adds mass to the ship (litres × density).

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    public enum GridTankMode
    {
        Auto,
        Stockpile
    }

    public class GridLiquidTank : GridBlock
    {
        [Header("Liquid Tank")]
        public LiquidType liquidType = LiquidType.Water;

        [Tooltip("Capacity in litres.")]
        public float capacity = 500f;
        public float stored;

        [Tooltip("Auto allows machines to draw from this tank. Stockpile keeps contents reserved.")]
        public GridTankMode mode = GridTankMode.Auto;

        public float Fill01 => capacity > 0 ? Mathf.Clamp01(stored / capacity) : 0f;

        /// <summary>Stored liquid mass (kg) = litres × density. Feeds the ship's total mass.</summary>
        public override float ContentMass => stored * liquidType.DensityKgPerL();

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (Grid != null && GridLiquidNetwork.Instance != null)
                GridLiquidNetwork.Instance.RegisterTank(Grid, this);
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (Grid != null && GridLiquidNetwork.Instance != null)
                GridLiquidNetwork.Instance.UnregisterTank(Grid, this);
        }

        /// <summary>Add up to capacity. Returns the litres actually accepted.</summary>
        public float Add(float litres)
        {
            if (litres <= 0) return 0;
            float space = capacity - stored;
            float take = Mathf.Min(space, litres);
            stored += take;
            return take;
        }

        /// <summary>Remove up to <paramref name="litres"/>. Returns the litres actually drawn.</summary>
        public float Remove(float litres)
        {
            if (litres <= 0) return 0;
            float take = Mathf.Min(stored, litres);
            stored -= take;
            return take;
        }

        public bool TryConsume(float litres)
        {
            if (stored < litres) return false;
            stored -= litres;
            return true;
        }

        /// <summary>Void the entire contents (drain valve).</summary>
        public void Drain() => stored = 0f;

        /// <summary>Change the carried liquid — only allowed when empty.</summary>
        public bool SetLiquidType(LiquidType t)
        {
            if (stored > 0.001f) return false;
            liquidType = t;
            return true;
        }

        // ── Universal bucket exchange (9.16.0) ────────────────────
        /// <summary>Litres carried by one full bucket scoop (gameplay abstraction).</summary>
        public const float BucketLitres = 100f;

        /// <summary>Fill an EMPTY bucket from this tank. The bucket remembers the liquid.</summary>
        public bool TryFillBucket(ItemStack bucket)
        {
            if (bucket == null || bucket.item == null) return false;
            if (bucket.durability > 0) return false;          // already carries liquid
            if (stored <= 0.001f) return false;               // tank is dry
            float take = Remove(BucketLitres);
            if (take <= 0f) return false;
            bucket.durability = 1;
            bucket.payload = liquidType;
            return true;
        }

        /// <summary>Pour a full bucket into this tank (same liquid, or any into an empty tank).</summary>
        public bool TryEmptyBucket(ItemStack bucket)
        {
            if (bucket == null || bucket.item == null) return false;
            if (bucket.durability <= 0) return false;
            if (!(bucket.payload is LiquidType carried)) return false;
            if (stored > 0.001f && liquidType != carried) return false;
            if (stored + BucketLitres > capacity + 0.001f) return false;   // needs full room
            liquidType = carried;
            stored += BucketLitres;
            bucket.durability = 0;
            bucket.payload = null;
            return true;
        }
    }
}
