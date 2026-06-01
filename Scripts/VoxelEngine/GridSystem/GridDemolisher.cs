// Assets/Scripts/VoxelEngine/GridSystem/GridDemolisher.cs
//
// Demolition block — damages buildings and other grids on contact.
// Not instant — applies DPS (damage per second) on collision.

using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.GridSystem
{
    public class GridDemolisher : GridBlock
    {
        [Header("Demolisher")]
        [Tooltip("Damage per second applied to contacted blocks.")]
        public float damagePerSecond = 50f;
        [Tooltip("Damage per second to terrain (voxel removal strength).")]
        public float terrainDPS = 30f;

        public override float PowerDraw => _isContacting ? 200f : 0f;

        private bool _isContacting;

        private void OnCollisionStay(Collision collision)
        {
            if (Grid == null || !Grid.HasPower) return;
            _isContacting = true;

            float dmg = damagePerSecond * Time.deltaTime;

            // Damage placed blocks.
            var placed = collision.collider.GetComponentInParent<PlacedBlock>();
            if (placed != null)
            {
                placed.Damage((int)dmg, null);
                return;
            }

            // Damage tiered blocks.
            var tiered = collision.collider.GetComponentInParent<PlacedTieredBlock>();
            if (tiered != null)
            {
                tiered.Damage((int)dmg, 99, null);
                return;
            }

            // Damage other grid blocks.
            var otherBlock = collision.collider.GetComponentInParent<GridBlock>();
            if (otherBlock != null && otherBlock.Grid != Grid)
            {
                otherBlock.Damage(dmg);
                return;
            }

            // Damage terrain.
            var world = VoxelEngine.Core.VoxelWorld.Instance;
            if (world != null)
            {
                var point = collision.GetContact(0).point;
                VoxelEngine.Modification.VoxelEditor.Subtract(
                    world, world.materialRegistry, point, 0.8f, terrainDPS * Time.deltaTime);
            }
        }

        private void OnCollisionExit(Collision collision)
        {
            _isContacting = false;
        }
    }
}
