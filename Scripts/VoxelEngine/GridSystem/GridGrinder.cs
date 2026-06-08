// Assets/Scripts/VoxelEngine/GridSystem/GridGrinder.cs
//
// Ship-mounted grinder. Breaks down blocks and terrain into resources.
// Better than Space Engineers: returns more resources, has tier system, particle FX.

using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;
using VoxelEngine.Modification;

namespace VoxelEngine.GridSystem
{
    public class GridGrinder : GridBlock
    {
        [Header("Grinder")]
        public float grindRadius = 1.2f;
        public float grindStrength = 60f;
        public float grindRate = 5f;
        public int miningTier = 2;

        public override float PowerDraw => _isActive ? 250f : 0f;

        private bool _isActive;
        private float _grindTimer;

        private void Update()
        {
            if (Grid == null || !Grid.IsControlled || !Grid.HasPower) { _isActive = false; return; }

            #if ENABLE_INPUT_SYSTEM
            _isActive = UnityEngine.InputSystem.Mouse.current != null && UnityEngine.InputSystem.Mouse.current.rightButton.isPressed;
            #else
            _isActive = Input.GetMouseButton(1);
            #endif

            if (!_isActive) return;

            _grindTimer += Time.deltaTime;
            if (_grindTimer < 1f / grindRate) return;
            _grindTimer = 0;

            Vector3 grindPoint = transform.position + transform.forward * Grid.gridSize.CellSize();
            var world = VoxelWorld.Instance;
            if (world == null) return;

            var result = VoxelEditor.Subtract(world, world.materialRegistry, grindPoint, grindRadius, grindStrength);

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
                if (kv.Value is GridCargoContainer cargo && cargo.container != null)
                {
                    cargo.container.Insert(new ItemStack(item, amount));
                }
            }
        }
    }
}