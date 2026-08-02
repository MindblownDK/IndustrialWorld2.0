// Assets/Scripts/VoxelEngine/Maritime/MechanicalBeltItem.cs
//
// Consumable player-held belt used to join two parallel shaft pulleys. The
// actual link lives on the owning movable GridEntity and is persisted with it.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.Maritime
{
    [CreateAssetMenu(menuName = "Voxel Engine/Maritime/Mechanical Belt", fileName = "Item_MechanicalBelt")]
    public sealed class MechanicalBeltItem : ItemDefinition
    {
        [Header("Mechanical Belt")]
        [Tooltip("Maximum centre-to-centre span between the two shaft pulleys, in metres.")]
        [Min(0.5f)] public float maxSpanMeters = 20f;

        [Tooltip("Shortest valid pulley separation, in metres. Adjacent inline shafts should use direct couplings instead.")]
        [Min(0.05f)] public float minSpanMeters = 0.75f;

        [Tooltip("Safety cap for independently routed belts on one movable grid.")]
        [Min(1)] public int maxBeltsPerGrid = 64;

        public float EffectiveMaxSpan => Mathf.Max(0.5f, maxSpanMeters);
        public float EffectiveMinSpan => Mathf.Clamp(minSpanMeters, 0.05f, EffectiveMaxSpan);
        public int EffectiveMaxBeltsPerGrid => Mathf.Max(1, maxBeltsPerGrid);
    }
}
