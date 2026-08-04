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
        public Vector2 visualDistanceRange = new Vector2(2200f, 7000f);

        [Tooltip("Rebuild when the viewer moves more than this.")]
        public float rebuildThreshold = 200f;

        [Tooltip("Max asteroids to render (perf budget).")]
        public int maxAsteroids = 160;

        private Mesh _asteroidMesh;
        private Material _asteroidMat;
        private NativeArray<Matrix4x4> _matrices;
        private NativeArray<Vector4> _colors;  // instanced color (SHADERFEATURE: procedural color)
        private int _instanceCount;
        private Vector3 _lastBuildPos = new Vector3(float.MaxValue, 0, 0);
        private Vector3 _lastBuildForward = Vector3.forward;
        private bool _loggedFirstBuild;
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
            Camera camera = Camera.main;
            Vector3 forward = camera != null ? camera.transform.forward : Vector3.forward;
            bool cameraTurned = Vector3.Dot(forward, _lastBuildForward) < 0.90f;
            if (Vector3.Distance(viewerPos, _lastBuildPos) > rebuildThreshold || cameraTurned)
            {
                Rebuild(viewerPos);
                _lastBuildPos = viewerPos;
                _lastBuildForward = forward;
            }

            if (_instanceCount > 0 && _matrices.IsCreated)
                Graphics.RenderMeshInstanced(_renderParams, _asteroidMesh, 0, _matrices);
        }

        private void Rebuild(Vector3 viewerPos)
        {
            if (settings == null) settings = new AsteroidFieldSettings();
            var rng = new Unity.Mathematics.Random((uint)(settings.shellRadiusKm.GetHashCode() + 1));
            var matrices = new List<Matrix4x4>(maxAsteroids);
            var colors = new List<Vector4>(maxAsteroids);

            // Prefer the registry's deterministic asteroid belt. Unlike the old tiny random
            // pebbles, these use the actual system layout and a readable apparent size.
            AddRegistryAsteroids(viewerPos, ref rng, matrices, colors);

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

            if (!_loggedFirstBuild)
            {
                _loggedFirstBuild = true;
                Debug.Log("[AsteroidFieldRenderer] Built " + _instanceCount + " visible asteroid proxies from the active solar system.");
            }
        }

        private void AddRegistryAsteroids(Vector3 viewerPos, ref Unity.Mathematics.Random rng,
            List<Matrix4x4> matrices, List<Vector4> colors)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Asteroids == null || registry.Asteroids.Count == 0) return;

            Vector3 viewerKm = ResolveViewerCosmicPosition(registry);
            int stride = Mathf.Max(1, Mathf.CeilToInt(registry.Asteroids.Count / (float)maxAsteroids));
            for (int i = 0; i < registry.Asteroids.Count && matrices.Count < maxAsteroids; i += stride)
            {
                AsteroidInstance asteroid = registry.Asteroids[i];
                if (asteroid == null) continue;
                Vector3 deltaKm = asteroid.positionKm - viewerKm;
                float distanceKm = deltaKm.magnitude;
                if (distanceKm < 0.001f) continue;

                Vector3 direction = deltaKm / distanceKm;
                float visualDistance = Mathf.Lerp(visualDistanceRange.x, visualDistanceRange.y,
                    Mathf.Clamp01(distanceKm / 12000f));
                float size = Mathf.Max(8f, asteroid.sizeKm * 350f);
                Quaternion rotation = Quaternion.Euler(
                    rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f), rng.NextFloat(0f, 360f));
                matrices.Add(Matrix4x4.TRS(deltaKm * 4f, rotation, Vector3.one * size));
                Color color = MaterialRegistry.DefaultColor(asteroid.material);
                colors.Add(new Vector4(color.r, color.g, color.b, 1f));
            }
        }

        private static Vector3 ResolveViewerCosmicPosition(CosmicRegistry registry)
        {
            // Real space: the viewer's actual cosmic position (works in deep space too).
            var origin = SpaceOrigin.Instance;
            if (origin != null && origin.viewer != null)
                return (Vector3)(float3)origin.GetCosmicKm(origin.viewer.position);

            var active = GravityProvider.ActiveBody;
            if (active == null) return Vector3.zero;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                BodyInstance body = registry.Bodies[i];
                if (body != null && body.settings == active.settings) return body.positionKm;
            }
            return Vector3.zero;
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
            // Unlit keeps distant rocks readable even when the local surface is in night-side
            // shadow. They remain simple visual proxies, never physical gameplay colliders.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "Mat_Asteroid_Runtime";
            mat.enableInstancing = true;
            Color rock = new Color(0.56f, 0.50f, 0.44f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", rock);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", rock);
            return mat;
        }
    }
}
