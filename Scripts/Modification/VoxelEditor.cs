// Assets/Scripts/VoxelEngine/Modification/VoxelEditor.cs
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.Modification
{
    /// <summary>
    /// Spherical voxel "brush" — used by both pickaxes (subtract) and build tools (add).
    /// Returns the items that should be granted to the player when subtracting.
    /// </summary>
    public static class VoxelEditor
    {
        // One warning per material per session for an incomplete mining-drop definition.
        private static readonly bool[] _warnedNoDrop = new bool[256];
        private static readonly bool[] _warnedNoDefinition = new bool[256];
        private static readonly bool[] _warnedInvalidDropAmount = new bool[256];
        private static bool _warnedMissingInventory;

        public struct EditResult
        {
            public bool changed;
            public ItemDefinition primaryItem;
            public int            primaryAmount;
            /// <summary>Per-material drop counts (index = material id). Only populated when the
            /// caller used <see cref="SubtractCollect"/> (autoGrant:false) so it can route the
            /// drops itself; null otherwise.</summary>
            public int[]          drops;
        }

        /// <summary>Subtracts a smooth sphere at world position. Drops are granted to the supplied
        /// player inventory; overflow uses Inventory's world-drop path, and a missing inventory
        /// falls back to spawning the mined items at the excavation point.</summary>
        public static EditResult Subtract(IVoxelWorld world, MaterialRegistry registry,
                                          Vector3 worldPos, float radius, float strength,
                                          Inventory dropRecipient = null)
        {
            return Apply(world, registry, worldPos, radius, strength, subtract:true,
                autoGrant:true, dropRecipient:dropRecipient);
        }

        /// <summary>Subtracts a sphere but does NOT auto-grant drops to the player — the caller
        /// receives the mined item + amount in the result (used by ship drills that route ore
        /// into their own buffer / cargo network instead of the player's pockets).</summary>
        public static EditResult SubtractCollect(IVoxelWorld world, MaterialRegistry registry,
                                                 Vector3 worldPos, float radius, float strength)
        {
            return Apply(world, registry, worldPos, radius, strength, subtract:true, autoGrant:false);
        }

        /// <summary>Adds material density (e.g. building/filling). 'fillMaterial' is what gets placed.</summary>
        public static EditResult Add(IVoxelWorld world, MaterialRegistry registry,
                                     Vector3 worldPos, float radius, float strength,
                                     MaterialId fillMaterial)
        {
            return Apply(world, registry, worldPos, radius, strength, subtract:false, fillMaterial);
        }

        /// <summary>Pick the nearest solid lattice sample on the inside of the hit.
        /// Surface nets can lie up to a cell away from the floor-rounded hit coordinate.</summary>
        public static bool TryResolveMiningPoint(IVoxelWorld world, Vector3 hit, Vector3 normal,
            Vector3 rayDirection, out Vector3 point)
        {
            point = hit + rayDirection.normalized * 0.35f;
            if (world == null) return false;
            var sphere = world as VoxelEngine.Cosmos.SphereWorld;
            Vector3 localHit = sphere != null && sphere.body != null
                ? sphere.body.transform.InverseTransformPoint(hit) / VoxelConstants.VOXEL_SIZE : hit / VoxelConstants.VOXEL_SIZE;
            Vector3 localNormal = sphere != null && sphere.body != null
                ? sphere.body.transform.InverseTransformDirection(normal).normalized : normal.normalized;
            Vector3Int center = Vector3Int.RoundToInt(localHit);
            float best = float.PositiveInfinity;
            Vector3Int chosen = default;
            for (int z=-2;z<=2;z++) for(int y=-2;y<=2;y++) for(int x=-2;x<=2;x++)
            {
                Vector3Int q = center + new Vector3Int(x,y,z);
                Vector3 delta = (Vector3)q - localHit;
                if (delta.sqrMagnitude > 3.1f || Vector3.Dot(delta, localNormal) > 0.35f) continue;
                Voxel voxel;
                if (sphere != null) { if (!sphere.TryGetVoxelReady(q, out voxel)) continue; }
                else voxel = world.GetVoxelWorld(q);
                if (!voxel.IsSolid) continue;
                float score = delta.sqrMagnitude;
                if (score >= best) continue;
                best = score; chosen = q;
            }
            if (float.IsPositiveInfinity(best)) return false;
            // Small interior offset makes WorldToVoxel(floor) recover this exact lattice index.
            Vector3 local = ((Vector3)chosen + Vector3.one * 0.01f) * VoxelConstants.VOXEL_SIZE;
            point = sphere != null && sphere.body != null ? sphere.body.transform.TransformPoint(local) : local;
            return true;
        }

        private static EditResult Apply(IVoxelWorld world, MaterialRegistry registry,
                                        Vector3 worldPos, float radius, float strength,
                                        bool subtract, MaterialId fillMaterial = MaterialId.Stone,
                                        bool autoGrant = true, Inventory dropRecipient = null)
        {
            if (world == null) return new EditResult();
            return ApplyAt(world, registry, world.WorldToVoxel(worldPos), radius, strength,
                subtract, fillMaterial, autoGrant, announce: true,
                dropRecipient:dropRecipient, dropPosition:worldPos);
        }

        /// <summary>Remote replication entry (14.7.0): the identical brush at an exact
        /// voxel center - integer voxel space is floating-origin-proof - with no drops
        /// granted and no re-announce. Same-seed worlds converge exactly.</summary>
        public static void ApplyReplicated(IVoxelWorld world, MaterialRegistry registry,
                                           Vector3Int centerVoxel, float radius, float strength,
                                           bool subtract, MaterialId fillMaterial)
        {
            if (world == null) return;
            ApplyAt(world, registry, centerVoxel, radius, strength, subtract, fillMaterial,
                autoGrant: false, announce: false, dropRecipient: null, dropPosition: default);
        }

        private static EditResult ApplyAt(IVoxelWorld world, MaterialRegistry registry,
                                          Vector3Int center, float radius, float strength,
                                          bool subtract, MaterialId fillMaterial,
                                          bool autoGrant, bool announce,
                                          Inventory dropRecipient, Vector3 dropPosition)
        {
            var result = new EditResult();
            if (subtract && registry == null)
            {
                Debug.LogError("[VoxelEditor] Mining was refused because the active voxel world has no MaterialRegistry; terrain was left unchanged.");
                return result;
            }
            int r = Mathf.CeilToInt(radius);
            float r2 = radius * radius;

            // Tally drops by material so we add to the inventory once per material.
            var drops = new int[256];

            for (int dz = -r; dz <= r; dz++)
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                Vector3 offset = new Vector3(dx, dy, dz);
                float d2 = offset.sqrMagnitude;
                if (d2 > r2) continue;

                var coord = new Vector3Int(center.x + dx, center.y + dy, center.z + dz);
                var v = world.GetVoxelWorld(coord);

                // Falloff: full strength at center, zero at edge.
                float falloff = 1f - Mathf.Sqrt(d2) / radius;
                int delta = Mathf.CeilToInt(strength * falloff);
                if (delta <= 0) continue;

                if (subtract)
                {
                    if (v.density <= 0) continue;
                    var def = registry.Get(v.material);
                    // 9.16.0 — solid rock soaked with a FLUID material (oil bore/reservoir
                    // casing, oil-soaked seep floors) is always diggable: the fluid
                    // definition's non-mineable flag must not armour the rock around it.
                    bool soakedRock = v.IsSolid
                        && VoxelEngine.WaterSim.FluidMaterialUtility.IsFluidMaterial(v.material);
                    if (def != null && !def.isMineable && !soakedRock) continue;

                    int newDensity = v.density - delta;
                    bool fullyRemoved = newDensity <= 0;

                    // 14.62.0 — yield is credited ONLY when a voxel is FULLY removed.
                    // The old per-hit credit paid dropAmount for every voxel the brush
                    // merely grazed, every swing: the stone rim of the brush re-dropped
                    // endlessly while the ore pocket at the centre credited once, so a
                    // mined vein read as "it just gives stone". Yield now equals voxels
                    // actually destroyed, per material — ore pockets pay in ore.
                    if (fullyRemoved)
                    {
                        if (def == null)
                        {
                            if (!_warnedNoDefinition[v.material])
                            {
                                _warnedNoDefinition[v.material] = true;
                                Debug.LogWarning($"[VoxelEditor] Removed voxel material id {v.material} has no VoxelMaterialDefinition in the active registry; no drop can be resolved. Run the material/drop-link Setup repair.");
                            }
                        }
                        else if (def.dropItem == null)
                        {
                            if (def.isMineable && !_warnedNoDrop[v.material])
                            {
                                _warnedNoDrop[v.material] = true;
                                Debug.LogWarning($"[VoxelEditor] Material '{def.displayName}' (id {v.material}) is mineable but has no dropItem — it mines to nothing. Run the material/drop-link Setup repair.");
                            }
                        }
                        else if (def.dropAmount <= 0)
                        {
                            if (def.isMineable && !_warnedInvalidDropAmount[v.material])
                            {
                                _warnedInvalidDropAmount[v.material] = true;
                                Debug.LogWarning($"[VoxelEditor] Material '{def.displayName}' (id {v.material}) has non-positive dropAmount {def.dropAmount}; its authored amount was preserved and no item was awarded.");
                            }
                        }
                        else
                        {
                            drops[v.material] += def.dropAmount;
                        }
                    }

                    if (fullyRemoved)
                    {
                        world.SetVoxelWorld(coord, new Voxel(-127, (byte)MaterialId.Air), remesh:false);
                    }
                    else
                    {
                        world.SetVoxelWorld(coord, new Voxel((sbyte)newDensity, v.material), remesh:false);
                    }
                    result.changed = true;
                }
                else
                {
                    int newDensity = v.density + delta;
                    if (newDensity > 127) newDensity = 127;
                    byte mat = v.density > 0 ? v.material : (byte)fillMaterial;
                    world.SetVoxelWorld(coord, new Voxel((sbyte)newDensity, mat), remesh:false);
                    result.changed = true;
                }
            }

            // Trigger one remesh pass for affected chunks.
            if (result.changed)
            {
                // 9.16.0 flow remake — terrain edits now WAKE the liquid sim instead of
                // pruning it. The old legacy dry-cave prune used to run here and DELETED
                // surrounding lake/stream water on every mining operation; it is gone.
                // Mining next to water now makes the water rush into the new space the
                // same frame, and building in a pool re-levels it instantly.
                VoxelEngine.WaterSim.FluidManager.Instance?.NotifyVoxelEdited(center, r + 1);
                // 9.18.2 - one bump per player edit batch: the GPU grass field (and any
                // other surface-caching visual) rebuilds exactly when the player changes
                // terrain, instead of waiting for them to walk 12 m.
                (world as VoxelEngine.Cosmos.SphereWorld)?.NotifyPlayerEdit(center, r);
                // Surface nets and border padding depend on one cell beyond the brush.
                // Floor both bounds: this also handles negative coordinates correctly.
                int cs = VoxelConstants.CHUNK_SIZE;
                Vector3Int min = Vector3Int.FloorToInt((Vector3)(center - Vector3Int.one * (r + 1)) / cs);
                Vector3Int max = Vector3Int.FloorToInt((Vector3)(center + Vector3Int.one * (r + 1)) / cs);
                for (int z = min.z; z <= max.z; z++)
                for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                {
                    if (world.TryGetChunk(new Vector3Int(x, y, z), out var ch) && ch.isGenerated)
                        world.ScheduleMeshJob(ch);
                }
            }

            // Compute primary item drop (for HUD popup) — pick the most-dropped one.
            int best = 0;
            for (int m = 1; m < 256; m++)
            {
                if (drops[m] > best)
                {
                    best = drops[m];
                    var def = registry.Get((byte)m);
                    result.primaryItem = def?.dropItem;
                    result.primaryAmount = drops[m];
                }
            }
            if (autoGrant)
            {
                if (registry != null) ApplyDrops(registry, drops, dropRecipient, dropPosition);
            }
            else
            {
                result.drops = drops; // hand the full breakdown back to the caller
            }

            if (result.changed && subtract)
            {
                VoxelEngine.Scattering.ScatterClear.ClearInBox(
                    dropPosition,
                    Vector3.one * Mathf.Max(0.75f, radius * 0.65f),
                    Quaternion.identity);
            }

            // Multiplayer: every brush that changed terrain replicates (14.7.0).
            if (announce && result.changed)
                VoxelEngine.Networking.TerrainSync.AnnounceBrush(world, center, radius,
                    strength, subtract, (byte)fillMaterial);
            return result;
        }

        private static void ApplyDrops(MaterialRegistry registry, int[] drops,
                                       Inventory preferredInventory, Vector3 dropPosition)
        {
            // Mining has the player's exact Inventory reference. Keep a scene fallback
            // for callers such as vehicle terrain damage that do not originate on the player.
            var inventory = preferredInventory != null
                ? preferredInventory
                : Object.FindAnyObjectByType<Inventory>();
            if (inventory != null)
            {
                for (int m = 1; m < 256; m++)
                {
                    if (drops[m] <= 0) continue;
                    var def = registry.Get((byte)m);
                    if (def?.dropItem == null) continue;
                    inventory.Add(def.dropItem, drops[m]);
                }
                return;
            }

            // Never silently destroy mined yield merely because an inventory reference
            // is unavailable; leave the items at the excavation site instead.
            if (!_warnedMissingInventory)
            {
                _warnedMissingInventory = true;
                Debug.LogWarning("[VoxelEditor] No player Inventory was available for mining drops; spawned them at the excavation point.");
            }
            for (int m = 1; m < 256; m++)
            {
                if (drops[m] <= 0) continue;
                var def = registry.Get((byte)m);
                if (def?.dropItem == null) continue;
                DroppedItem.Spawn(new ItemStack(def.dropItem, drops[m]),
                    dropPosition + Vector3.up * 0.45f, Vector3.up);
            }
        }
    }
}
