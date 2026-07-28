using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Items;

namespace VoxelEngine.WaterSim
{
    /// <summary>
    /// Crest Voxel Block Foam – v3.20.0
    /// Scans nearby voxel water surface for intersecting solid blocks and spawns
    /// Crest SphereWaterInteraction components to generate shoreline foam.
    /// Eliminates the need for per-chunk water meshes to show foam.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class VoxelCrestBlockFoamEmitter : MonoBehaviour
    {
        [Header("Scan")]
        public Transform followTarget;
        [Range(16f, 256f)] public float scanRadius = 96f;
        [Range(0.2f, 2f)] public float scanInterval = 0.5f;
        [Range(4, 64)] public int maxEmitters = 32;
        public LayerMask blockMask = ~0;

        [Header("Foam Tuning")]
        [Range(0.5f, 4f)] public float emitterRadius = 1.8f;
        [Range(0.1f, 2f)] public float weight = 1.25f;
        public bool addToPlacedBlocksOnly = true;

        private float _nextScan;
        private readonly List<SphereFoamProxy> _pool = new();
        private readonly List<Vector3> _foamPoints = new(128);

        class SphereFoamProxy
        {
            public GameObject go;
            public Component sphereInteraction;
            public float lastUsed;
        }

        private void OnEnable()
        {
            _nextScan = 0f;
        }

        private void Update()
        {
            if (Time.time < _nextScan) return;
            _nextScan = Time.time + scanInterval;
            ScanAndEmit();
        }

        private Transform GetFollow()
        {
            if (followTarget != null) return followTarget;
            var world = ActiveWorld.Current;
            if (world != null && world.Viewer != null) return world.Viewer;
            var cam = Camera.main;
            return cam != null ? cam.transform : transform;
        }

        private void ScanAndEmit()
        {
            var target = GetFollow();
            if (target == null) return;
            var world = ActiveWorld.Current;
            if (world == null) return;

            _foamPoints.Clear();

            Vector3 center = target.position;
            int r = Mathf.CeilToInt(scanRadius / VoxelConstants.VOXEL_SIZE);
            Vector3Int cVox = world.WorldToVoxel(center);

            // Sparse grid scan – check every 2 voxels to save CPU
            for (int z = -r; z <= r; z += 2)
            for (int y = -8; y <= 8; y += 2)
            for (int x = -r; x <= r; x += 2)
            {
                if (x * x + z * z > r * r) continue;
                var vp = cVox + new Vector3Int(x, y, z);
                var v = world.GetVoxelWorld(vp);
                if (!v.IsSolid) continue;
                if (addToPlacedBlocksOnly && v.density < 100) continue; // crude placed-block heuristic (sbyte max 127)

                // Is this solid touching water?
                bool touchesWater = false;
                foreach (var n in _nbrs)
                {
                    var nv = world.GetVoxelWorld(vp + n);
                    if (FluidMaterialUtility.IsFluid(nv))
                    {
                        touchesWater = true;
                        break;
                    }
                }
                if (!touchesWater) continue;

                Vector3 worldPos = ((Vector3)vp + new Vector3(0.5f, 0.5f, 0.5f)) * VoxelConstants.VOXEL_SIZE;
                _foamPoints.Add(worldPos);
                if (_foamPoints.Count >= maxEmitters * 2) break;
            }

            // Activate pool emitters
            int useCount = Mathf.Min(_foamPoints.Count, maxEmitters);
            EnsurePoolCapacity(useCount);

            for (int i = 0; i < _pool.Count; i++)
            {
                var p = _pool[i];
                bool active = i < useCount;
                if (p.go.activeSelf != active) p.go.SetActive(active);
                if (active)
                {
                    p.go.transform.position = _foamPoints[i];
                    p.lastUsed = Time.time;
                    // Try push radius/weight via reflection to stay version-agnostic
                    TryConfigureSphere(p.sphereInteraction, emitterRadius, weight);
                }
            }
        }

        private static readonly Vector3Int[] _nbrs =
        {
            Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down, Vector3Int.forward, Vector3Int.back
        };

        private void EnsurePoolCapacity(int needed)
        {
            while (_pool.Count < needed)
            {
                var go = new GameObject("CrestVoxelFoamEmitter_pooled");
                go.transform.SetParent(transform, true);
                go.SetActive(false);

                Component sphere = null;
                var sphereType = System.Type.GetType("Crest.SphereWaterInteraction, Crest");
                if (sphereType != null)
                    sphere = go.AddComponent(sphereType);

                // Fallback visual – small sphere so we can see it in editor if Crest missing
                var sf = go.AddComponent<SphereCollider>();
                sf.isTrigger = true;
                sf.radius = 0.5f;

                _pool.Add(new SphereFoamProxy { go = go, sphereInteraction = sphere, lastUsed = 0f });
            }
        }

        private static void TryConfigureSphere(Component sphere, float radius, float weight)
        {
            if (sphere == null) return;
            var t = sphere.GetType();
            var radiusField = t.GetField("_radius", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (radiusField != null) radiusField.SetValue(sphere, radius);
            var weightField = t.GetField("_weight", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (weightField != null) weightField.SetValue(sphere, weight);
            var velField = t.GetField("_velocity", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (velField != null) velField.SetValue(sphere, 0.5f);
        }

        private void OnDisable()
        {
            foreach (var p in _pool)
                if (p.go != null) p.go.SetActive(false);
        }
    }
}
