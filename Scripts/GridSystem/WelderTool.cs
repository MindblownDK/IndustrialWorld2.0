// Assets/Scripts/VoxelEngine/GridSystem/WelderTool.cs
//
// 14.61.0 - the welder: the grinder's constructive twin. Hold LMB on a damaged
// grid block to restore its hit points. Repairs are paid for in material
// (default: Iron Ingots) as HP flows back - the look-at HUD shows the full
// cost of the block under the crosshair while a welder is in hand.

using UnityEngine;
using VoxelEngine.Items;

namespace VoxelEngine.GridSystem
{
    [CreateAssetMenu(menuName = "Voxel Engine/Grid/Welder Tool")]
    public class WelderTool : ToolItem
    {
        [Header("Welding")]
        [Tooltip("Hit points restored per second of welding.")]
        public float repairHPPerSecond = 150f; // 14.65.0 — 45 was a soldering iron, not a welder

        [Tooltip("Material consumed as HP is restored. One unit pays for hpPerMaterialUnit hit points.")]
        public ItemDefinition repairMaterial;

        [Tooltip("Hit points restored per unit of repair material.")]
        public float hpPerMaterialUnit = 60f;
    }
}
