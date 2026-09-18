// Assets/Scripts/VoxelEngine/Cosmos/AsteroidVoxelBody.cs
//
// A SMALL VOXEL ASTEROID — stone and ore you actually dig into.
//
// WHAT CHANGED AND WHY
// Asteroids used to be a single lumpy mesh with health: you shot it, it popped, it gave
// you ore. That is a destructible prop, not a minable body. You could not tunnel into
// one, could not see the ore seams, and could not leave a half-mined rock behind.
//
// This replaces that with a real voxel volume - the same idea as the planet terrain,
// just tiny. A rock is a dense sphere of voxels with ore veins running through it, and
// mining carves actual material out of it.
//
// WHY ITS OWN GRID RATHER THAN THE PLANET'S
// `SphereWorld` is planet-scale: it streams chunks around a viewer and its coordinates
// are anchored to a body's centre. An asteroid is a free-floating object a few metres
// across that DRIFTS AND TUMBLES. Putting it in the planet's voxel grid would mean
// either it cannot move, or the grid has to support moving sub-volumes - a far larger
// change than this feature is worth.
//
// So each asteroid owns a small dense voxel array in LOCAL space and meshes itself.
// Because the data is local, the whole rock can be moved and rotated freely by simply
// moving its transform, which is exactly what a drifting asteroid needs.
//
// SIZE IS THE POINT
// Rocks are 4-26 m across at a 0.5 m voxel, so a grid is at most ~52 cells per axis -
// small enough to mesh in one pass with no streaming, no jobs and no chunk bookkeeping.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public class AsteroidVoxelBody : MonoBehaviour
    {
        /// <summary>Edge length of one asteroid voxel, in metres.</summary>
        public const float VoxelSize = 0.5f;

        /// <summary>Density at or below this is empty space.</summary>
        private const sbyte SolidThreshold = 0;

        [SerializeField] private int _dim;              // cells per axis
        [SerializeField] private float _radiusMetres;

        private sbyte[] _density;
        private byte[] _material;

        private MeshFilter _filter;
        private MeshCollider _collider;
        private Mesh _mesh;
        private bool _dirty;

        public int Dimension => _dim;
        public float RadiusMetres => _radiusMetres;

        /// <summary>Solid voxels remaining. At zero the rock is gone.</summary>
        public int SolidCount { get; private set; }

        /// <summary>Total solids the rock started with, for a depletion readout.</summary>
        public int InitialSolidCount { get; private set; }

        public float Remaining01 => InitialSolidCount > 0
            ? Mathf.Clamp01(SolidCount / (float)InitialSolidCount) : 0f;

        // ── Registry, so a drill can find nearby rocks cheaply ───────────────────
        private static readonly List<AsteroidVoxelBody> s_all = new();
        public static IReadOnlyList<AsteroidVoxelBody> All => s_all;

        private void OnEnable() { if (!s_all.Contains(this)) s_all.Add(this); }
        private void OnDisable() { s_all.Remove(this); }

        // ════════════════════════════════════════════════════════════════
        //  GENERATION
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fills the rock: a noisy sphere of stone with ore veins running through it.
        ///
        /// The ore is placed as VEINS rather than a uniform mix, because the whole reason
        /// to dig into a rock rather than shoot it is to follow something. A uniform
        /// sprinkle would make every cubic metre identical and the digging pointless.
        /// </summary>
        public void Generate(float radiusMetres, MaterialId primaryOre, int seed)
        {
            _radiusMetres = Mathf.Max(1.5f, radiusMetres);

            // +2 cells of padding so the surface never touches the array edge, which would
            // leave a flat clipped face where the mesher runs out of neighbours.
            _dim = Mathf.Clamp(Mathf.CeilToInt(_radiusMetres * 2f / VoxelSize) + 3, 8, 72);

            int count = _dim * _dim * _dim;
            _density = new sbyte[count];
            _material = new byte[count];

            float centre = (_dim - 1) * 0.5f;
            var rng = new System.Random(seed);

            // Three vein axes, each a random plane through the rock. A voxel near one of
            // these planes becomes ore instead of stone.
            Vector3[] veinDir = new Vector3[3];
            float[] veinWidth = new float[3];
            for (int i = 0; i < 3; i++)
            {
                veinDir[i] = new Vector3(
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f,
                    (float)rng.NextDouble() * 2f - 1f).normalized;
                veinWidth[i] = Mathf.Lerp(0.6f, 1.6f, (float)rng.NextDouble());
            }

            float noiseSeed = seed * 0.0131f;
            SolidCount = 0;

            for (int z = 0; z < _dim; z++)
            for (int y = 0; y < _dim; y++)
            for (int x = 0; x < _dim; x++)
            {
                int i = Index(x, y, z);
                Vector3 local = new Vector3(x - centre, y - centre, z - centre);
                float dist = local.magnitude * VoxelSize;

                // A wobbly surface so rocks are not billiard balls.
                Vector3 dir = local.sqrMagnitude > 0.0001f ? local.normalized : Vector3.up;
                float wobble = Mathf.PerlinNoise(
                    dir.x * 2.3f + noiseSeed, dir.z * 2.3f + dir.y * 1.7f + noiseSeed);
                float surface = _radiusMetres * Mathf.Lerp(0.72f, 1.05f, wobble);

                if (dist > surface)
                {
                    _density[i] = 0;
                    _material[i] = (byte)MaterialId.Air;
                    continue;
                }

                _density[i] = 127;
                SolidCount++;

                // Ore near a vein plane, stone elsewhere. Deeper voxels are likelier to be
                // ore, so the valuable material is genuinely inside rather than on the skin.
                byte chosen = (byte)MaterialId.Stone;
                float depth01 = 1f - Mathf.Clamp01(dist / Mathf.Max(0.01f, surface));

                for (int v = 0; v < 3; v++)
                {
                    float planeDist = Mathf.Abs(Vector3.Dot(local * VoxelSize, veinDir[v]));
                    if (planeDist < veinWidth[v] * (0.5f + depth01))
                    {
                        chosen = (byte)primaryOre;
                        break;
                    }
                }

                _material[i] = chosen;
            }

            InitialSolidCount = SolidCount;
            _dirty = true;
            Rebuild();
        }

        // ════════════════════════════════════════════════════════════════
        //  MINING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Carves a sphere out of the rock at a WORLD position, reporting what came out.
        ///
        /// Returns false when nothing was removed, so a caller can tell "I mined" from
        /// "I swung at empty space" rather than silently granting nothing.
        /// </summary>
        public bool Carve(Vector3 worldPosition, float radiusMetres, Dictionary<MaterialId, int> yield)
        {
            if (_density == null) return false;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float centre = (_dim - 1) * 0.5f;

            // Local space is in metres; convert to cell space.
            Vector3 cell = local / VoxelSize + new Vector3(centre, centre, centre);
            float cellRadius = Mathf.Max(0.75f, radiusMetres / VoxelSize);

            int min = Mathf.FloorToInt(-cellRadius);
            int max = Mathf.CeilToInt(cellRadius);
            bool removed = false;

            for (int dz = min; dz <= max; dz++)
            for (int dy = min; dy <= max; dy++)
            for (int dx = min; dx <= max; dx++)
            {
                int x = Mathf.RoundToInt(cell.x) + dx;
                int y = Mathf.RoundToInt(cell.y) + dy;
                int z = Mathf.RoundToInt(cell.z) + dz;
                if (x < 0 || y < 0 || z < 0 || x >= _dim || y >= _dim || z >= _dim) continue;

                Vector3 voxelCentre = new Vector3(x, y, z);
                if (Vector3.Distance(voxelCentre, cell) > cellRadius) continue;

                int i = Index(x, y, z);
                if (_density[i] <= SolidThreshold) continue;

                var material = (MaterialId)_material[i];
                _density[i] = 0;
                _material[i] = (byte)MaterialId.Air;
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
                _dirty = true;
                Rebuild();

                // A rock mined to nothing removes itself rather than leaving an invisible
                // collider-less husk in the field.
                if (SolidCount <= 0) Destroy(gameObject);
            }

            return removed;
        }

        /// <summary>Material at a world position, or Air when outside or already mined.</summary>
        public MaterialId MaterialAt(Vector3 worldPosition)
        {
            if (_density == null) return MaterialId.Air;

            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float centre = (_dim - 1) * 0.5f;
            Vector3 cell = local / VoxelSize + new Vector3(centre, centre, centre);

            int x = Mathf.RoundToInt(cell.x), y = Mathf.RoundToInt(cell.y), z = Mathf.RoundToInt(cell.z);
            if (x < 0 || y < 0 || z < 0 || x >= _dim || y >= _dim || z >= _dim) return MaterialId.Air;

            int i = Index(x, y, z);
            return _density[i] > SolidThreshold ? (MaterialId)_material[i] : MaterialId.Air;
        }

        // ════════════════════════════════════════════════════════════════
        //  MESHING
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Rebuilds the visible mesh and its collider from the voxel data.
        ///
        /// Deliberately a simple exposed-face mesher rather than surface nets: a rock is at
        /// most ~70 cells per axis and is remeshed only when actually mined, so the extra
        /// complexity of a smooth mesher buys nothing here - and blocky faces read as
        /// "this is voxel material you are digging", which is the point.
        /// </summary>
        private void Rebuild()
        {
            if (!_dirty || _density == null) return;
            _dirty = false;

            EnsureComponents();

            var verts = new List<Vector3>(1024);
            var tris = new List<int>(2048);
            var colors = new List<Color32>(1024);

            float centre = (_dim - 1) * 0.5f;

            for (int z = 0; z < _dim; z++)
            for (int y = 0; y < _dim; y++)
            for (int x = 0; x < _dim; x++)
            {
                int i = Index(x, y, z);
                if (_density[i] <= SolidThreshold) continue;

                Color32 tint = TintFor((MaterialId)_material[i]);
                Vector3 origin = (new Vector3(x, y, z) - new Vector3(centre, centre, centre)) * VoxelSize;

                // Only emit a face where the neighbour is empty. Interior faces are never
                // seen and would multiply the triangle count many times over.
                for (int f = 0; f < 6; f++)
                {
                    int nx = x + FaceNormals[f].x;
                    int ny = y + FaceNormals[f].y;
                    int nz = z + FaceNormals[f].z;

                    bool neighbourSolid =
                        nx >= 0 && ny >= 0 && nz >= 0 && nx < _dim && ny < _dim && nz < _dim
                        && _density[Index(nx, ny, nz)] > SolidThreshold;
                    if (neighbourSolid) continue;

                    int baseIndex = verts.Count;
                    for (int c = 0; c < 4; c++)
                    {
                        verts.Add(origin + FaceCorners[f, c] * VoxelSize);
                        colors.Add(tint);
                    }

                    tris.Add(baseIndex);
                    tris.Add(baseIndex + 1);
                    tris.Add(baseIndex + 2);
                    tris.Add(baseIndex);
                    tris.Add(baseIndex + 2);
                    tris.Add(baseIndex + 3);
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "AsteroidVoxelMesh" };
                _mesh.MarkDynamic();
            }
            _mesh.Clear();

            if (verts.Count == 0)
            {
                _filter.sharedMesh = _mesh;
                _collider.sharedMesh = null;
                return;
            }

            // A fully mined rock can still exceed 65k vertices while partly intact.
            _mesh.indexFormat = verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.SetColors(colors);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            _filter.sharedMesh = _mesh;

            // Collider must be reassigned, not just mutated, or PhysX keeps the old shape.
            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh;
        }

        private void EnsureComponents()
        {
            if (_filter == null)
            {
                _filter = GetComponent<MeshFilter>();
                if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            }

            var renderer = GetComponent<MeshRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<MeshRenderer>();
            if (renderer.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
                renderer.sharedMaterial = new Material(shader) { name = "Mat_AsteroidVoxel" };
            }

            if (_collider == null)
            {
                _collider = GetComponent<MeshCollider>();
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
            }

            // Non-convex is correct here and safe: the rock is on a KINEMATIC rigidbody, and
            // a convex hull would fill in the tunnels the player just dug - you would mine a
            // cave and still bump into a solid ball.
            _collider.convex = false;
        }

        private int Index(int x, int y, int z) => (z * _dim + y) * _dim + x;

        private static Color32 TintFor(MaterialId material) => material switch
        {
            MaterialId.Iron => new Color32(150, 110, 88, 255),
            MaterialId.Nickel => new Color32(168, 170, 158, 255),
            MaterialId.Silicon => new Color32(116, 128, 140, 255),
            MaterialId.Cobalt => new Color32(86, 108, 176, 255),
            MaterialId.Gold => new Color32(214, 176, 74, 255),
            MaterialId.Platinum => new Color32(198, 206, 212, 255),
            MaterialId.Silver => new Color32(188, 194, 200, 255),
            MaterialId.Uranium => new Color32(112, 176, 96, 255),
            MaterialId.Ice => new Color32(178, 214, 232, 255),
            _ => new Color32(104, 100, 96, 255),   // stone
        };

        private static readonly Vector3Int[] FaceNormals =
        {
            new(0, 0, 1), new(0, 0, -1),
            new(1, 0, 0), new(-1, 0, 0),
            new(0, 1, 0), new(0, -1, 0),
        };

        // Corners wound counter-clockwise when viewed from outside each face.
        private static readonly Vector3[,] FaceCorners =
        {
            { new(-0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f) },
            { new(0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, -0.5f) },
            { new(0.5f, -0.5f, 0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, 0.5f, -0.5f), new(0.5f, 0.5f, 0.5f) },
            { new(-0.5f, -0.5f, -0.5f), new(-0.5f, -0.5f, 0.5f), new(-0.5f, 0.5f, 0.5f), new(-0.5f, 0.5f, -0.5f) },
            { new(-0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, 0.5f), new(0.5f, 0.5f, -0.5f), new(-0.5f, 0.5f, -0.5f) },
            { new(-0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, -0.5f), new(0.5f, -0.5f, 0.5f), new(-0.5f, -0.5f, 0.5f) },
        };
    }
}
