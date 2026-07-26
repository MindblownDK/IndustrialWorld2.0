// Assets/Scripts/VoxelEngine/Cosmos/AsteroidFieldRenderer.cs
//
// Renders a procedural asteroid field in deep space as GPU-instanced low-poly rocks.
//
// Asteroids are scattered in a spherical shell around the star (per the AsteroidFieldSettings).
// Each is a small random-polyhedron mesh, coloured by its material (iron/nickel/ice/etc.).
// Resources are capped at 0-5 types per the design brief; any asteroid that gets no resource
// defaults to stone. This is the VISUAL representation — mineable voxel asteroids arrive in
// Phase 6+ when we have a floating-origin space renderer.
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// GPU-instanced asteroid field. Reads AsteroidFieldSettings from the active system.
    /// Attach near the SpaceBodyRenderer.
    /// </summary>
    public class AsteroidFieldRenderer : MonoBehaviour
    {
        [Tooltip("Field settings. If null, uses defaults.")]
        public AsteroidFieldSettings settings = new AsteroidFieldSettings();

        [Tooltip("Visual distance range for the asteroids (min/max metres from viewer).")]
        public Vector2 visualDistanceRange = new Vector2(3000f, 8000f);

        [Tooltip("Rebuild when the viewer moves more than this.")]
        public float rebuildThreshold = 200f;

        [Tooltip("Max asteroids to render (perf budget).")]
        public int maxAsteroids = 200;

        private Mesh _asteroidMesh;
        private Material _asteroidMat;
        private NativeArray<Matrix4x4> _matrices;
        private NativeArray<Vector4> _colors;  // instanced color (SHADERFEATURE: procedural color)
        private int _instanceCount;
        private Vector3 _lastBuildPos = new Vector3(float.MaxValue, 0, 0);
        private RenderParams _renderParams;

        private void Awake()
        {
            _asteroidMesh = CreateAsteroidMesh();
            _asteroidMat = CreateAsteroidMaterial();
            _renderParams = new RenderParams(_asteroidMat);
        }

        private void OnDestroy()
        {
            if (_matrices.IsCreated) _matrices.Dispose();
            if (_colors.IsCreated) _colors.Dispose();
            if (_asteroidMesh != null) Destroy(_asteroidMesh);
            if (_asteroidMat != null) Destroy(_asteroidMat);
        }

        private void Update()
        {
            Vector3 viewerPos = GetViewerPosition();
            if (Vector3.Distance(viewerPos, _lastBuildPos) > rebuildThreshold)
            {
                Rebuild(viewerPos);
                _lastBuildPos = viewerPos;
            }

            if (_instanceCount > 0 && _matrices.IsCreated)
            {
                _asteroidMat.SetVectorArray("_InstanceColors", _colors.ToArray());
                Graphics.RenderMeshInstanced(_renderParams, _asteroidMesh, 0, _matrices);
            }
        }

        private void Rebuild(Vector3 viewerPos)
        {
            var rng = new Unity.Mathematics.Random((uint)(settings.shellRadiusKm.GetHashCode() + 1));

            // Pick up to resourceCount materials.
            var chosenColors = new List<Color>();
            if (settings.possibleResources != null && settings.resourceCount > 0)
            {
                var pool = new List<MaterialId>(settings.possibleResources);
                for (int i = 0; i < settings.resourceCount && pool.Count > 0; i++)
                {
                    int idx = rng.NextInt(0, pool.Count);
                    chosenColors.Add(MaterialRegistry.DefaultColor(pool[idx]));
                    pool.RemoveAt(idx);
                }
            }
            if (chosenColors.Count == 0)
                chosenColors.Add(MaterialRegistry.DefaultColor(MaterialId.Stone));

            int count = Mathf.Clamp(Mathf.RoundToInt(maxAsteroids * settings.density), 0, maxAsteroids);
            var matrices = new List<Matrix4x4>(count);
            var colors = new List<Vector4>(count);

            for (int i = 0; i < count; i++)
            {
                // Random direction in the sky.
                Vector3 dir = new Vector3(
                    rng.NextFloat(-1f, 1f),
                    rng.NextFloat(-0.6f, 1f),
                    rng.NextFloat(-1f, 1f)).normalized;

                // Distance within the visual range.
                float dist = rng.NextFloat(visualDistanceRange.x, visualDistanceRange.y);
                Vector3 pos = viewerPos + dir * dist;

                // Size.
                float sizeMin = settings.sizeRangeKm.x * 50f;
                float sizeMax = settings.sizeRangeKm.y * 50f;
                float size = rng.NextFloat(sizeMin, sizeMax);
                size = Mathf.Max(2f, size);

                // Random rotation.
                Quaternion rot = Quaternion.Euler(
                    rng.NextFloat(0, 360), rng.NextFloat(0, 360), rng.NextFloat(0, 360));

                matrices.Add(Matrix4x4.TRS(pos, rot, Vector3.one * size));

                // Color: pick a random resource color.
                Color c = chosenColors[rng.NextInt(0, chosenColors.Count)];
                colors.Add(new Vector4(c.r, c.g, c.b, 1f));
            }

            if (_matrices.IsCreated) _matrices.Dispose();
            if (_colors.IsCreated) _colors.Dispose();
            _instanceCount = matrices.Count;
            if (_instanceCount == 0) return;
            _matrices = new NativeArray<Matrix4x4>(_instanceCount, Allocator.Persistent);
            _colors = new NativeArray<Vector4>(_instanceCount, Allocator.Persistent);
            for (int i = 0; i < _instanceCount; i++)
            {
                _matrices[i] = matrices[i];
                _colors[i] = colors[i];
            }
        }

        private Vector3 GetViewerPosition()
        {
            var pc = FindAnyObjectByType<VoxelEngine.Player.PlayerController>();
            if (pc != null) return pc.transform.position;
            var cam = Camera.main;
            return cam != null ? cam.transform.position : transform.position;
        }

        // ── Procedural asteroid mesh (a low-poly irregular rock) ──
        private static Mesh CreateAsteroidMesh()
        {
            var rng = new Unity.Mathematics.Random(42);
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Start from an icosahedron, jitter vertices for an irregular rock shape.
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            verts.Add(Norm(-1, t, 0, rng));
            verts.Add(Norm( 1, t, 0, rng));
            verts.Add(Norm(-1,-t, 0, rng));
            verts.Add(Norm( 1,-t, 0, rng));
            verts.Add(Norm( 0,-1, t, rng));
            verts.Add(Norm( 0, 1, t, rng));
            verts.Add(Norm( 0,-1,-t, rng));
            verts.Add(Norm( 0, 1,-t, rng));
            verts.Add(Norm( t, 0,-1, rng));
            verts.Add(Norm( t, 0, 1, rng));
            verts.Add(Norm(-t, 0,-1, rng));
            verts.Add(Norm(-t, 0, 1, rng));

            tris.AddRange(new int[]
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10,2, 10, 7,6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            });

            var mesh = new Mesh { name = "Asteroid" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 Norm(float x, float y, float z, Unity.Mathematics.Random rng)
        {
            float jitter = 0.7f + rng.NextFloat(0f, 0.5f);
            return new Vector3(x, y, z).normalized * jitter;
        }

        private static Material CreateAsteroidMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Mat_Asteroid_Runtime";
            mat.enableInstancing = true;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", Color.white);
            return mat;
        }
    }
}
