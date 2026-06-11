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
        [Tooltip("VOID-mode (RMB) is this many times faster than collect-mode (LMB).")]
        public float voidSpeedMultiplier = 2.5f;

        [Tooltip("Log why the drill isn't firing/mining (enable to diagnose).")]
        public bool debugLog = false;

        private VoxelWorld _world;
        private MaterialRegistry _registry;

        [Tooltip("Small internal buffer; auto-empties into grid cargo.")]
        public ItemContainer buffer;

        [Tooltip("Watts consumed while actively drilling.")]
        public float powerDraw = 200f;
        public override float PowerDraw => _isActive ? powerDraw : 0f;
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
                if (debugLog && GridInput.Mouse0)
                    Debug.Log($"[GridDrill] not firing — Enabled={Enabled} Grid={(Grid!=null)} " +
                              $"IsControlled={Grid?.IsControlled} HasPower={Grid?.HasPower} " +
                              $"SelectedTool={(Grid!=null && Grid.IsSelectedTool(this))} (group={Grid?.SelectedGroup})");
                _isActive = false;
                return;
            }

            // LMB = mine + collect; RMB = mine + VOID (no resources, but faster).
            bool collect = GridInput.Mouse0;
            bool voidDig = GridInput.Mouse1 || Grid.DrillVoidMode;
            _isActive = collect || voidDig;
            if (!_isActive) return;

            // Void mode digs faster.
            float effectiveRate = voidDig && !collect ? drillRate * Mathf.Max(1f, voidSpeedMultiplier) : drillRate;
            _drillTimer += Time.deltaTime;
            if (_drillTimer < 1f / effectiveRate) return;
            _drillTimer = 0;

            MineForward(collect && !voidDig);
        }

        // Carve a sphere of terrain in front of the drill. When 'collect' is true the mined
        // ore is routed into the internal buffer (→ ship cargo); otherwise it is voided.
        private void MineForward(bool collect)
        {
            if (_world == null) _world = VoxelWorld.Instance;
            if (_registry == null) _registry = Object.FindAnyObjectByType<MaterialRegistry>();
            if (_world == null || _registry == null) return;

            float cs = Grid != null ? Grid.gridSize.CellSize() : 1f;
            Vector3 dir = transform.forward;

            // Find a SOLID voxel near the drill to carve. We scan a line of sample points
            // stepping outward from the drill face along its forward axis; the first point that
            // sits in solid terrain is the carve centre. This is orientation-tolerant and works
            // whether the drill is flush against a wall or a short reach away from it — it no
            // longer depends on a single raycast that often missed (the previous "mines nothing
            // even when powered" bug).
            Vector3 carveAt = Vector3.zero;
            bool found = false;
            int steps = Mathf.Max(2, Mathf.CeilToInt(drillReach / (cs * 0.5f)));
            for (int i = 0; i <= steps && !found; i++)
            {
                Vector3 p = transform.position + dir * (cs * 0.4f + i * (cs * 0.5f));
                var vp = _world.WorldToVoxel(p);
                var v = _world.GetVoxelWorld(vp);
                if (v.density > 0)
                {
                    var def = _registry.Get(v.material);
                    if (def == null || def.isMineable) { carveAt = p; found = true; }
                }
            }

            // Fallback: carve just ahead of the face even if our sample missed solid terrain
            // (e.g. drill buried inside terrain where every sample is past the surface).
            if (!found) carveAt = transform.position + dir * (cs * 0.5f);

            var res = VoxelEditor.SubtractCollect(_world, _registry, carveAt, drillRadius, drillStrength);
            if (!res.changed)
            {
                if (debugLog) Debug.Log($"[GridDrill] no terrain to carve. pos={transform.position} fwd={dir} carveAt={carveAt} found={found}");
                return;
            }
            if (debugLog) Debug.Log($"[GridDrill] carved terrain at {carveAt} (collect={collect})");

            if (!collect || res.drops == null) return; // void mode: ore is discarded

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
