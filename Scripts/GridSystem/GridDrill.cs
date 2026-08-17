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
        public float drillRadius = 1.45f;
        public float drillStrength = 80f;
        public float drillRate = 1.25f;
        [Tooltip("How far ahead of the drill we reach for terrain (metres).")]
        public float drillReach = 4.5f;
        [Tooltip("VOID-mode (RMB) is this many times faster than collect-mode (LMB).")]
        public float voidSpeedMultiplier = 2.25f;

        [Tooltip("Log why the drill isn't firing/mining (enable to diagnose).")]
        public bool debugLog = false;

        private VoxelEngine.Core.IVoxelWorld _world;
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

            if (!Enabled || Grid == null || !Grid.IsControlled || !Grid.IsSelectedTool(this))
            {
                if (debugLog && GridInput.Mouse0)
                    Debug.Log($"[GridDrill] not firing — Enabled={Enabled} Grid={(Grid!=null)} " +
                              $"IsControlled={Grid?.IsControlled} " +
                              $"SelectedTool={(Grid!=null && Grid.IsSelectedTool(this))} (group={Grid?.SelectedGroup})");
                _isActive = false;
                return;
            }

            // LMB = mine + collect; RMB = mine + VOID (no resources, but faster).
            bool collect = GridInput.Mouse0;
            bool voidDig = GridInput.Mouse1 || Grid.DrillVoidMode;
            _isActive = collect || voidDig;
            if (!_isActive) return;

            // LMB collect mode is intentionally slower and shallower-feeling; RMB void mode
            // can still chew faster for clearing tunnels.
            float lmbRate = Mathf.Min(drillRate, 1.35f);
            float effectiveRate = voidDig && !collect
                ? Mathf.Min(drillRate * Mathf.Max(1f, voidSpeedMultiplier), 3.0f)
                : lmbRate;
            _drillTimer += Time.deltaTime;
            if (_drillTimer < 1f / Mathf.Max(0.1f, effectiveRate)) return;
            _drillTimer = 0;

            MineForward(collect && !voidDig);
        }

        // Carve a sphere of terrain in front of the drill. When 'collect' is true the mined
        // ore is routed into the internal buffer (→ ship cargo); otherwise it is voided.
        private void MineForward(bool collect)
        {
            if (!ResolveVoxelReferences()) return;

            float cs = EffectiveCellSize;
            Vector3 dir = transform.forward.normalized;
            float effectiveRadius = Mathf.Clamp(drillRadius * 1.10f, 0.65f, 1.75f);
            float effectiveReach = Mathf.Clamp(Mathf.Max(drillReach, cs * 1.15f, effectiveRadius * 1.75f), 2.5f, 5.0f);
            float effectiveStrength = Mathf.Clamp(drillStrength, 45f, 90f);

            bool found = TryFindTerrainSurface(dir, effectiveRadius, effectiveReach, out Vector3 carveAt);
            if (!found)
            {
                found = TryFindSolidVoxelAhead(dir, cs, effectiveRadius, effectiveReach, out carveAt);
            }

            // Fallback: carve just ahead of the face if nothing was detected. This still lets
            // the brush bite into freshly-loaded terrain whose collider/mesh is a frame late.
            if (!found) carveAt = transform.position + dir * (cs * 0.75f);

            var res = VoxelEditor.SubtractCollect(_world, _registry, carveAt, effectiveRadius, effectiveStrength);

            // If the first chosen point did not change anything, sweep the full drill volume.
            // This makes drilling reliable even when the visual drill nose, voxel surface and
            // mesh collider disagree by a small amount.
            if (!res.changed)
            {
                res = SweepCarve(dir, cs, effectiveRadius, effectiveReach, effectiveStrength);
            }
            
            if (debugLog && !res.changed)
            {
                Debug.Log($"[GridDrill] no terrain to carve at {carveAt}. pos={transform.position} fwd={dir} found={found} reach={effectiveReach:0.0} radius={effectiveRadius:0.0} strength={effectiveStrength:0}");
            }

            CollectDrops(res, collect);
        }

        private bool ResolveVoxelReferences()
        {
            // 8.0.0: the flat world is removed — ActiveWorld.Current (the sphere) is the only world.
            if (_world == null) _world = VoxelEngine.Core.ActiveWorld.Current;
            if (_registry == null && _world != null) _registry = _world.MaterialRegistry;
            if (_registry == null) _registry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (_registry == null)
            {
                var registries = Resources.FindObjectsOfTypeAll<MaterialRegistry>();
                if (registries != null && registries.Length > 0) _registry = registries[0];
            }

            if (_world != null && _registry != null) return true;

            if (debugLog)
                Debug.Log($"[GridDrill] missing voxel refs — world={(_world != null)} registry={(_registry != null)}");
            return false;
        }

        private bool TryFindTerrainSurface(Vector3 dir, float effectiveRadius, float effectiveReach, out Vector3 carveAt)
        {
            carveAt = Vector3.zero;
            float cs = EffectiveCellSize;
            float castRadius = Mathf.Clamp(effectiveRadius * 0.35f, 0.15f, 0.85f);
            float castDistance = effectiveReach + cs;
            Vector3 origin = transform.position + dir * (cs * 0.15f);

            var hits = Physics.SphereCastAll(origin, castRadius, dir, castDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;

                // Do not drill our own ship or another grid block; this tool is for voxel terrain.
                var hitGrid = col.GetComponentInParent<GridEntity>();
                if (hitGrid != null) continue;

                carveAt = hits[i].point + dir * Mathf.Min(effectiveRadius * 0.35f, 0.9f);
                return true;
            }

            return false;
        }

        private bool TryFindSolidVoxelAhead(Vector3 dir, float cs, float effectiveRadius, float effectiveReach, out Vector3 carveAt)
        {
            carveAt = Vector3.zero;
            float stepSize = Mathf.Max(0.15f, cs * 0.20f);
            int steps = Mathf.Max(4, Mathf.CeilToInt(effectiveReach / stepSize));
            float sideOffset = Mathf.Max(0.15f, effectiveRadius * 0.45f);

            Vector3[] offsets =
            {
                Vector3.zero,
                transform.right * sideOffset,
                -transform.right * sideOffset,
                transform.up * sideOffset,
                -transform.up * sideOffset
            };

            for (int i = 0; i <= steps; i++)
            {
                float dist = cs * 0.2f + i * stepSize;
                for (int o = 0; o < offsets.Length; o++)
                {
                    Vector3 p = transform.position + offsets[o] + dir * dist;
                    var vp = _world.WorldToVoxel(p);
                    var v = _world.GetVoxelWorld(vp);
                    if (v.density <= 0) continue;

                    var def = _registry.Get(v.material);
                    if (def != null && !def.isMineable) continue;

                    carveAt = p;
                    return true;
                }
            }

            return false;
        }

        private VoxelEditor.EditResult SweepCarve(Vector3 dir, float cs, float effectiveRadius, float effectiveReach, float effectiveStrength)
        {
            var result = new VoxelEditor.EditResult();
            float stepSize = Mathf.Max(0.35f, effectiveRadius * 0.55f);
            int steps = Mathf.Max(4, Mathf.CeilToInt(effectiveReach / stepSize));
            float sideOffset = Mathf.Max(0.2f, effectiveRadius * 0.35f);

            Vector3[] offsets =
            {
                Vector3.zero,
                transform.right * sideOffset,
                -transform.right * sideOffset,
                transform.up * sideOffset,
                -transform.up * sideOffset
            };

            for (int i = 0; i <= steps; i++)
            {
                float dist = cs * 0.35f + i * stepSize;
                for (int o = 0; o < offsets.Length; o++)
                {
                    Vector3 p = transform.position + offsets[o] + dir * dist;
                    result = VoxelEditor.SubtractCollect(_world, _registry, p, effectiveRadius, effectiveStrength);
                    if (result.changed) return result;
                }
            }

            return result;
        }

        private void CollectDrops(VoxelEditor.EditResult res, bool collect)
        {
            if (!res.changed || !collect || res.drops == null) return;

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
