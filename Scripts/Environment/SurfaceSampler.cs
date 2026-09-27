// Assets/Scripts/VoxelEngine/Environment/SurfaceSampler.cs
//
// WHAT IS UNDER THIS CONTACT PATCH?
//
// One question, four possible answers, resolved in priority order because a wheel
// can stand on all four at once:
//   1. AsphaltRoad        — a built surface wins over whatever it was laid on, and
//                           carries its own wear multipliers from the road run.
//   2. Unity Terrain      — sample the alphamap at the hit point and take the
//                           dominant TerrainLayer.
//   3. Collider material  — a MeshCollider's PhysicsMaterial (or a SurfaceTag) names
//                           the surface directly.
//   4. Voxel world        — this engine's own MaterialId under the wheel, which is
//                           what a planet actually is when no Terrain exists.
//
// Cost control: alphamap reads are cached per terrain on a 1 m grid and collider
// lookups are cached by instance id, so a convoy of 5x5 rigs does not pay a managed
// array allocation per wheel per FixedUpdate. Nothing here allocates in the steady
// state.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Building;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.Environment
{
    /// <summary>Reads the ground under a wheel and returns its friction profile.</summary>
    public static class SurfaceSampler
    {
        private const float VoxelProbeDepth = 1.25f;

        // Caches key on the objects themselves, not on instance ids: Unity 6.5 deprecated
        // Object.GetInstanceID, and an object-keyed dictionary is also safer — an id can be
        // recycled after a destroy and hand the next collider a stale surface.
        private static readonly Dictionary<Collider, SurfaceProfile> s_colliderCache =
            new Dictionary<Collider, SurfaceProfile>(128);
        private static readonly Dictionary<(Terrain terrain, int mapX, int mapZ), SurfaceProfile> s_terrainCache =
            new Dictionary<(Terrain, int, int), SurfaceProfile>(256);
        private static float[,,] s_alphaScratch;
        private static int s_cacheFrame = -1;

        /// <summary>Optional explicit tag component; beats material-name guessing.</summary>
        public static SurfaceProfile TagOn(Collider collider)
        {
            if (collider == null) return null;
            var tag = collider.GetComponentInParent<SurfaceTag>();
            return tag != null ? tag.profile : null;
        }

        /// <summary>
        /// Resolves the surface for a wheel contact. <paramref name="road"/> is returned so
        /// the caller can bill the road run for traffic without a second lookup.
        /// </summary>
        public static SurfaceSample Sample(in RaycastHit hit, Vector3 up, out AsphaltRoad road)
        {
            road = null;
            var library = SurfaceProfileLibrary.Active;
            SurfaceProfile fallback = library != null ? library.Default : null;

            var collider = hit.collider;
            if (collider == null) return new SurfaceSample(fallback);

            PruneCachePerFrame();

            // 1) Built road surface.
            road = collider.GetComponentInParent<AsphaltRoad>();
            if (road != null && !road.IsSupported) road = null;
            if (road != null)
            {
                var roadProfile = ResolveName(library, road.surfaceKind.ToString(), fallback);
                return new SurfaceSample(roadProfile)
                    .Scaled(Mathf.Max(0.05f, road.TractionMultiplier), Mathf.Max(0.05f, road.GripMultiplier));
            }

            // 2) Explicit tag.
            var tagged = TagOn(collider);
            if (tagged != null) return new SurfaceSample(tagged);

            // 3) Unity Terrain alphamap.
            if (collider is TerrainCollider terrainCollider)
            {
                var terrain = terrainCollider.GetComponent<Terrain>();
                var sampled = SampleTerrain(library, terrain, hit.point, fallback);
                if (sampled != null) return new SurfaceSample(sampled);
            }

            // 4) Collider physics material / renderer material name.
            if (!s_colliderCache.TryGetValue(collider, out var cached) || cached == null)
            {
                cached = ResolveCollider(library, collider, fallback);
                s_colliderCache[collider] = cached;
            }
            if (cached != null && cached != fallback) return new SurfaceSample(cached);

            // 5) Voxel material beneath the contact point.
            var voxelProfile = SampleVoxel(library, hit.point, up, fallback);
            return new SurfaceSample(voxelProfile);
        }

        /// <summary>Surface under an arbitrary world point (used by foot movement and FX).</summary>
        public static SurfaceSample SampleAt(Vector3 worldPoint, Vector3 up)
        {
            var library = SurfaceProfileLibrary.Active;
            SurfaceProfile fallback = library != null ? library.Default : null;
            RoadSurfaceUtility.TryGetRoadBelow(worldPoint, up, 1.25f, out var road);
            if (road != null && road.IsSupported)
            {
                var profile = ResolveName(library, road.surfaceKind.ToString(), fallback);
                return new SurfaceSample(profile)
                    .Scaled(Mathf.Max(0.05f, road.TractionMultiplier), Mathf.Max(0.05f, road.GripMultiplier));
            }
            return new SurfaceSample(SampleVoxel(library, worldPoint, up, fallback));
        }

        // ── Terrain ─────────────────────────────────────────────────────────
        private static SurfaceProfile SampleTerrain(SurfaceProfileLibrary library, Terrain terrain,
            Vector3 worldPoint, SurfaceProfile fallback)
        {
            if (terrain == null || terrain.terrainData == null) return null;
            var data = terrain.terrainData;
            if (data.alphamapLayers <= 0 || data.terrainLayers == null || data.terrainLayers.Length == 0) return null;

            Vector3 local = worldPoint - terrain.transform.position;
            float u = Mathf.Clamp01(local.x / Mathf.Max(0.001f, data.size.x));
            float v = Mathf.Clamp01(local.z / Mathf.Max(0.001f, data.size.z));

            int mapX = Mathf.Clamp(Mathf.RoundToInt(u * (data.alphamapWidth - 1)), 0, Mathf.Max(0, data.alphamapWidth - 1));
            int mapZ = Mathf.Clamp(Mathf.RoundToInt(v * (data.alphamapHeight - 1)), 0, Mathf.Max(0, data.alphamapHeight - 1));

            var cacheKey = (terrain, mapX, mapZ);
            if (s_terrainCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            s_alphaScratch = data.GetAlphamaps(mapX, mapZ, 1, 1);
            int dominant = 0;
            float best = -1f;
            int layers = Mathf.Min(data.alphamapLayers, s_alphaScratch.GetLength(2));
            for (int i = 0; i < layers; i++)
            {
                float weight = s_alphaScratch[0, 0, i];
                if (weight > best) { best = weight; dominant = i; }
            }

            var layer = dominant < data.terrainLayers.Length ? data.terrainLayers[dominant] : null;
            string layerName = layer != null
                ? (layer.diffuseTexture != null ? layer.diffuseTexture.name : layer.name)
                : string.Empty;

            var resolved = ResolveName(library, layerName, fallback);
            s_terrainCache[cacheKey] = resolved;
            return resolved;
        }

        // ── Colliders ───────────────────────────────────────────────────────
        private static SurfaceProfile ResolveCollider(SurfaceProfileLibrary library, Collider collider, SurfaceProfile fallback)
        {
            var material = collider.sharedMaterial;
            if (material != null)
            {
                var byMaterial = ResolveName(library, material.name, fallback);
                if (byMaterial != fallback) return byMaterial;

                // No keyword match: derive multipliers straight from the physics material
                // so an unregistered surface still behaves like the value an artist set.
                var derived = ScriptableObject.CreateInstance<SurfaceProfile>();
                derived.hideFlags = HideFlags.HideAndDontSave;
                derived.name = $"Surface_{material.name}";
                derived.surfaceName = material.name;
                derived.forwardFriction = Mathf.Clamp(material.dynamicFriction / 0.6f, 0.05f, 2f);
                derived.lateralGrip = Mathf.Clamp(material.staticFriction / 0.6f, 0.05f, 2f);
                derived.steeringResponse = Mathf.Clamp(0.6f + material.staticFriction * 0.6f, 0.2f, 1.6f);
                derived.rollingResistance = 0.02f;
                return derived;
            }

            var renderer = collider.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                return ResolveName(library, renderer.sharedMaterial.name, fallback);

            return fallback;
        }

        // ── Voxel world ─────────────────────────────────────────────────────
        private static SurfaceProfile SampleVoxel(SurfaceProfileLibrary library, Vector3 worldPoint, Vector3 up,
            SurfaceProfile fallback)
        {
            var world = ActiveWorld.Current;
            if (world == null) return fallback;
            if (up.sqrMagnitude < 0.0001f) up = Vector3.up;
            up.Normalize();

            int samples = 5;
            for (int i = 0; i <= samples; i++)
            {
                Vector3 probe = worldPoint - up * (VoxelProbeDepth * (i / (float)samples));
                var voxel = world.GetVoxelWorld(world.WorldToVoxel(probe));
                if (voxel.density <= VoxelConstants.ISO_LEVEL) continue;
                if (voxel.material == (byte)MaterialId.Air) continue;
                return library != null ? library.ResolveVoxel(voxel.material) : fallback;
            }
            return fallback;
        }

        private static SurfaceProfile ResolveName(SurfaceProfileLibrary library, string name, SurfaceProfile fallback)
            => library != null ? library.ResolveName(name) : fallback;

        /// <summary>Caches are cheap but must not outlive a world reload or a repaint.</summary>
        private static void PruneCachePerFrame()
        {
            int frame = Time.frameCount;
            if (frame == s_cacheFrame) return;
            s_cacheFrame = frame;
            if (s_colliderCache.Count > 512) s_colliderCache.Clear();
            if (s_terrainCache.Count > 4096) s_terrainCache.Clear();
        }

        /// <summary>Called when the library is rebuilt so stale profiles are not served.</summary>
        public static void ClearCaches()
        {
            s_colliderCache.Clear();
            s_terrainCache.Clear();
        }
    }

    /// <summary>
    /// Drop this on any collider (or a parent) to name its surface explicitly. Beats
    /// material-name guessing and costs nothing at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class SurfaceTag : MonoBehaviour
    {
        [Tooltip("Friction profile this collider hierarchy reports to wheels.")]
        public SurfaceProfile profile;
    }
}
