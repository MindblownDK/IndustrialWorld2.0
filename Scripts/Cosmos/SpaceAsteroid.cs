// Assets/Scripts/VoxelEngine/Cosmos/SpaceAsteroid.cs
//
// A procedural, minable asteroid drifting in deep space — the real-space counterpart
// of the authored asteroid belts. Each asteroid is a noise-displaced icosphere with a
// MeshCollider, an ore payload (drops), and health: mine it with any tool (or shoot
// it) and it breaks apart into ore items.
//
// Asteroids are deterministic per spawn seed (same position + seed → same rock), so
// revisiting a region of space feels consistent. They are static in the cosmic frame;
// SpaceOrigin rebases them with the rest of the world.
using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Combat;
using VoxelEngine.Items;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    public class SpaceAsteroid : Damageable
    {
        [Header("Asteroid")]
        [Tooltip("Nominal radius in metres.")]
        public float sizeMetres = 30f;

        [Tooltip("Primary ore material — drives vertex tint + drop bias.")]
        public MaterialId oreMaterial = MaterialId.Stone;

        [Tooltip("Item drops rolled on destruction (ore items).")]
        public ItemDefinition[] oreDrops;

        [Tooltip("HP per metre of radius.")]
        public float hpPerMetre = 6f;

        [Tooltip("Visual tumble speed (deg/s).")]
        public float tumbleSpeed = 4f;

        private Vector3 _tumbleAxis;
        private Vector3 _driftVelocity;

        public static SpaceAsteroid Spawn(Vector3 position, float radius, MaterialId material,
            ItemDefinition[] drops, int seed)
            => Spawn(position, radius, material, drops, seed, Vector3.zero);

        public static SpaceAsteroid Spawn(Vector3 position, float radius, MaterialId material,
            ItemDefinition[] drops, int seed, Vector3 driftVelocity)
        {
            var go = new GameObject("SpaceAsteroid_" + material);
            go.transform.position = position;

            var asteroid = go.AddComponent<SpaceAsteroid>();
            asteroid.sizeMetres = radius;
            asteroid.oreMaterial = material;
            asteroid.oreDrops = drops;
            asteroid.maxHealth = Mathf.Max(40f, radius * asteroid.hpPerMetre);
            asteroid.minDrops = 3;
            asteroid.maxDrops = 7;
            asteroid.dropCount = 1;
            asteroid.drops = drops;
            asteroid._driftVelocity = driftVelocity;
            asteroid.BuildMesh(seed);
            asteroid._tumbleAxis = new Vector3(
                Mathf.Sin(seed * 12.9898f), Mathf.Cos(seed * 78.233f), Mathf.Sin(seed * 37.719f)).normalized;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = radius * radius * radius * 0.25f;
            rb.useGravity = false;
            rb.isKinematic = true;          // asteroids drift with the frame; nothing pushes them
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // Damageable.Awake already ran with default health — sync to the real value.
            asteroid.RefreshHealth();
            return asteroid;
        }

        /// <summary>Recompute max health from the configured size and reset current health.</summary>
        public void RefreshHealth()
        {
            maxHealth = Mathf.Max(40f, sizeMetres * hpPerMetre);
            Health = maxHealth;
        }

        public void BuildMesh(int seed)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            Icosahedron(verts, tris);
            Subdivide(verts, tris, 2);

            var colors = new Color[verts.Count];
            Color tint = OreTint(oreMaterial);
            float noiseScale = 2.2f / Mathf.Max(1f, sizeMetres * 0.06f);
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 dir = verts[i].normalized;
                float n = Fbm(dir * noiseScale + new Vector3(seed * 0.013f, seed * 0.007f, seed * 0.019f), 3);
                float r = Mathf.Lerp(0.82f, 1.28f, n);
                verts[i] = dir * sizeMetres * r;
                float grey = RandomValue(seed + i * 7919) * 0.12f;
                colors[i] = tint * (0.82f + grey);
                colors[i].a = 1f;
            }

            var mesh = new Mesh { name = "AsteroidMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetColors(colors);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var mf = gameObject.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            if (mr.sharedMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/Lit")
                             ?? Shader.Find("Standard");
                mr.sharedMaterial = new Material(shader) { name = "Mat_Asteroid" };
            }
            var mc = gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (tumbleSpeed > 0.01f)
                transform.Rotate(_tumbleAxis, tumbleSpeed * dt, Space.World);
            // Gentle through-field drift (9.15.0): the belt is alive, not parked.
            if (_driftVelocity.sqrMagnitude > 0.0001f)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null && rb.isKinematic) rb.MovePosition(rb.position + _driftVelocity * dt);
                else transform.position += _driftVelocity * dt;
            }
        }

        private static Color OreTint(MaterialId material)
        {
            switch (material)
            {
                case MaterialId.Iron:     return new Color(0.55f, 0.45f, 0.40f);
                case MaterialId.Nickel:   return new Color(0.65f, 0.65f, 0.60f);
                case MaterialId.Silicon:  return new Color(0.55f, 0.58f, 0.68f);
                case MaterialId.Cobalt:   return new Color(0.30f, 0.42f, 0.68f);
                case MaterialId.Silver:   return new Color(0.75f, 0.76f, 0.80f);
                case MaterialId.Gold:     return new Color(0.62f, 0.50f, 0.25f);
                case MaterialId.Platinum: return new Color(0.68f, 0.70f, 0.74f);
                case MaterialId.Uranium:  return new Color(0.38f, 0.52f, 0.30f);
                case MaterialId.Ice:      return new Color(0.72f, 0.82f, 0.92f);
                default:                  return new Color(0.45f, 0.44f, 0.43f);
            }
        }

        // ── Deterministic value noise (no Unity randomness — stable per seed) ──
        private static float RandomValue(int seed)
        {
            uint x = (uint)seed;
            x ^= x << 13; x ^= x >> 17; x ^= x << 5;
            return (x & 0xFFFFFF) / (float)0xFFFFFF;
        }

        private static float Hash3(int x, int y, int z)
        {
            int h = x * 374761393 + y * 668265263 + z * 1440662683;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static float ValueNoise(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x), y0 = Mathf.FloorToInt(p.y), z0 = Mathf.FloorToInt(p.z);
            float fx = p.x - x0, fy = p.y - y0, fz = p.z - z0;
            float sx = Smooth(fx), sy = Smooth(fy), sz = Smooth(fz);

            float c000 = Hash3(x0, y0, z0), c100 = Hash3(x0 + 1, y0, z0);
            float c010 = Hash3(x0, y0 + 1, z0), c110 = Hash3(x0 + 1, y0 + 1, z0);
            float c001 = Hash3(x0, y0, z0 + 1), c101 = Hash3(x0 + 1, y0, z0 + 1);
            float c011 = Hash3(x0, y0 + 1, z0 + 1), c111 = Hash3(x0 + 1, y0 + 1, z0 + 1);

            float x00 = Mathf.Lerp(c000, c100, sx), x10 = Mathf.Lerp(c010, c110, sx);
            float x01 = Mathf.Lerp(c001, c101, sx), x11 = Mathf.Lerp(c011, c111, sx);
            float y0v = Mathf.Lerp(x00, x10, sy), y1v = Mathf.Lerp(x01, x11, sy);
            return Mathf.Lerp(y0v, y1v, sz);
        }

        private static float Fbm(Vector3 p, int octaves)
        {
            float sum = 0f, amp = 0.5f, freq = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(p * freq) * amp;
                norm += amp;
                amp *= 0.5f;
                freq *= 2.13f;
            }
            return sum / norm;
        }

        // ── Icosphere helpers ─────────────────────────────────────
        private static void Icosahedron(List<Vector3> verts, List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] v =
            {
                new Vector3(-1, t, 0).normalized, new Vector3(1, t, 0).normalized,
                new Vector3(-1, -t, 0).normalized, new Vector3(1, -t, 0).normalized,
                new Vector3(0, -1, t).normalized, new Vector3(0, 1, t).normalized,
                new Vector3(0, -1, -t).normalized, new Vector3(0, 1, -t).normalized,
                new Vector3(t, 0, -1).normalized, new Vector3(t, 0, 1).normalized,
                new Vector3(-t, 0, -1).normalized, new Vector3(-t, 0, 1).normalized,
            };
            verts.AddRange(v);
            tris.AddRange(new[]
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            });
        }

        private static void Subdivide(List<Vector3> verts, List<int> tris, int iterations)
        {
            for (int it = 0; it < iterations; it++)
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
        }
    }
}
