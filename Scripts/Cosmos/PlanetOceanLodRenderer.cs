// Assets/Scripts/VoxelEngine/Cosmos/PlanetOceanLodRenderer.cs
//
// Whole-planet ocean LOD sampled from the same radial terrain density as the
// voxel world. It renders triangles only where the terrain is a true ocean
// basin — never a complete mathematical water sphere — and cuts itself out
// around the local streamed water chunks.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Biomes;
using VoxelEngine.Core;

namespace VoxelEngine.Cosmos
{
    [ExecuteAlways]
    public sealed class PlanetOceanLodRenderer : MonoBehaviour
    {
        public CelestialBody body;
        public BiomeRegistry biomeRegistry;
        public Transform viewer;
        [Range(642, 10242)] public int resolution = 2562;
        [Range(0.05f, 2f)] public float surfaceInset = 0.35f;
        [Range(0f, 128f)] public float localCutoutPadding = 0f;

        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;
        private NativeArray<BiomeData> _biomes;
        private int _lastResolution;
        private int _lastSeed;

        private void OnEnable()
        {
            EnsureComponents();
            EnsureMaterial();
            Rebuild();
        }

        private void OnDisable()
        {
            if (_biomes.IsCreated) _biomes.Dispose();
            _biomes = default;
        }

        private void Update()
        {
            var resolved = ResolveBody();
            if (resolved == null || resolved.settings == null) return;
            int effective = ResolveRuntimeResolution(resolved);
            if (!_biomes.IsCreated || _lastResolution != effective || _lastSeed != resolved.genParams.seed)
                Rebuild(effective);
            UpdateMaterial(resolved);
        }

        private CelestialBody ResolveBody()
        {
            if (body != null && body.settings != null) return body;
            foreach (var candidate in GetComponentsInParent<CelestialBody>(true))
            {
                if (candidate == null || candidate.settings == null) continue;
                body = candidate;
                return body;
            }
            return body;
        }

        private int ResolveRuntimeResolution(CelestialBody resolved)
        {
            int highest = Mathf.Clamp(resolution, 642, 10242);
            // The capped 10k proxy is inexpensive enough to retain its authored detail in
            // orbit. Lower quality tiers still use their own lower ceiling via GraphicsPreset.
            return highest;
        }

        private void EnsureComponents()
        {
            _filter ??= GetComponent<MeshFilter>();
            _renderer ??= GetComponent<MeshRenderer>();
            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();
            foreach (var collider in GetComponents<Collider>())
                if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider);
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;
            Shader shader = Shader.Find("VoxelEngine/PlanetOceanLodURP")
                          ?? Shader.Find("Universal Render Pipeline/Unlit")
                          ?? Shader.Find("Universal Render Pipeline/Lit")
                          ?? Shader.Find("Standard");
            _material = new Material(shader) { name = "Mat_PlanetOceanLOD_Runtime" };
            if (_material.HasProperty("_DeepColor")) _material.SetColor("_DeepColor", new Color(0.015f, 0.07f, 0.24f, 0.95f));
            if (_material.HasProperty("_ShallowColor")) _material.SetColor("_ShallowColor", new Color(0.10f, 0.45f, 0.78f, 0.82f));
            _material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_ZWrite", 0);
            if (_renderer != null) _renderer.sharedMaterial = _material;
        }

        private void Rebuild(int targetResolution = -1)
        {
            var resolved = ResolveBody();
            if (resolved == null || resolved.settings == null) return;
            resolved.ApplySettings();
            if (targetResolution <= 0) targetResolution = ResolveRuntimeResolution(resolved);

            BiomeData[] authored = resolved.BuildBiomeData(biomeRegistry);
            if (_biomes.IsCreated) _biomes.Dispose();
            _biomes = new NativeArray<BiomeData>(authored.Length, Allocator.Persistent);
            for (int i = 0; i < authored.Length; i++) _biomes[i] = authored[i];

            var directions = new List<Vector3>(IcosahedronVerts());
            var allTriangles = new List<int>(IcosahedronTriangles());
            int subdivision = 0;
            while (directions.Count < targetResolution && subdivision < 6)
            {
                Subdivide(directions, allTriangles);
                subdivision++;
            }

            var vertices = new Vector3[directions.Count];
            var normals = new Vector3[directions.Count];
            var colors = new Color[directions.Count];
            var ocean = new bool[directions.Count];
            float seaRadius = resolved.genParams.seaRadius;

            for (int i = 0; i < directions.Count; i++)
            {
                Vector3 dir = directions[i].normalized;
                SphereDensity.EvaluateColumn(resolved.genParams, _biomes, (float3)dir, out float surfaceRadius, out _);
                ocean[i] = surfaceRadius < seaRadius + 0.5f;
                vertices[i] = dir * Mathf.Max(1f, seaRadius - surfaceInset);
                normals[i] = dir;
                float depth = Mathf.Clamp01((seaRadius - surfaceRadius) / 42f);
                colors[i] = new Color(depth, 1f, 1f, 1f);
            }

            var oceanTriangles = new List<int>(allTriangles.Count);
            for (int i = 0; i < allTriangles.Count; i += 3)
            {
                int a = allTriangles[i];
                int b = allTriangles[i + 1];
                int c = allTriangles[i + 2];
                if (!ocean[a] && !ocean[b] && !ocean[c]) continue;
                oceanTriangles.Add(a); oceanTriangles.Add(b); oceanTriangles.Add(c);
            }

            _mesh ??= new Mesh { name = "PlanetOceanLOD", indexFormat = IndexFormat.UInt32 };
            _mesh.Clear();
            _mesh.SetVertices(vertices);
            _mesh.SetNormals(normals);
            _mesh.SetColors(colors);
            _mesh.SetTriangles(oceanTriangles, 0);
            _mesh.RecalculateBounds();
            _filter.sharedMesh = _mesh;
            _renderer.enabled = oceanTriangles.Count > 0;
            _lastResolution = targetResolution;
            _lastSeed = resolved.genParams.seed;
        }

        private void UpdateMaterial(CelestialBody resolved)
        {
            if (_material == null || resolved == null) return;
            bool isBelt = resolved.settings != null &&
                          (resolved.settings.bodyName.IndexOf("Asteroid", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           resolved.settings.bodyName.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (isBelt)
            {
                if (_renderer != null) _renderer.enabled = false;
                return;
            }
            if (_renderer != null) _renderer.enabled = true;
            Vector3 center = resolved.transform.position;
            if (_material.HasProperty("_BodyCenter")) _material.SetVector("_BodyCenter", new Vector4(center.x, center.y, center.z, 1f));
            if (_material.HasProperty("_ViewerPosition"))
            {
                Vector3 position = viewer != null ? viewer.position : center;
                _material.SetVector("_ViewerPosition", new Vector4(position.x, position.y, position.z, 1f));
            }
            if (_material.HasProperty("_CutoutRadius"))
            {
                // The ocean LOD sits slightly below actual streamed water and can safely fill
                // every chunk seam. A zero radius means no local cutout, eliminating gaps.
                _material.SetFloat("_CutoutRadius", 0f);
            }
            if (_material.HasProperty("_WaveTime")) _material.SetFloat("_WaveTime", Time.time);
        }

        private static List<Vector3> IcosahedronVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            return new List<Vector3>
            {
                N(-1, t, 0), N(1, t, 0), N(-1, -t, 0), N(1, -t, 0),
                N(0, -1, t), N(0, 1, t), N(0, -1, -t), N(0, 1, -t),
                N(t, 0, -1), N(t, 0, 1), N(-t, 0, -1), N(-t, 0, 1)
            };
        }

        private static List<int> IcosahedronTriangles() => new()
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };

        private static Vector3 N(float x, float y, float z) => new Vector3(x, y, z).normalized;

        private static void Subdivide(List<Vector3> vertices, List<int> triangles)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>(triangles.Count * 4);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int found)) return found;
                int index = vertices.Count;
                vertices.Add(((vertices[a] + vertices[b]) * 0.5f).normalized);
                cache[key] = index;
                return index;
            }

            for (int i = 0; i < triangles.Count; i += 3)
            {
                int a = triangles[i]; int b = triangles[i + 1]; int c = triangles[i + 2];
                int ab = Mid(a, b); int bc = Mid(b, c); int ca = Mid(c, a);
                next.Add(a); next.Add(ab); next.Add(ca);
                next.Add(b); next.Add(bc); next.Add(ab);
                next.Add(c); next.Add(ca); next.Add(bc);
                next.Add(ab); next.Add(bc); next.Add(ca);
            }
            triangles.Clear();
            triangles.AddRange(next);
        }
    }
}
