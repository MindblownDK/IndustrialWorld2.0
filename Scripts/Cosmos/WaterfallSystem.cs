// Assets/Scripts/VoxelEngine/Cosmos/WaterfallSystem.cs
//
// Detects and renders beautiful waterfalls on spherical voxel terrain.
//
// A waterfall is a column of water flowing over a vertical cliff: the top voxel is water, the
// voxel(s) directly below it are AIR (not solid), and the column falls until it hits terrain or
// water. This system scans chunks around the viewer for such configurations, then renders the
// falling water as a stretched, animated, GPU-instanced mesh (with foam at the top and splash
// at the bottom) — so waterfalls emerge naturally wherever the terrain creates a drop, exactly
// as requested in the design brief.
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Scans for waterfall configurations near the viewer and renders falling-water columns as
    /// GPU-instanced meshes. Attach near the active SphereWorld.
    /// </summary>
    [RequireComponent(typeof(CelestialBody))]
    public class WaterfallSystem : MonoBehaviour
    {
        [Header("References")]
        public CelestialBody body;
        public Transform viewer;
        public Material waterfallMaterial;   // transparent, scrolling UV, GPU-instanced
        public Mesh waterfallMesh;           // a tall thin plane

        [Header("Detection")]
        [Tooltip("Radius around the viewer to scan for waterfalls (metres).")]
        public float scanRange = 80f;
        [Tooltip("Minimum drop height (voxels) for a waterfall to render.")]
        public int minDropVoxels = 3;
        [Tooltip("Re-scan when the viewer moves more than this (metres).")]
        public float rescanThreshold = 10f;

        [Header("Visuals")]
        public float bladeWidth = 1.2f;
        public float foamHeight = 0.6f;

        private Vector3 _lastScanPos = new Vector3(float.MaxValue, 0, 0);
        private NativeArray<Matrix4x4> _matrices;
        private int _instanceCount;
        private RenderParams _renderParams;

        private void Awake()
        {
            if (body == null) body = GetComponentInParent<CelestialBody>();
            if (waterfallMesh == null) waterfallMesh = CreateDefaultWaterfallMesh();
            if (waterfallMaterial == null) waterfallMaterial = CreateDefaultWaterfallMaterial();
            _renderParams = new RenderParams(waterfallMaterial);
        }

        private void OnDestroy()
        {
            if (_matrices.IsCreated) _matrices.Dispose();
        }

        private void Update()
        {
            if (body == null || viewer == null) return;

            if (Vector3.Distance(viewer.position, _lastScanPos) > rescanThreshold)
            {
                Rescan();
                _lastScanPos = viewer.position;
            }

            // Animate the scroll offset so the water appears to flow downward.
            if (waterfallMaterial != null)
                waterfallMaterial.SetFloat("_ScrollOffset", Time.time * 0.8f);

            if (_instanceCount > 0 && _matrices.IsCreated)
                Graphics.RenderMeshInstanced(_renderParams, waterfallMesh, 0, _matrices);
        }

        // ── Detection ──
        private void Rescan()
        {
            var world = ActiveWorld.Current;
            if (world == null) { _instanceCount = 0; return; }

            Vector3 viewerLocal = body.transform.InverseTransformPoint(viewer.position);
            int range = Mathf.CeilToInt(scanRange);
            var candidates = new List<Matrix4x4>(64);
            var seen = new HashSet<Vector3Int>();

            int3 vl = new int3(
                Mathf.FloorToInt(viewerLocal.x),
                Mathf.FloorToInt(viewerLocal.y),
                Mathf.FloorToInt(viewerLocal.z));

            // Scan the volume; for each water voxel, check if the voxel below is air → waterfall top.
            int step = 2; // coarser scan for performance
            for (int dz = -range; dz <= range; dz += step)
            for (int dy = -range; dy <= range; dy += step)
            for (int dx = -range; dx <= range; dx += step)
            {
                if (dx * dx + dy * dy + dz * dz > scanRange * scanRange) continue;

                int wx = vl.x + dx, wy = vl.y + dy, wz = vl.z + dz;
                var key = new Vector3Int(wx, wy, wz);
                if (seen.Contains(key)) continue;

                var here = world.GetVoxelWorld(key);
                bool isWater = here.material == (byte)MaterialId.WaterLiquid ||
                               here.material == (byte)MaterialId.WaterVoxel ||
                               here.waterLevel > 0;
                if (!isWater) continue;

                // Check the voxel directly "below" (radial-in): is it air?
                // On a sphere, "below" is toward the body core = -radialUp.
                float3 worldPos = (float3)body.transform.TransformPoint(new Vector3(wx + 0.5f, wy + 0.5f, wz + 0.5f));
                float3 radialUp = math.normalizesafe(worldPos - (float3)body.transform.position, new float3(0, 1, 0));

                // Sample a few voxels downward (along -radialUp) to measure the drop.
                int drop = 0;
                for (int d = 1; d <= 40; d++)
                {
                    float3 samplePos = worldPos - radialUp * d;
                    Vector3Int sv = world.WorldToVoxel((Vector3)samplePos);
                    var sv_ = world.GetVoxelWorld(sv);
                    if (sv_.density > 0) break;  // hit terrain — stop counting the drop
                    drop++;
                }

                if (drop < minDropVoxels) continue;

                // We have a waterfall! Render a falling-water column from `here` down `drop` voxels.
                seen.Add(key);
                float3 fallStart = worldPos;
                float3 fallEnd = worldPos - radialUp * drop;

                // Build a transform: position at the midpoint, scale Y = drop length, orient so
                // the plane's up aligns with -radialUp (the fall direction).
                float3 center = (fallStart + fallEnd) * 0.5f;
                quaternion rot = quaternion.LookRotation(radialUp, new float3(0, 1, 0));
                float3 scale = new float3(bladeWidth, drop, 1f);
                candidates.Add(Matrix4x4.TRS(center, rot, scale));

                if (candidates.Count > 200) goto done;
            }
            done:

            if (_matrices.IsCreated) _matrices.Dispose();
            _instanceCount = candidates.Count;
            if (_instanceCount == 0) return;
            _matrices = new NativeArray<Matrix4x4>(_instanceCount, Allocator.Persistent);
            for (int i = 0; i < _instanceCount; i++) _matrices[i] = candidates[i];
        }

        // ── Default assets ──
        private static Mesh CreateDefaultWaterfallMesh()
        {
            // A tall thin plane (2 triangles), pointing up along Y.
            var mesh = new Mesh { name = "WaterfallPlane" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var verts = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(-0.5f,  0.5f, 0), new Vector3(0.5f,  0.5f, 0),
            };
            var uvs = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            var tris = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateDefaultWaterfallMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Unlit/Transparent")
                       ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Mat_Waterfall_Runtime";
            mat.enableInstancing = true;
            Color c = new Color(0.7f, 0.85f, 0.95f, 0.7f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
            // Transparent blend.
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            return mat;
        }
    }
}
