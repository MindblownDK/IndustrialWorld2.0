// Assets/Scripts/VoxelEngine/GridSystem/GridDrill.cs
//
// Advanced ship drill. Mines voxels in front of it into a small internal buffer,
// then auto-pushes the buffer's contents to the nearest cargo container on the
// grid (so the drill never clogs).

using UnityEngine;
using VoxelEngine.Items;
using VoxelEngine.Core;
using VoxelEngine.Materials;
using VoxelEngine.Modification;

namespace VoxelEngine.GridSystem
{
    public class GridDrill : GridBlock
    {
        public const int BUFFER_SLOTS = 4;

        [Header("Drill")]
        public float drillRadius = 2f;
        public float drillStrength = 120f;
        public float drillRate = 3f;
        [Tooltip("How far ahead of the drill we reach for terrain (metres).")]
        public float drillReach = 4f;

        private VoxelWorld _world;
        private MaterialRegistry _registry;

        [Tooltip("Small internal buffer; auto-empties into grid cargo.")]
        public ItemContainer buffer;

        public override float PowerDraw => _isActive ? 450f : 0f;
        public override float ContentMass => buffer != null ? MassUtil.ContainerMass(buffer) : 0f;
        public bool IsActive => _isActive;

        private bool _isActive;
        private float _drillTimer;
        private float _pushTimer;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Mining Drill";
            if (buffer == null) buffer = new ItemContainer("Drill", BUFFER_SLOTS);
            else buffer.Resize(BUFFER_SLOTS);
        }

        private void Update()
        {
            // Always try to offload the buffer, even when not actively drilling.
            _pushTimer += Time.deltaTime;
            if (_pushTimer >= 0.5f) { _pushTimer = 0f; PushToCargo(); }

            if (!Enabled || Grid == null || !Grid.IsControlled || !Grid.HasPower || !Grid.IsSelectedTool(this))
            {
                _isActive = false;
                return;
            }

            _isActive = GridInput.Mouse0;
            if (!_isActive) return;

            _drillTimer += Time.deltaTime;
            if (_drillTimer < 1f / drillRate) return;
            _drillTimer = 0;

            MineForward();
        }

        // Carve a sphere of terrain directly in front of the drill and route every mined
        // item into the internal buffer (which auto-empties into ship cargo).
        private void MineForward()
        {
            if (_world == null) _world = VoxelWorld.Instance;
            if (_registry == null) _registry = Object.FindAnyObjectByType<MaterialRegistry>();
            if (_world == null || _registry == null) return;

            // Cast forward from the drill face; if we hit terrain, carve there. Otherwise
            // carve a point a short way ahead so the drill still bites into surfaces it is
            // pushed against even without a clean ray hit.
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            Vector3 carveAt = origin + dir * (drillReach * 0.5f);
            if (Physics.Raycast(origin, dir, out var hit, drillReach))
                carveAt = hit.point - dir * 0.1f;

            var res = VoxelEditor.SubtractCollect(_world, _registry, carveAt, drillRadius, drillStrength);
            if (!res.changed || res.drops == null) return;

            for (int m = 1; m < res.drops.Length; m++)
            {
                if (res.drops[m] <= 0) continue;
                var def = _registry.Get((byte)m);
                if (def?.dropItem == null) continue;
                CollectOre(def.dropItem, res.drops[m]);
            }
        }

        /// <summary>Deposit mined material into the buffer (called by the mining system).</summary>
        public int CollectOre(ItemDefinition ore, int count)
        {
            if (buffer == null || ore == null) return 0;
            var leftover = buffer.Insert(new ItemStack(ore, count));
            return count - (leftover?.count ?? 0);
        }

        // Empty the buffer into the nearest cargo container on the grid.
        private void PushToCargo()
        {
            if (buffer == null || Grid == null || GridItemNetwork.Instance == null) return;
            var cargos = GridItemNetwork.Instance.GetConnectedContainers(Grid);
            if (cargos.Count == 0) return;

            for (int i = 0; i < buffer.Size; i++)
            {
                var s = buffer.GetSlot(i);
                if (s == null || s.IsEmpty || s.item == null) continue;
                var moving = new ItemStack(s.item, s.count);
                foreach (var cargo in cargos)
                {
                    if (cargo?.container == null) continue;
                    moving = cargo.container.Insert(moving);
                    if (moving == null || moving.IsEmpty) break;
                }
                // Whatever moved, remove from the buffer.
                int moved = s.count - (moving?.count ?? 0);
                if (moved > 0) buffer.Remove(s.item, moved);
            }
        }
    }
}
