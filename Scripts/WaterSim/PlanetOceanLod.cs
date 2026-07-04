// Assets/Scripts/VoxelEngine/WaterSim/PlanetOceanLod.cs
//
// Global continuous ocean LOD for spherical planets.
//
// Replaces fragmented per-voxel surface meshing with a unified, high-fidelity geodesic
// icosphere of radius SeaLevel * VOXEL_SIZE. This eliminates all blocky voxel edges,
// staircase coastline artifacts, and tile gaps while extending the ocean seamlessly
// to the horizon from any altitude or orbit.
//
// Operates as a child of the celestial body and shares the primary URP water shader
// from WaterMeshBuilder.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;

namespace VoxelEngine.WaterSim
{
    [DefaultExecutionOrder(100)]
    public class PlanetOceanLod : MonoBehaviour
    {
        public CelestialBody body;
        [Range(2, 6)] public int subdivision = 5; // ~20k vertices for smooth horizon

        private GameObject _oceanGO;
        private MeshFilter _meshFilter;
        private MeshRenderer _meshRenderer;
        private Mesh _mesh;
        private int _lastSubdivision = -1;
        private float _lastSeaRad = -1f;

        private void LateUpdate()
        {
            if (body == null) body = GetComponent<CelestialBody>();
            if (body == null || body.settings == null || body.settings.waterVolume <= 0.01f || body.settings.waterLevel <= 0)
            {
                if (_oceanGO != null) _oceanGO.SetActive(false);
                return;
            }

            EnsureGO();
            _oceanGO.SetActive(true);

            float seaRad = body.SeaRadius;
            if (subdivision != _lastSubdivision || Mathf.Abs(seaRad - _lastSeaRad) > 0.05f)
            {
                BuildOceanSphere(seaRad);
                _lastSubdivision = subdivision;
                _lastSeaRad = seaRad;
            }

            var mat = WaterMeshBuilder.GetWaterMaterial();
            if (_meshRenderer.sharedMaterial != mat && mat != null)
            {
                _meshRenderer.sharedMaterial = mat;
            }
        }

        private void EnsureGO()
        {
            if (_oceanGO != null) return;
            _oceanGO = new GameObject("PlanetOceanLOD");
            _oceanGO.transform.SetParent(transform, false);
            _oceanGO.transform.localPosition = Vector3.zero;
            _oceanGO.transform.localRotation = Quaternion.identity;
            _oceanGO.transform.localScale = Vector3.one;

            _meshFilter = _oceanGO.AddComponent<MeshFilter>();
            _meshRenderer = _oceanGO.AddComponent<MeshRenderer>();
            _meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
        }

        private void BuildOceanSphere(float radius)
        {
            var verts = new List<Vector3>(IcosahedronVerts());
            var tris  = new List<int>(IcosahedronTris());

            for (int s = 0; s < subdivision; s++)
            {
                Subdivide(verts, tris);
            }

            var finalVerts = new Vector3[verts.Count];
            var finalNorms = new Vector3[verts.Count];
            var finalUVs   = new Vector2[verts.Count];
            var finalUV2s  = new Vector2[verts.Count];
            var finalCols  = new Color[verts.Count];

            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i].normalized;
                finalVerts[i] = dir * radius;
                finalNorms[i] = dir;
                finalCols[i]  = Color.white; // depthToTerrain = 1 (deep open ocean)
                finalUVs[i]   = new Vector2(finalVerts[i].x * 0.37f + finalVerts[i].y * 0.19f, finalVerts[i].z + finalVerts[i].y * 0.19f);
                finalUV2s[i]  = Vector2.zero;
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "PlanetOceanLOD_Mesh" };
                _mesh.indexFormat = verts.Count > 60000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            }
            _mesh.Clear();
            _mesh.SetVertices(finalVerts);
            _mesh.SetNormals(finalNorms);
            _mesh.SetUVs(0, finalUVs);
            _mesh.SetUVs(1, finalUV2s);
            _mesh.SetColors(finalCols);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();

            if (_meshFilter != null) _meshFilter.sharedMesh = _mesh;
        }

        private static List<Vector3> IcosahedronVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new List<Vector3>
            {
                new Vector3(-1,  t,  0).normalized, new Vector3( 1,  t,  0).normalized, new Vector3(-1, -t,  0).normalized, new Vector3( 1, -t,  0).normalized,
                new Vector3( 0, -1,  t).normalized, new Vector3( 0,  1,  t).normalized, new Vector3( 0, -1, -t).normalized, new Vector3( 0,  1, -t).normalized,
                new Vector3( t,  0, -1).normalized, new Vector3( t,  0,  1).normalized, new Vector3(-t,  0, -1).normalized, new Vector3(-t,  0,  1).normalized,
            };
        }

        private static List<int> IcosahedronTris()
        {
            return new List<int>
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            };
        }

        private static void Subdivide(List<Vector3> verts, List<int> tris)
        {
            var cache = new Dictionary<long, int>();
            var newTris = new List<int>(tris.Count * 4);

            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count;
                verts.Add(mid);
                cache[key] = idx;
                return idx;
            }

            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                newTris.Add(a); newTris.Add(ab); newTris.Add(ca);
                newTris.Add(b); newTris.Add(bc); newTris.Add(ab);
                newTris.Add(c); newTris.Add(ca); newTris.Add(bc);
                newTris.Add(ab); newTris.Add(bc); newTris.Add(ca);
            }
            tris.Clear();
            tris.AddRange(newTris);
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                if (Application.isPlaying) Object.Destroy(_mesh);
                else Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
            if (_oceanGO != null)
            {
                if (Application.isPlaying) Object.Destroy(_oceanGO);
                else Object.DestroyImmediate(_oceanGO);
                _oceanGO = null;
            }
        }
    }
}
