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

        /// <summary>Subtracts a smooth sphere of density at world position. Returns mined item totals.
        /// Drops are automatically granted to the local player's inventory.</summary>
        public static EditResult Subtract(VoxelWorld world, MaterialRegistry registry,
                                          Vector3 worldPos, float radius, float strength)
        {
            return Apply(world, registry, worldPos, radius, strength, subtract:true, autoGrant:true);
        }

        /// <summary>Subtracts a sphere but does NOT auto-grant drops to the player — the caller
        /// receives the mined item + amount in the result (used by ship drills that route ore
        /// into their own buffer / cargo network instead of the player's pockets).</summary>
        public static EditResult SubtractCollect(VoxelWorld world, MaterialRegistry registry,
                                                 Vector3 worldPos, float radius, float strength)
        {
            return Apply(world, registry, worldPos, radius, strength, subtract:true, autoGrant:false);
        }

        /// <summary>Adds material density (e.g. building/filling). 'fillMaterial' is what gets placed.</summary>
        public static EditResult Add(VoxelWorld world, MaterialRegistry registry,
                                     Vector3 worldPos, float radius, float strength,
                                     MaterialId fillMaterial)
        {
            return Apply(world, registry, worldPos, radius, strength, subtract:false, fillMaterial);
        }

        private static EditResult Apply(VoxelWorld world, MaterialRegistry registry,
                                        Vector3 worldPos, float radius, float strength,
                                        bool subtract, MaterialId fillMaterial = MaterialId.Stone,
                                        bool autoGrant = true)
        {
            var result = new EditResult();
            if (world == null) return result;

            Vector3Int center = world.WorldToVoxel(worldPos);
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
                    if (def != null && !def.isMineable) continue;

                    int newDensity = v.density - delta;
                    bool fullyRemoved = newDensity <= 0;

                    if (def != null && def.dropItem != null)
                        drops[v.material] += def.dropAmount; // simple: 1 drop per "hit voxel"

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
                // Mark a coarse 2-chunk radius around the brush dirty to be safe.
                int cs = VoxelConstants.CHUNK_SIZE;
                Vector3Int chunkCenter = new Vector3Int(
                    Mathf.FloorToInt(center.x / (float)cs),
                    Mathf.FloorToInt(center.y / (float)cs),
                    Mathf.FloorToInt(center.z / (float)cs));
                int chunkR = Mathf.CeilToInt(radius / cs) + 1;
                for (int z = -chunkR; z <= chunkR; z++)
                for (int y = -chunkR; y <= chunkR; y++)
                for (int x = -chunkR; x <= chunkR; x++)
                {
                    if (world.TryGetChunk(chunkCenter + new Vector3Int(x, y, z), out var ch) && ch.isGenerated)
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
            if (autoGrant) ApplyDrops(registry, drops);
            else           result.drops = drops; // hand the full breakdown back to the caller
            return result;
        }

        private static void ApplyDrops(MaterialRegistry registry, int[] drops)
        {
            // Find inventory on the local player (first one found).
            var inv = Object.FindAnyObjectByType<Inventory>();
            if (inv == null) return;
            for (int m = 1; m < 256; m++)
            {
                if (drops[m] <= 0) continue;
                var def = registry.Get((byte)m);
                if (def?.dropItem == null) continue;
                inv.Add(def.dropItem, drops[m]);
            }
        }
    }
}
