// Assets/Scripts/VoxelEngine/Cosmos/AsteroidVoxelBody.cs
//
// A SMALL VOXEL ASTEROID — stone and ore you actually dig into, meshed smooth.
//
// WHAT CHANGED IN 11.28.0
// The first voxel pass (11.27.0) meshed rocks with a hand-written exposed-face builder,
// which made them read as Minecraft cubes. They are now meshed with `SurfaceNetsJob` -
// the SAME job the planets use - so an asteroid has the same smooth iso-surface look as
// terrain, and the same material colours, because it is literally the same code path.
//
// THE KEY CONSTRAINT THAT MAKES THAT REUSE POSSIBLE
// `SurfaceNetsJob` is hard-wired to a padded CHUNK_SIZE_P (34) cube: it indexes voxels
// as CHUNK_SIZE_P^3 and walks CHUNK_SIZE+1 cells. So the asteroid's voxel grid is
// exactly one chunk. That is not a limitation in practice - at 0.5 m voxels a 32-cell
// chunk is a 16 m rock, which is already at the top of the size range we want.
//
// Sizing the grid to the mesher rather than writing a second mesher means asteroids
// inherit every future fix to terrain meshing for free.
//
// SHAPE: NOT ALL SPHERES
// A field of identical balls reads as procedural filler. Rocks are deformed by several
// layers of value noise plus a random axis stretch, so they come out as potatoes,
// shards and lumps - recognisably rocks rather than spheres with dents.
//
// MATERIALS ARE THE PLANET'S MATERIALS
// Voxels store real `MaterialId` values and are coloured through the shared
// `MaterialRegistry`, so asteroid stone looks like planet stone and asteroid iron looks
// like planet iron. Mining them yields the registry's configured drops. There is no
// separate asteroid material table to drift out of sync.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;          // IJobExtensions.Run
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using VoxelEngine.Core;
using VoxelEngine.Materials;
using VoxelEngine.Meshing;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public class AsteroidVoxelBody : MonoBehaviour
    {
        /// <summary>
        /// Edge length of one asteroid voxel, in metres - the SAME 1 m as planet terrain.
        ///
        /// Matching the planet matters for two reasons: a mining brush that carves a given
        /// radius of ground carves the same amount of rock, and the grid (a fixed 32 inner
        /// cells) then spans 32 m, which is what allows a usefully sized rock once the
        /// stretch and noise headroom is subtracted. At 0.5 m the ceiling was a 3 m pebble.
        /// </summary>
        public const float VoxelSize = VoxelConstants.VOXEL_SIZE;

        /// <summary>Grid dimension, fixed by the mesher's padded chunk layout.</summary>
        private const int Dim = VoxelConstants.CHUNK_SIZE_P;      // 34
        private const int Inner = VoxelConstants.CHUNK_SIZE;      // 32

        /// <summary>Half the grid, in metres. The mesh is emitted in a 0..Dim*vs box.</summary>
        private const float GridCentreMetres = (Dim - 1) * 0.5f * VoxelSize;

        private Voxel[] _voxels;
        private Transform _meshRoot;
        private MeshFilter _filter;
        private MeshRenderer _renderer;
        private MeshCollider _collider;
        private Mesh _mesh;

        [SerializeField] private float _radiusMetres;

        public float RadiusMetres => _radiusMetres;
        public int SolidCount { get; private set; }
        public int InitialSolidCount { get; private set; }

        public float Remaining01 => InitialSolidCount > 0
            ? Mathf.Clamp01(SolidCount / (float)InitialSolidCount) : 0f;

        private static readonly List<AsteroidVoxelBody> s_all = new();
        public static IReadOnlyList<AsteroidVoxelBody> All => s_all;

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        // ════════════════════════════════════════════════════════════════
        //  GENERATION
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fills the rock with stone and ore veins and meshes it.
        ///
        /// Density is SIGNED and graded rather than a hard 0/127, because surface nets
        /// interpolates the iso-crossing between neighbouring voxels. A binary field would
        /// put every vertex exactly halfway and reintroduce the blockiness this replaces.
        /// </summary>
        public void Generate(float radiusMetres, MaterialId primaryOre, int seed)
        {
            // The grid is a fixed 32 inner cells. The nominal radius must leave room for
            // BOTH the ellipsoid stretch and the noise displacement, or the rock grows past
            // the padding and gets sliced flat where it runs out of grid.
            //
            //   worst case reach = radius * maxStretch * (1 + 0.38 + 0.18 + 0.08)
            //
            // Solving that against the half-grid is what sets the ceiling here, and it is
            // why the spawner asks for smaller rocks than the raw grid size suggests.
            const float maxStretch = 1.45f;
            const float maxNoiseGain = 1f + 0.38f + 0.18f + 0.08f;
            float halfGrid = (Inner * 0.5f - 1.5f) * VoxelSize;
            float maxRadius = halfGrid / (maxStretch * maxNoiseGain);

            _radiusMetres = Mathf.Clamp(radiusMetres, 1.2f, maxRadius);

            _voxels = new Voxel[Dim * Dim * Dim];
            var rng = new System.Random(seed);

            // ── Shape ──
            // A random ellipsoid stretch plus layered noise. The stretch is what stops the
            // field being a bag of spheres; the noise is what makes the surface irregular.
            var stretch = new Vector3(
                Mathf.Lerp(0.55f, 1.45f, (float)rng.NextDouble()),
                Mathf.Lerp(0.55f, 1.45f, (float)rng.NextDouble()),
                Mathf.Lerp(0.55f, 1.45f, (float)rng.NextDouble()));

            var noiseOffset = new Vector3(
                (float)rng.NextDouble() * 100f,
                (float)rng.NextDouble() * 100f,
                (float)rng.NextDouble() * 100f);

            // A few big gouges, so some rocks are cracked or bitten into rather than whole.
            int gougeCount = rng.Next(0, 3);
            var gougeCentre = new Vector3[gougeCount];
            var gougeRadius = new float[gougeCount];
            for (int g = 0; g < gougeCount; g++)
            {
                gougeCentre[g] = RandomOnSphere(rng) * _radiusMetres * Mathf.Lerp(0.6f, 1.0f, (float)rng.NextDouble());
                gougeRadius[g] = _radiusMetres * Mathf.Lerp(0.25f, 0.55f, (float)rng.NextDouble());
            }

            // ── Ore veins ──
            int veinCount = rng.Next(2, 5);
            var veinDir = new Vector3[veinCount];
            var veinWidth = new float[veinCount];
            for (int v = 0; v < veinCount; v++)
            {
                veinDir[v] = RandomOnSphere(rng);
                veinWidth[v] = _radiusMetres * Mathf.Lerp(0.10f, 0.22f, (float)rng.NextDouble());
            }

            float centre = (Dim - 1) * 0.5f;
            SolidCount = 0;

            for (int z = 0; z < Dim; z++)
            for (int y = 0; y < Dim; y++)
            for (int x = 0; x < Dim; x++)
            {
                int i = (z * Dim + y) * Dim + x;

                Vector3 cell = new Vector3(x - centre, y - centre, z - centre) * VoxelSize;
                Vector3 shaped = new Vector3(cell.x / stretch.x, cell.y / stretch.y, cell.z / stretch.z);
                float dist = shaped.magnitude;

                // Surface radius wobbles with direction so the silhouette is irregular.
                Vector3 dir = dist > 0.0001f ? shaped / dist : Vector3.up;
                // Strong, multi-octave displacement. The first pass used +/-11% and +/-5.5%,
                // which is a sphere with a slight orange-peel texture - not a rock. These
                // amplitudes reshape the silhouette properly while staying inside the grid.
                float surface = _radiusMetres * (1f
                    + 0.38f * (Noise(dir * 1.7f + noiseOffset) - 0.5f) * 2f
                    + 0.18f * (Noise(dir * 3.9f + noiseOffset * 1.7f) - 0.5f) * 2f
                    + 0.08f * (Noise(dir * 8.3f + noiseOffset * 2.3f) - 0.5f) * 2f);

                float signed = surface - dist;

                // Gouges cut into the body: take the nearest surface, so a bite reads as a
                // concave crater rather than a floating hole.
                for (int g = 0; g < gougeCount; g++)
                {
                    float gd = Vector3.Distance(cell, gougeCentre[g]) - gougeRadius[g];
                    signed = Mathf.Min(signed, gd);
                }

                // SIGNED density, matching the engine's own asteroid-belt generator
                // (SphereDensity.EvaluateAsteroidVoxel): SOLID is +1..127 and EMPTY is
                // -127..-1. Empty must be NEGATIVE, never 0.
                //
                // This was the bug behind the shattered, disconnected faces: SurfaceNets
                // finds an iso-crossing with `(da > 0) != (db > 0)` and places the vertex
                // at `t = da / (da - db)`. With air stored as 0 the sign test still fires
                // but t collapses to 0 or 1, so every vertex snapped to a cell corner
                // instead of interpolating - and pass 2's `IsTerrainSolid` (also `> 0`)
                // disagreed about which cells had vertices at all, dropping the quads that
                // would have joined them up.
                float signedVoxels = signed / VoxelSize;   // distance to surface, in voxels

                if (signed <= 0f)
                {
                    sbyte outside = (sbyte)Mathf.Clamp(Mathf.RoundToInt(signedVoxels * 32f), -127, -1);
                    _voxels[i] = new Voxel(outside, (byte)MaterialId.Air);
                    continue;
                }

                // Graded, not binary: the gradient across the skin is what surface nets
                // interpolates to place a vertex smoothly between two voxel centres.
                sbyte density = (sbyte)Mathf.Clamp(Mathf.RoundToInt(signedVoxels * 32f), 1, 127);

                byte material = (byte)MaterialId.Stone;
                float depth01 = Mathf.Clamp01(signed / Mathf.Max(0.01f, _radiusMetres));

                for (int v = 0; v < veinCount; v++)
                {
                    // Distance to a plane through the centre: a slab of ore through the rock.
                    float planeDist = Mathf.Abs(Vector3.Dot(cell, veinDir[v]));
                    if (planeDist < veinWidth[v] * (0.45f + depth01))
                    {
                        material = (byte)primaryOre;
                        break;
                    }
                }

                _voxels[i] = new Voxel(density, material);
                SolidCount++;
            }

            InitialSolidCount = SolidCount;
            Rebuild();
        }

        // ════════════════════════════════════════════════════════════════
        //  MINING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Carves a sphere out of the rock at a WORLD position, reporting what came out.
        /// Returns false when nothing was removed.
        /// </summary>
        public bool Carve(Vector3 worldPosition, float radiusMetres, Dictionary<MaterialId, int> yield)
        {
            if (_voxels == null) return false;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float centre = (Dim - 1) * 0.5f;
            Vector3 cell = local / VoxelSize + new Vector3(centre, centre, centre);
            float cellRadius = Mathf.Max(0.9f, radiusMetres / VoxelSize);

            int span = Mathf.CeilToInt(cellRadius) + 1;
            int cx = Mathf.RoundToInt(cell.x), cy = Mathf.RoundToInt(cell.y), cz = Mathf.RoundToInt(cell.z);
            bool removed = false;

            for (int dz = -span; dz <= span; dz++)
            for (int dy = -span; dy <= span; dy++)
            for (int dx = -span; dx <= span; dx++)
            {
                int x = cx + dx, y = cy + dy, z = cz + dz;
                if (x < 0 || y < 0 || z < 0 || x >= Dim || y >= Dim || z >= Dim) continue;

                int i = (z * Dim + y) * Dim + x;
                var v = _voxels[i];
                if (v.density <= VoxelConstants.ISO_LEVEL) continue;

                float d = Vector3.Distance(new Vector3(x, y, z), cell);
                if (d > cellRadius) continue;

                // Soften rather than delete at the rim. A hard cut would leave a faceted
                // crater; easing the density lets surface nets round the new surface the
                // same way it rounds the original one.
                float falloff = 1f - Mathf.Clamp01(d / cellRadius);
                int reduction = Mathf.Max(24, Mathf.RoundToInt(127f * falloff));
                int next = v.density - reduction;

                if (next > 0)
                {
                    // Still solid, just thinner - the surface moves outward smoothly.
                    _voxels[i] = new Voxel((sbyte)next, v.material);
                    removed = true;
                    continue;
                }

                // Now empty. Carry the overshoot through as NEGATIVE density so the new
                // surface has a real gradient to interpolate against; clamping to 0 here
                // would re-create the faceted crater this whole scheme avoids.
                var material = (MaterialId)v.material;
                sbyte emptied = (sbyte)Mathf.Clamp(next, -127, -1);
                _voxels[i] = new Voxel(emptied, (byte)MaterialId.Air);
                SolidCount--;
                removed = true;

                if (yield != null)
                {
                    yield.TryGetValue(material, out int had);
                    yield[material] = had + 1;
                }
            }

            if (removed)
            {
                Rebuild();
                if (SolidCount <= 0) Destroy(gameObject);
            }
            return removed;
        }

        /// <summary>Material at a world position, or Air when outside or already mined.</summary>
        public MaterialId MaterialAt(Vector3 worldPosition)
        {
            if (_voxels == null) return MaterialId.Air;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float centre = (Dim - 1) * 0.5f;
            Vector3 cell = local / VoxelSize + new Vector3(centre, centre, centre);

            int x = Mathf.RoundToInt(cell.x), y = Mathf.RoundToInt(cell.y), z = Mathf.RoundToInt(cell.z);
            if (x < 0 || y < 0 || z < 0 || x >= Dim || y >= Dim || z >= Dim) return MaterialId.Air;

            var v = _voxels[(z * Dim + y) * Dim + x];
            return v.density > VoxelConstants.ISO_LEVEL ? (MaterialId)v.material : MaterialId.Air;
        }

        // ════════════════════════════════════════════════════════════════
        //  MESHING  (the planet's own surface-nets job)
        // ════════════════════════════════════════════════════════════════

        private void Rebuild()
        {
            if (_voxels == null) return;
            EnsureComponents();

            const int cells = (Inner + 1) * (Inner + 1) * (Inner + 1);
            int maxVerts = cells, maxIdx = cells * 18;

            var voxels = new NativeArray<Voxel>(_voxels.Length, Allocator.TempJob,
                NativeArrayOptions.UninitializedMemory);
            voxels.CopyFrom(_voxels);

            var meshDataArray = Mesh.AllocateWritableMeshData(1);
            var bounds = new NativeArray<Bounds>(1, Allocator.TempJob);
            var counts = new NativeArray<int>(2, Allocator.TempJob);
            var vertScratch = new NativeArray<float3>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var normScratch = new NativeArray<float3>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var colScratch = new NativeArray<Color32>(maxVerts, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var idxScratch = new NativeArray<int>(maxIdx, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var cellLut = new NativeArray<int>(cells, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var attributes = new NativeArray<VertexAttributeDescriptor>(3, Allocator.TempJob);
            attributes[0] = new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3);
            attributes[1] = new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3);
            attributes[2] = new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4);

            var colors = MaterialColorTable();

            var job = new SurfaceNetsJob
            {
                voxels = voxels,
                meshData = meshDataArray[0],
                bounds = bounds,
                counts = counts,
                vertexScratch = vertScratch,
                normalScratch = normScratch,
                colorScratch = colScratch,
                indexScratch = idxScratch,
                cellVertexIndex = cellLut,
                materialColors = colors,
                vertexAttributes = attributes,
                // Not a planet chunk: no radial-up assumptions, and AO is affordable here
                // because a rock is one small grid remeshed only when mined.
                isSphere = false,
                enableVertexAo = true,
                // Only used for radial shading when isSphere is set, which it is not here.
                chunkOrigin = float3.zero,
                voxelSize = VoxelSize,
            };

            // Run immediately: a dig must show its result this frame, and one 34^3 grid is
            // small enough that scheduling and waiting costs more than it saves.
            job.Run();

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "AsteroidVoxelMesh" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, _mesh,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            _mesh.bounds = bounds[0];

            bool hasGeometry = counts[1] >= 3 && _mesh.bounds.size.sqrMagnitude > 1e-7f;

            _filter.sharedMesh = _mesh;
            // Reassign rather than mutate, or PhysX keeps the previous shape and the player
            // collides with rock they already mined away.
            _collider.sharedMesh = null;
            if (hasGeometry) _collider.sharedMesh = _mesh;

            // SurfaceNets emits vertices at (cell * voxelSize), so the mesh occupies a
            // 0..Dim*voxelSize box rather than straddling the origin. The renderer and
            // collider live on a child that is pushed back by half the grid, which puts the
            // rock's centre on this object's origin - so it tumbles about itself instead of
            // swinging around a corner.
            _meshRoot.localPosition = new Vector3(-GridCentreMetres, -GridCentreMetres, -GridCentreMetres);

            voxels.Dispose();
            bounds.Dispose();
            counts.Dispose();
            vertScratch.Dispose();
            normScratch.Dispose();
            colScratch.Dispose();
            idxScratch.Dispose();
            cellLut.Dispose();
            attributes.Dispose();
        }

        /// <summary>
        /// Colour LUT straight from the shared MaterialRegistry, so asteroid stone is the
        /// same colour as planet stone. Cached because it never changes at runtime.
        /// </summary>
        private static NativeArray<Color32> MaterialColorTable()
        {
            var table = new NativeArray<Color32>(256, Allocator.TempJob);
            var registry = ResolveRegistry();
            for (int i = 0; i < 256; i++)
                table[i] = registry != null
                    ? (Color32)registry.GetColor((byte)i)
                    : new Color32(120, 116, 110, 255);
            return table;
        }

        private static MaterialRegistry _registry;

        private static MaterialRegistry ResolveRegistry()
        {
            if (_registry != null) return _registry;

            var world = ActiveWorld.Current;
            if (world != null && world.MaterialRegistry != null) return _registry = world.MaterialRegistry;

            _registry = Resources.Load<MaterialRegistry>("MaterialRegistry");
            if (_registry == null)
            {
                var all = Resources.FindObjectsOfTypeAll<MaterialRegistry>();
                if (all != null && all.Length > 0) _registry = all[0];
            }
            return _registry;
        }

        private void EnsureComponents()
        {
            // The geometry lives on a CHILD, offset by half the grid, because the mesher
            // emits a 0..Dim*voxelSize box. Keeping the parent clean means the asteroid's
            // transform is its true centre - which is what drift, tumble and the
            // world-to-local mining maths all assume.
            if (_meshRoot == null)
            {
                var existing = transform.Find("Voxels");
                if (existing != null) _meshRoot = existing;
                else
                {
                    var go = new GameObject("Voxels");
                    go.transform.SetParent(transform, false);
                    _meshRoot = go.transform;
                }
            }

            if (_filter == null)
            {
                _filter = _meshRoot.GetComponent<MeshFilter>();
                if (_filter == null) _filter = _meshRoot.gameObject.AddComponent<MeshFilter>();
            }

            if (_renderer == null)
            {
                _renderer = _meshRoot.GetComponent<MeshRenderer>();
                if (_renderer == null) _renderer = _meshRoot.gameObject.AddComponent<MeshRenderer>();
            }

            if (_renderer.sharedMaterial == null)
                _renderer.sharedMaterial = SharedSurfaceMaterial();

            if (_collider == null)
            {
                _collider = _meshRoot.GetComponent<MeshCollider>();
                if (_collider == null) _collider = _meshRoot.gameObject.AddComponent<MeshCollider>();
            }

            // Non-convex: a convex hull would fill in the tunnels the player just dug, so
            // they would mine a cave and still bump into a solid ball. Safe because rocks
            // are on kinematic rigidbodies.
            _collider.convex = false;
        }

        /// <summary>
        /// One shared vertex-colour material for every rock, so a field of asteroids is a
        /// single material and does not leak one per object.
        /// </summary>
        private static Material _surfaceMaterial;

        private static Material SharedSurfaceMaterial()
        {
            if (_surfaceMaterial != null) return _surfaceMaterial;

            // The mesher bakes material colour into VERTEX COLOURS, so the shader has to
            // read them. A plain URP/Lit material ignores vertex colour entirely, which is
            // why the first attempt rendered every rock flat white regardless of ore.
            //
            // Use the terrain's own material: it is already the vertex-colour voxel shader,
            // so rocks and ground shade identically by construction.
            if (ActiveWorld.Current is SphereWorld sphere && sphere.terrainMaterial != null)
                return _surfaceMaterial = sphere.terrainMaterial;

            var shared = Resources.Load<Material>("Mat_Terrain");
            if (shared != null) return _surfaceMaterial = shared;

            Shader shader = Shader.Find("VoxelEngine/VoxelTerrainURP")
                         ?? Shader.Find("VoxelEngine/VoxelTerrainEnhanced")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            _surfaceMaterial = new Material(shader) { name = "Mat_AsteroidVoxel" };
            if (_surfaceMaterial.HasProperty("_Smoothness")) _surfaceMaterial.SetFloat("_Smoothness", 0f);
            if (_surfaceMaterial.HasProperty("_BaseColor"))
                _surfaceMaterial.SetColor("_BaseColor", Color.white);   // let vertex colour show
            return _surfaceMaterial;
        }

        // ── Noise helpers ────────────────────────────────────────────────────────

        private static Vector3 RandomOnSphere(System.Random rng)
        {
            float z = (float)rng.NextDouble() * 2f - 1f;
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        /// <summary>Cheap 3D value noise in 0..1, built from Unity's 2D Perlin.</summary>
        private static float Noise(Vector3 p)
        {
            float xy = Mathf.PerlinNoise(p.x, p.y);
            float yz = Mathf.PerlinNoise(p.y, p.z);
            float zx = Mathf.PerlinNoise(p.z, p.x);
            return (xy + yz + zx) / 3f;
        }
    }
}
