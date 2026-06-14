// Assets/Scripts/VoxelEngine/Fluids/WaterTank.cs
// Liquid tank node. Kept backwards-compatible with old water-only users through
// the `water` field, but now carries a configurable LiquidType so water and crude
// oil can share the same pipe/tank infrastructure.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Fluids
{
    public class WaterTank : FluidNode
    {
        public override FluidNodeKind Kind => FluidNodeKind.Tank;

        public float capacityLitres = 1000f;
        public LiquidType liquidType = LiquidType.Water;
        [Tooltip("Stored litres. Legacy name kept so existing prefabs/saves keep their fill amount.")]
        public float water = 0f;
        public bool isGlass = false;

        public float StoredLitres => water;
        public float Fill01 => capacityLitres > 0 ? Mathf.Clamp01(water / capacityLitres) : 0f;
        public bool IsEmpty => water <= 0.001f;

        public bool CanAccept(LiquidType type) => IsEmpty || liquidType == type;

        public bool TryAdd(float litres) => TryAdd(LiquidType.Water, litres);

        public bool TryAdd(LiquidType type, float litres)
        {
            if (!CanAccept(type)) return false;
            float space = capacityLitres - water;
            if (litres > space) return false;
            if (IsEmpty) liquidType = type;
            water += litres;
            return true;
        }

        public float AddSome(float litres) => AddSome(LiquidType.Water, litres);

        public float AddSome(LiquidType type, float litres)
        {
            if (litres <= 0f || !CanAccept(type)) return 0f;
            float space = Mathf.Max(0f, capacityLitres - water);
            float add = Mathf.Min(space, litres);
            if (add > 0f && IsEmpty) liquidType = type;
            water += add;
            return add;
        }

        public float TakeSome(float litres) => TakeSome(liquidType, litres);

        public float TakeSome(LiquidType type, float litres)
        {
            if (type != liquidType || litres <= 0f) return 0f;
            float take = Mathf.Min(water, litres);
            water -= take;
            return take;
        }
    }
}
