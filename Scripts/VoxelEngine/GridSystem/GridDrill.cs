// Assets/Scripts/VoxelEngine/GridSystem/GridDrill.cs
//
// Ship-mounted drill. Mines terrain in front of the drill when activated.
// Mined resources go into the grid's inventory (via connected cargo containers).

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Modification;

namespace VoxelEngine.GridSystem
{
    public class GridDrill : GridBlock
    {
        [Header("Drill")]
        public float drillRadius = 1.5f;
        public float drillStrength = 80f;
        public float drillRate = 4f; // hits per second
        public int   miningTier = 3;

        public override float PowerDraw => _isActive ? 300f : 0f;
        public bool IsActive => _isActive;

        private bool _isActive;
        private float _drillTimer;

        private void Update()
        {
            if (Grid == null || !Grid.IsControlled || !Grid.HasPower) { _isActive = false; return; }

            // Activate drill with mouse button or toolbar key.
            #if ENABLE_INPUT_SYSTEM
            _isActive = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
#else
            _isActive = Input.GetMouseButton(0);
#endif

            if (!_isActive) return;

            _drillTimer += Time.deltaTime;
            if (_drillTimer < 1f / drillRate) return;
            _drillTimer = 0;

            var world = VoxelWorld.Instance;
            if (world == null) return;

            // Drill forward from the block's position.
            Vector3 drillPoint = transform.position + transform.forward * Grid.gridSize.CellSize();

            // Check tier.
            var registry = world.materialRegistry;
            Vector3Int voxelPos = world.WorldToVoxel(drillPoint);
            var v = world.GetVoxelWorld(voxelPos);
            if (v.density > 0)
            {
                var def = registry?.Get(v.material);
                if (def != null && def.miningTier > miningTier) return; // can't mine
                if (v.material == (byte)MaterialId.Bedrock) return; // can't mine bedrock
            }

            var result = VoxelEditor.Subtract(world, registry, drillPoint, drillRadius, drillStrength);

            // Add mined items to any cargo container on the grid.
            if (result.changed && result.primaryItem != null)
            {
                AddToGridCargo(result.primaryItem, result.primaryAmount);
            }
        }

        private void AddToGridCargo(ItemDefinition item, int amount)
        {
            if (Grid == null || amount <= 0) return;
            foreach (var kv in Grid.Blocks)
            {
                if (kv.Value is GridCargoContainer cargo)
                {
                    var leftover = cargo.container.Insert(new ItemStack(item, amount));
                    if (leftover == null || leftover.count <= 0) return;
                    amount = leftover.count;
                }
            }
        }
    }
}
