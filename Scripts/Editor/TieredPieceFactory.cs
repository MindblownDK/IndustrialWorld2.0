#if UNITY_EDITOR
// Assets/Scripts/VoxelEngine/Editor/TieredPieceFactory.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║       INDUSTRIAL WORLD — TIERED CONSTRUCTION GEOMETRY             ║
// ║                                                                  ║
// ║  Every hammer-placed piece is modelled here and baked into one   ║
// ║  combined mesh per surface. A wall carries fifty-odd modelled    ║
// ║  parts - logs, studs, braces, rivets - and still costs three     ║
// ║  draw calls, because the parts are welded at author time instead ║
// ║  of shipped as fifty renderers each with its own transform.      ║
// ║                                                                  ║
// ║  SIZE. One module is 7.5 m square and one storey is 5.625 m:     ║
// ║  double the old footprint and half again the old height, so a    ║
// ║  room finally feels like a room rather than a cupboard.          ║
// ║                                                                  ║
// ║  STRONG SIDE / WEAK SIDE. +Z is the exterior: clad, sealed, and  ║
// ║  whatever the tier is made of. -Z is the interior: the structure ║
// ║  holding that cladding up. You can tell which way a wall faces   ║
// ║  from across the valley, which is the whole point.               ║
// ║                                                                  ║
// ║  UVs are generated at a fixed texel density from each part's     ║
// ║  world size, so a 7.5 m wall and a 0.3 m rivet strip show the    ║
// ║  same grain instead of one stretched and one tiled forty times.  ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VoxelEngine.Building.Tiered;

namespace VoxelEngine.EditorTools
{
    public static class TieredPieceFactory
    {
        // ── The size contract every piece and every socket obeys ─────────
        /// <summary>Foundation, floor, wall and roof footprint, in metres.</summary>
        public const float Module = 7.5f;
        /// <summary>Floor-to-floor height of one storey, in metres.</summary>
        public const float Storey = 5.625f;
        /// <summary>Top surface of a foundation deck above its placement origin.</summary>
        public const float DeckTop = 1.125f;

        public const float WallThick = 0.30f;
        public const float HalfModule = Module * 0.5f;

        /// <summary>Door opening: wide enough for two abreast, tall enough to feel built.</summary>
        private const float DoorW = 2.60f, DoorH = 3.90f;
        /// <summary>Garage opening: a vehicle-width hole in a wall.</summary>
        private const float GarageW = 5.00f, GarageH = 4.30f;
        /// <summary>Hatch opening in a floor slab.</summary>
        private const float HatchW = 2.60f;

        private const float Texel = 0.55f;   // metres per texture tile

        // ══════════════════════════════════════════════════════════════════
        //  MESH ASSEMBLY
        // ══════════════════════════════════════════════════════════════════

        private sealed class Part
        {
            public Mesh Mesh;
            public Matrix4x4 Xform;
        }

        /// <summary>
        /// Collects parts per surface and welds them. Parts are generated with
        /// world-scaled UVs up front, so the combine is a straight append.
        /// </summary>
        public sealed class PieceMesh
        {
            private readonly Dictionary<PieceSurface, List<Part>> _parts = new();

            public void Box(PieceSurface surface, Vector3 centre, Vector3 size)
                => Add(surface, BoxMesh(size), Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one));

            public void Box(PieceSurface surface, Vector3 centre, Vector3 size, Vector3 euler)
                => Add(surface, BoxMesh(size), Matrix4x4.TRS(centre, Quaternion.Euler(euler), Vector3.one));

            /// <summary>A log or pipe lying along its local Y, then rotated into place.</summary>
            public void Cylinder(PieceSurface surface, Vector3 centre, float radius, float length, Vector3 euler)
                => Add(surface, CylinderMesh(radius, length),
                       Matrix4x4.TRS(centre, Quaternion.Euler(euler), Vector3.one));

            /// <summary>A right-triangle prism: the run of a stair, the pitch of a roof.</summary>
            public void Wedge(PieceSurface surface, Vector3 centre, Vector3 size, Vector3 euler)
                => Add(surface, WedgeMesh(size), Matrix4x4.TRS(centre, Quaternion.Euler(euler), Vector3.one));

            private void Add(PieceSurface surface, Mesh mesh, Matrix4x4 xform)
            {
                if (!_parts.TryGetValue(surface, out var list))
                    _parts[surface] = list = new List<Part>();
                list.Add(new Part { Mesh = mesh, Xform = xform });
            }

            /// <summary>
            /// Welds each surface group and attaches it to the root as one renderer.
            /// The meshes are saved into a single asset file so the prefab keeps
            /// working after a domain reload.
            /// </summary>
            public void Commit(GameObject root, BuildTier tier, string meshAssetPath)
            {
                Object container = null;
                foreach (var pair in _parts)
                {
                    if (pair.Value.Count == 0) continue;

                    var combine = new CombineInstance[pair.Value.Count];
                    for (int i = 0; i < pair.Value.Count; i++)
                        combine[i] = new CombineInstance { mesh = pair.Value[i].Mesh, transform = pair.Value[i].Xform };

                    var merged = new Mesh { name = $"{root.name}_{pair.Key}" };
                    merged.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    merged.CombineMeshes(combine, true, true);
                    merged.RecalculateBounds();
                    merged.RecalculateTangents();

                    if (container == null)
                    {
                        AssetDatabase.CreateAsset(merged, meshAssetPath);
                        container = merged;
                    }
                    else AssetDatabase.AddObjectToAsset(merged, meshAssetPath);

                    var child = new GameObject($"Mesh_{pair.Key}");
                    child.transform.SetParent(root.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = merged;
                    var renderer = child.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = TieredSurfaces.Get(tier, pair.Key);
                    renderer.shadowCastingMode = pair.Key == PieceSurface.Glass
                        ? UnityEngine.Rendering.ShadowCastingMode.Off
                        : UnityEngine.Rendering.ShadowCastingMode.On;
                }

                foreach (var pair in _parts)
                    foreach (var part in pair.Value)
                        Object.DestroyImmediate(part.Mesh);
                _parts.Clear();
            }
        }

        // ── Primitive generation with world-scaled UVs ────────────────────

        private static Mesh BoxMesh(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            var verts = new List<Vector3>(24);
            var norms = new List<Vector3>(24);
            var uvs = new List<Vector2>(24);
            var tris = new List<int>(36);

            void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float w, float t)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 4; k++) norms.Add(n);
                float uw = Mathf.Max(0.02f, w / Texel), ut = Mathf.Max(0.02f, t / Texel);
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(uw, 0f));
                uvs.Add(new Vector2(uw, ut)); uvs.Add(new Vector2(0f, ut));
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i); tris.Add(i + 3); tris.Add(i + 2);
            }

            Face(Vector3.forward, new(-h.x, -h.y, h.z), new(h.x, -h.y, h.z), new(h.x, h.y, h.z), new(-h.x, h.y, h.z), size.x, size.y);
            Face(Vector3.back, new(h.x, -h.y, -h.z), new(-h.x, -h.y, -h.z), new(-h.x, h.y, -h.z), new(h.x, h.y, -h.z), size.x, size.y);
            Face(Vector3.right, new(h.x, -h.y, h.z), new(h.x, -h.y, -h.z), new(h.x, h.y, -h.z), new(h.x, h.y, h.z), size.z, size.y);
            Face(Vector3.left, new(-h.x, -h.y, -h.z), new(-h.x, -h.y, h.z), new(-h.x, h.y, h.z), new(-h.x, h.y, -h.z), size.z, size.y);
            Face(Vector3.up, new(-h.x, h.y, h.z), new(h.x, h.y, h.z), new(h.x, h.y, -h.z), new(-h.x, h.y, -h.z), size.x, size.z);
            Face(Vector3.down, new(-h.x, -h.y, -h.z), new(h.x, -h.y, -h.z), new(h.x, -h.y, h.z), new(-h.x, -h.y, h.z), size.x, size.z);

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        private static Mesh CylinderMesh(float radius, float length, int sides = 12)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float half = length * 0.5f;
            float circumference = 2f * Mathf.PI * radius;

            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 d0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                Vector3 d1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                int b = verts.Count;
                verts.Add(d0 * radius + Vector3.down * half);
                verts.Add(d1 * radius + Vector3.down * half);
                verts.Add(d1 * radius + Vector3.up * half);
                verts.Add(d0 * radius + Vector3.up * half);
                norms.Add(d0); norms.Add(d1); norms.Add(d1); norms.Add(d0);
                float u0 = i / (float)sides * circumference / Texel;
                float u1 = (i + 1) / (float)sides * circumference / Texel;
                float vt = length / Texel;
                uvs.Add(new Vector2(u0, 0f)); uvs.Add(new Vector2(u1, 0f));
                uvs.Add(new Vector2(u1, vt)); uvs.Add(new Vector2(u0, vt));
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
            }

            // Flat caps: a log end is a visible disc, and leaving it open reads
            // as a hole the moment the piece is seen from the side.
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? -half : half;
                Vector3 n = cap == 0 ? Vector3.down : Vector3.up;
                int centre = verts.Count;
                verts.Add(new Vector3(0f, y, 0f));
                norms.Add(n);
                uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    Vector3 d = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    verts.Add(d * radius + Vector3.up * y);
                    norms.Add(n);
                    uvs.Add(new Vector2(0.5f + d.x * 0.5f, 0.5f + d.z * 0.5f));
                }
                for (int i = 0; i < sides; i++)
                {
                    if (cap == 0) { tris.Add(centre); tris.Add(centre + i + 1); tris.Add(centre + i + 2); }
                    else { tris.Add(centre); tris.Add(centre + i + 2); tris.Add(centre + i + 1); }
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        /// <summary>Right-triangle prism, hypotenuse rising along +X, extruded along Z.</summary>
        private static Mesh WedgeMesh(Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 a = new(-h.x, -h.y, h.z), b = new(h.x, -h.y, h.z), c = new(h.x, h.y, h.z);
            Vector3 d = new(-h.x, -h.y, -h.z), e = new(h.x, -h.y, -h.z), f = new(h.x, h.y, -h.z);

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            void Tri(Vector3 p0, Vector3 p1, Vector3 p2)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                int i = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2);
                norms.Add(n); norms.Add(n); norms.Add(n);
                uvs.Add(new Vector2(p0.x / Texel, p0.y / Texel));
                uvs.Add(new Vector2(p1.x / Texel, p1.y / Texel));
                uvs.Add(new Vector2(p2.x / Texel, p2.y / Texel));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float w, float t)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p3 - p0).normalized;
                int i = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
                for (int k = 0; k < 4; k++) norms.Add(n);
                float uw = w / Texel, ut = t / Texel;
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(uw, 0f));
                uvs.Add(new Vector2(uw, ut)); uvs.Add(new Vector2(0f, ut));
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }

            Tri(a, b, c);
            Tri(e, d, f);
            Quad(d, e, b, a, size.x, size.z);                                  // underside
            Quad(a, c, f, d, Mathf.Sqrt(size.x * size.x + size.y * size.y), size.z); // pitch
            Quad(b, e, f, c, size.z, size.y);                                  // riser

            var mesh = new Mesh();
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            return mesh;
        }

        // ══════════════════════════════════════════════════════════════════
        //  TIER CLADDING — the same surface, told four different ways
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// The strong exterior face of a rectangular panel, in the piece's local
        /// frame. <paramref name="z"/> is the outward face plane.
        /// </summary>
        private static void CladSkin(PieceMesh m, BuildTier tier, Vector3 centre, float width, float height, float z, float depth)
        {
            switch (tier)
            {
                case BuildTier.Wood:
                {
                    // Horizontally laid rounded logs, tightly lined.
                    int courses = Mathf.Max(2, Mathf.RoundToInt(height / 0.62f));
                    float r = height / (courses * 2f);
                    for (int i = 0; i < courses; i++)
                    {
                        float y = centre.y - height * 0.5f + r + i * r * 2f;
                        m.Cylinder(PieceSurface.Skin, new Vector3(centre.x, y, z - r * 0.35f),
                                   r * 1.02f, width, new Vector3(0f, 0f, 90f));
                    }
                    break;
                }
                case BuildTier.Stone:
                {
                    // Fitted blocks in running bond, recessed mortar between.
                    m.Box(PieceSurface.Skin, new Vector3(centre.x, centre.y, z - depth * 0.5f),
                          new Vector3(width, height, depth));
                    int rows = Mathf.Max(3, Mathf.RoundToInt(height / 0.78f));
                    float rh = height / rows;
                    for (int r = 0; r < rows; r++)
                    {
                        int cols = 5;
                        float cw = width / cols;
                        float offset = (r & 1) == 0 ? 0f : cw * 0.5f;
                        for (int c = -1; c <= cols; c++)
                        {
                            float x = centre.x - width * 0.5f + offset + cw * (c + 0.5f);
                            if (x < centre.x - width * 0.5f + 0.05f || x > centre.x + width * 0.5f - 0.05f) continue;
                            m.Box(PieceSurface.Skin,
                                  new Vector3(x, centre.y - height * 0.5f + rh * (r + 0.5f), z + 0.035f),
                                  new Vector3(cw * 0.93f, rh * 0.86f, depth * 0.55f));
                        }
                    }
                    break;
                }
                case BuildTier.Iron:
                {
                    // Patchwork corrugated sheets riveted over a backing plate.
                    m.Box(PieceSurface.Skin, new Vector3(centre.x, centre.y, z - depth * 0.5f),
                          new Vector3(width, height, depth));
                    int sheets = Mathf.Max(2, Mathf.RoundToInt(width / 1.7f));
                    float sw = width / sheets;
                    for (int s = 0; s < sheets; s++)
                    {
                        float x = centre.x - width * 0.5f + sw * (s + 0.5f);
                        float lift = (s & 1) == 0 ? 0.045f : 0.025f;
                        float hh = height * ((s & 1) == 0 ? 0.97f : 0.90f);
                        m.Box(PieceSurface.Skin, new Vector3(x, centre.y, z + lift),
                              new Vector3(sw * 0.98f, hh, 0.05f));
                        for (int b = 0; b < 4; b++)
                            m.Box(PieceSurface.Trim,
                                  new Vector3(x, centre.y - hh * 0.5f + hh * (b + 0.5f) / 4f, z + lift + 0.035f),
                                  new Vector3(sw * 0.9f, 0.05f, 0.03f));
                    }
                    break;
                }
                default:
                {
                    // Armour: heavy plates, bevelled seams, corner rivets.
                    m.Box(PieceSurface.Skin, new Vector3(centre.x, centre.y, z - depth * 0.5f),
                          new Vector3(width, height, depth));
                    int cols = Mathf.Max(2, Mathf.RoundToInt(width / 2.6f));
                    int rows = Mathf.Max(2, Mathf.RoundToInt(height / 2.6f));
                    float pw = width / cols, ph = height / rows;
                    for (int c = 0; c < cols; c++)
                    for (int r = 0; r < rows; r++)
                    {
                        float x = centre.x - width * 0.5f + pw * (c + 0.5f);
                        float y = centre.y - height * 0.5f + ph * (r + 0.5f);
                        m.Box(PieceSurface.Skin, new Vector3(x, y, z + 0.04f),
                              new Vector3(pw * 0.94f, ph * 0.94f, 0.07f));
                        foreach (float sx in new[] { -1f, 1f })
                        foreach (float sy in new[] { -1f, 1f })
                            m.Box(PieceSurface.Trim,
                                  new Vector3(x + sx * pw * 0.38f, y + sy * ph * 0.38f, z + 0.085f),
                                  new Vector3(0.14f, 0.14f, 0.05f));
                    }
                    break;
                }
            }
        }

        /// <summary>The weak interior face: whatever is holding the cladding up.</summary>
        private static void CladFrame(PieceMesh m, BuildTier tier, Vector3 centre, float width, float height, float z)
        {
            switch (tier)
            {
                case BuildTier.Wood:
                {
                    // Vertical posts with diagonal cross-bracing between them.
                    int bays = Mathf.Max(2, Mathf.RoundToInt(width / 2.4f));
                    float bw = width / bays;
                    for (int i = 0; i <= bays; i++)
                    {
                        float x = centre.x - width * 0.5f + bw * i;
                        m.Box(PieceSurface.Frame, new Vector3(x, centre.y, z + 0.06f),
                              new Vector3(0.22f, height, 0.12f));
                    }
                    float diag = Mathf.Sqrt(bw * bw + height * height);
                    float angle = Mathf.Atan2(height, bw) * Mathf.Rad2Deg;
                    for (int i = 0; i < bays; i++)
                    {
                        float x = centre.x - width * 0.5f + bw * (i + 0.5f);
                        m.Box(PieceSurface.Frame, new Vector3(x, centre.y, z + 0.04f),
                              new Vector3(0.16f, diag * 0.96f, 0.10f), new Vector3(0f, 0f, angle - 90f));
                        m.Box(PieceSurface.Frame, new Vector3(x, centre.y, z + 0.04f),
                              new Vector3(0.16f, diag * 0.96f, 0.10f), new Vector3(0f, 0f, 90f - angle));
                    }
                    m.Box(PieceSurface.Trim, new Vector3(centre.x, centre.y + height * 0.5f - 0.16f, z + 0.06f),
                          new Vector3(width, 0.26f, 0.14f));
                    m.Box(PieceSurface.Trim, new Vector3(centre.x, centre.y - height * 0.5f + 0.16f, z + 0.06f),
                          new Vector3(width, 0.26f, 0.14f));
                    break;
                }
                case BuildTier.Stone:
                {
                    // Rough-hewn rock, individual stones projecting inward, with a
                    // timber lintel course where the floor supports would land.
                    int rows = Mathf.Max(3, Mathf.RoundToInt(height / 0.95f));
                    int cols = 4;
                    float rh = height / rows, cw = width / cols;
                    for (int r = 0; r < rows; r++)
                    for (int c = 0; c < cols; c++)
                    {
                        float jitter = Mathf.PerlinNoise(c * 3.7f + r * 1.3f, r * 2.1f);
                        m.Box(PieceSurface.Frame,
                              new Vector3(centre.x - width * 0.5f + cw * (c + 0.5f),
                                          centre.y - height * 0.5f + rh * (r + 0.5f),
                                          z + 0.05f + jitter * 0.09f),
                              new Vector3(cw * (0.82f + jitter * 0.14f), rh * (0.78f + jitter * 0.16f), 0.16f),
                              new Vector3(0f, 0f, (jitter - 0.5f) * 5f));
                    }
                    m.Box(PieceSurface.Trim, new Vector3(centre.x, centre.y + height * 0.5f - 0.22f, z + 0.14f),
                          new Vector3(width, 0.34f, 0.24f));
                    break;
                }
                case BuildTier.Iron:
                {
                    // A grid of structural L-beams with exposed bolt heads.
                    int bays = Mathf.Max(2, Mathf.RoundToInt(width / 2.0f));
                    float bw = width / bays;
                    for (int i = 0; i <= bays; i++)
                    {
                        float x = centre.x - width * 0.5f + bw * i;
                        m.Box(PieceSurface.Frame, new Vector3(x, centre.y, z + 0.07f), new Vector3(0.20f, height, 0.14f));
                        m.Box(PieceSurface.Frame, new Vector3(x + 0.08f, centre.y, z + 0.13f), new Vector3(0.05f, height, 0.14f));
                    }
                    int rails = Mathf.Max(2, Mathf.RoundToInt(height / 1.5f));
                    for (int r = 0; r <= rails; r++)
                    {
                        float y = centre.y - height * 0.5f + height * r / rails;
                        m.Box(PieceSurface.Frame, new Vector3(centre.x, y, z + 0.07f), new Vector3(width, 0.18f, 0.14f));
                        for (int i = 0; i <= bays; i++)
                            m.Box(PieceSurface.Trim,
                                  new Vector3(centre.x - width * 0.5f + bw * i, y, z + 0.155f),
                                  new Vector3(0.16f, 0.16f, 0.05f));
                    }
                    break;
                }
                default:
                {
                    // Tread-plate inner skin over heavy diagonal bracing.
                    m.Box(PieceSurface.Frame, new Vector3(centre.x, centre.y, z + 0.05f),
                          new Vector3(width * 0.99f, height * 0.99f, 0.10f));
                    float diag = Mathf.Sqrt(width * width + height * height);
                    float angle = Mathf.Atan2(height, width) * Mathf.Rad2Deg;
                    m.Box(PieceSurface.Frame, new Vector3(centre.x, centre.y, z + 0.13f),
                          new Vector3(0.26f, diag * 0.98f, 0.12f), new Vector3(0f, 0f, angle - 90f));
                    m.Box(PieceSurface.Frame, new Vector3(centre.x, centre.y, z + 0.13f),
                          new Vector3(0.26f, diag * 0.98f, 0.12f), new Vector3(0f, 0f, 90f - angle));
                    foreach (float sy in new[] { -1f, 1f })
                        m.Box(PieceSurface.Trim, new Vector3(centre.x, centre.y + sy * (height * 0.5f - 0.2f), z + 0.13f),
                              new Vector3(width, 0.32f, 0.16f));
                    break;
                }
            }
        }

        /// <summary>A clad panel with a rectangular hole: doorway, window, garage frame.</summary>
        private static void CladPanelWithHole(PieceMesh m, BuildTier tier, float width, float height,
                                              float holeW, float holeH, float holeBottom, float thick)
        {
            float halfW = width * 0.5f, z = thick * 0.5f;
            float sideW = (width - holeW) * 0.5f;
            float holeTop = holeBottom + holeH;

            if (sideW > 0.05f)
            {
                foreach (float sx in new[] { -1f, 1f })
                {
                    float cx = sx * (halfW - sideW * 0.5f);
                    CladSkin(m, tier, new Vector3(cx, height * 0.5f, 0f), sideW, height, z, thick);
                    CladFrame(m, tier, new Vector3(cx, height * 0.5f, 0f), sideW, height, -z);
                }
            }
            if (holeBottom > 0.05f)
            {
                CladSkin(m, tier, new Vector3(0f, holeBottom * 0.5f, 0f), holeW, holeBottom, z, thick);
                CladFrame(m, tier, new Vector3(0f, holeBottom * 0.5f, 0f), holeW, holeBottom, -z);
            }
            if (height - holeTop > 0.05f)
            {
                float capH = height - holeTop;
                CladSkin(m, tier, new Vector3(0f, holeTop + capH * 0.5f, 0f), holeW, capH, z, thick);
                CladFrame(m, tier, new Vector3(0f, holeTop + capH * 0.5f, 0f), holeW, capH, -z);
            }

            // Reveal lining, so the opening reads as cut through a solid wall.
            m.Box(PieceSurface.Trim, new Vector3(0f, holeTop + 0.07f, 0f), new Vector3(holeW + 0.3f, 0.16f, thick + 0.1f));
            if (holeBottom > 0.05f)
                m.Box(PieceSurface.Trim, new Vector3(0f, holeBottom - 0.07f, 0f), new Vector3(holeW + 0.3f, 0.16f, thick + 0.1f));
            foreach (float sx in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(sx * (holeW * 0.5f + 0.07f), holeBottom + holeH * 0.5f, 0f),
                      new Vector3(0.16f, holeH + 0.2f, thick + 0.1f));
        }

        // ══════════════════════════════════════════════════════════════════
        //  FAMILY GEOMETRY
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Builds one family at one tier onto <paramref name="root"/>.</summary>
        public static void Build(GameObject root, BuildFamily family, BuildTier tier, string meshAssetPath)
        {
            var m = new PieceMesh();
            switch (family)
            {
                case BuildFamily.Foundation: Foundation(m, tier); break;
                case BuildFamily.Floor:      Floor(m, tier, false); break;
                case BuildFamily.FloorHatch: Floor(m, tier, true); HatchLid(m, tier); break;
                case BuildFamily.Wall:       Wall(m, tier); break;
                case BuildFamily.Doorway:    CladPanelWithHole(m, tier, Module, Storey, DoorW, DoorH, 0f, WallThick); break;
                case BuildFamily.WallFrame:  CladPanelWithHole(m, tier, Module, Storey, GarageW, GarageH, 0f, WallThick); GarageTrack(m, tier); break;
                case BuildFamily.Window:     Window(m, tier); break;
                case BuildFamily.Door:       DoorLeaf(m, tier); break;
                case BuildFamily.GarageDoor: GarageDoor(m, tier); break;
                case BuildFamily.Stairs:     Stairs(m, tier); break;
                case BuildFamily.Roof:       Roof(m, tier); break;
                case BuildFamily.Pillar:     Pillar(m, tier); break;
                case BuildFamily.HalfWall:   HalfWall(m, tier); break;
                default:                     Wall(m, tier); break;
            }
            m.Commit(root, tier, meshAssetPath);
            AddColliders(root, family);
        }

        private static void Foundation(PieceMesh m, BuildTier tier)
        {
            const float deck = 0.34f;
            float top = DeckTop, bottom = top - deck;

            // Deck boards, laid across the module and overlapped so no light leaks.
            int boards = 10;
            float bw = Module / boards;
            for (int i = 0; i < boards; i++)
                m.Box(PieceSurface.Skin,
                      new Vector3(-HalfModule + bw * (i + 0.5f), top - deck * 0.5f, 0f),
                      new Vector3(bw * 1.04f, deck, Module));

            // Perimeter ring beam and corner piers carrying the deck off the ground.
            foreach (float s in new[] { -1f, 1f })
            {
                m.Box(PieceSurface.Trim, new Vector3(0f, bottom - 0.16f, s * (HalfModule - 0.18f)),
                      new Vector3(Module, 0.36f, 0.36f));
                m.Box(PieceSurface.Trim, new Vector3(s * (HalfModule - 0.18f), bottom - 0.16f, 0f),
                      new Vector3(0.36f, 0.36f, Module - 0.72f));
            }

            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                Vector3 p = new(x * (HalfModule - 0.42f), (bottom - 0.34f) * 0.5f, z * (HalfModule - 0.42f));
                m.Box(PieceSurface.Frame, new Vector3(p.x, p.y, p.z),
                      new Vector3(0.60f, bottom - 0.34f, 0.60f));
                // Knee braces from pier to ring beam.
                m.Box(PieceSurface.Frame, new Vector3(p.x - x * 0.42f, bottom - 0.48f, p.z),
                      new Vector3(0.18f, 1.05f, 0.18f), new Vector3(0f, 0f, x * 46f));
                m.Box(PieceSurface.Frame, new Vector3(p.x, bottom - 0.48f, p.z - z * 0.42f),
                      new Vector3(0.18f, 1.05f, 0.18f), new Vector3(-z * 46f, 0f, 0f));
            }

            // Perimeter kerb: a lip that visually locks walls to the deck edge.
            foreach (float s in new[] { -1f, 1f })
            {
                m.Box(PieceSurface.Trim, new Vector3(0f, top + 0.06f, s * (HalfModule - 0.07f)),
                      new Vector3(Module, 0.12f, 0.14f));
                m.Box(PieceSurface.Trim, new Vector3(s * (HalfModule - 0.07f), top + 0.06f, 0f),
                      new Vector3(0.14f, 0.12f, Module - 0.28f));
            }
        }

        private static void Floor(PieceMesh m, BuildTier tier, bool hatch)
        {
            const float slab = 0.38f;
            float half = HatchW * 0.5f;

            if (!hatch)
            {
                int boards = 10;
                float bw = Module / boards;
                for (int i = 0; i < boards; i++)
                    m.Box(PieceSurface.Skin, new Vector3(-HalfModule + bw * (i + 0.5f), slab * 0.5f, 0f),
                          new Vector3(bw * 1.04f, slab, Module));
            }
            else
            {
                // Deck the slab around a square opening.
                float band = (Module - HatchW) * 0.5f;
                foreach (float s in new[] { -1f, 1f })
                {
                    m.Box(PieceSurface.Skin, new Vector3(s * (HalfModule - band * 0.5f), slab * 0.5f, 0f),
                          new Vector3(band, slab, Module));
                    m.Box(PieceSurface.Skin, new Vector3(0f, slab * 0.5f, s * (HalfModule - band * 0.5f)),
                          new Vector3(HatchW, slab, band));
                }
                foreach (float s in new[] { -1f, 1f })
                {
                    m.Box(PieceSurface.Trim, new Vector3(s * (half + 0.08f), slab * 0.5f + 0.03f, 0f),
                          new Vector3(0.16f, slab + 0.06f, HatchW + 0.32f));
                    m.Box(PieceSurface.Trim, new Vector3(0f, slab * 0.5f + 0.03f, s * (half + 0.08f)),
                          new Vector3(HatchW + 0.32f, slab + 0.06f, 0.16f));
                }
            }

            // Joists under the slab: the ceiling of the room below.
            int joists = 6;
            for (int i = 0; i < joists; i++)
            {
                float z = -HalfModule + Module * (i + 0.5f) / joists;
                if (hatch && Mathf.Abs(z) < half + 0.2f) continue;
                m.Box(PieceSurface.Frame, new Vector3(0f, -0.17f, z), new Vector3(Module, 0.34f, 0.24f));
            }
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(s * (HalfModule - 0.11f), slab * 0.5f, 0f),
                      new Vector3(0.22f, slab + 0.05f, Module));
        }

        private static void HatchLid(PieceMesh m, BuildTier tier)
        {
            // The lid sits folded up on its hinge with the ladder dropped through.
            m.Box(PieceSurface.Skin, new Vector3(0f, HatchW * 0.5f + 0.4f, -HatchW * 0.5f - 0.1f),
                  new Vector3(HatchW - 0.1f, 0.14f, HatchW - 0.1f), new Vector3(78f, 0f, 0f));
            m.Box(PieceSurface.Trim, new Vector3(0f, 0.44f, -HatchW * 0.5f - 0.05f),
                  new Vector3(HatchW - 0.1f, 0.16f, 0.16f));

            for (int i = 0; i < 9; i++)
            {
                float y = -0.5f - i * 0.55f;
                m.Cylinder(PieceSurface.Trim, new Vector3(0f, y, 0.34f), 0.05f, HatchW * 0.55f, new Vector3(0f, 0f, 90f));
            }
            foreach (float s in new[] { -1f, 1f })
                m.Cylinder(PieceSurface.Trim, new Vector3(s * HatchW * 0.27f, -2.7f, 0.34f), 0.045f, 5.0f, Vector3.zero);
        }

        private static void Wall(PieceMesh m, BuildTier tier)
        {
            float z = WallThick * 0.5f;
            CladSkin(m, tier, new Vector3(0f, Storey * 0.5f, 0f), Module, Storey, z, WallThick);
            CladFrame(m, tier, new Vector3(0f, Storey * 0.5f, 0f), Module, Storey, -z);

            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(s * (HalfModule - 0.14f), Storey * 0.5f, 0f),
                      new Vector3(0.28f, Storey, WallThick + 0.2f));
            m.Box(PieceSurface.Trim, new Vector3(0f, Storey - 0.13f, 0f), new Vector3(Module, 0.26f, WallThick + 0.2f));
            m.Box(PieceSurface.Trim, new Vector3(0f, 0.13f, 0f), new Vector3(Module, 0.26f, WallThick + 0.2f));
        }

        private static void Window(PieceMesh m, BuildTier tier)
        {
            const float w = 3.6f, h = 2.1f;
            float bottom = (Storey - h) * 0.5f;
            CladPanelWithHole(m, tier, Module, Storey, w, h, bottom, WallThick);
            m.Box(PieceSurface.Glass, new Vector3(0f, bottom + h * 0.5f, 0f), new Vector3(w - 0.1f, h - 0.1f, 0.05f));
            m.Box(PieceSurface.Trim, new Vector3(0f, bottom + h * 0.5f, 0f), new Vector3(0.12f, h, WallThick + 0.06f));
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(s * w * 0.25f, bottom + h * 0.5f, 0.02f),
                      new Vector3(0.08f, h - 0.1f, 0.08f));
        }

        private static void DoorLeaf(PieceMesh m, BuildTier tier)
        {
            float w = DoorW - 0.16f, h = DoorH - 0.12f;

            if (tier == BuildTier.Wood)
            {
                int planks = 6;
                for (int i = 0; i < planks; i++)
                    m.Box(PieceSurface.Skin, new Vector3(-w * 0.5f + w * (i + 0.5f) / planks, h * 0.5f, 0f),
                          new Vector3(w / planks * 0.97f, h, 0.14f));
                foreach (float t in new[] { 0.16f, 0.84f })
                    m.Box(PieceSurface.Trim, new Vector3(0f, h * t, 0.09f), new Vector3(w, 0.22f, 0.06f));
            }
            else
            {
                m.Box(PieceSurface.Skin, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, 0.16f));
                foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                    m.Box(PieceSurface.Trim, new Vector3(sx * w * 0.4f, h * 0.5f + sy * h * 0.42f, 0.095f),
                          new Vector3(0.16f, 0.16f, 0.05f));

                // Sheet metal gets an eye hatch; armour gets a wide vision slot.
                float slotW = tier == BuildTier.Iron ? 0.34f : 0.90f;
                m.Box(PieceSurface.Trim, new Vector3(0f, h * 0.70f, 0.085f), new Vector3(slotW + 0.16f, 0.36f, 0.05f));
                m.Box(PieceSurface.Frame, new Vector3(0f, h * 0.70f, 0.06f), new Vector3(slotW, 0.22f, 0.04f));
            }

            m.Box(PieceSurface.Frame, new Vector3(0f, h * 0.5f, -0.09f), new Vector3(w - 0.1f, h - 0.1f, 0.06f));
            foreach (float t in new[] { 0.1f, 0.5f, 0.9f })
                m.Box(PieceSurface.Trim, new Vector3(-w * 0.5f + 0.05f, h * t, 0f), new Vector3(0.14f, 0.34f, 0.24f));
            m.Cylinder(PieceSurface.Trim, new Vector3(w * 0.5f - 0.28f, h * 0.46f, 0.14f), 0.07f, 0.26f, new Vector3(90f, 0f, 0f));
        }

        private static void GarageTrack(PieceMesh m, BuildTier tier)
        {
            // The drum housing the shutter rolls into, plus its side rails.
            m.Box(PieceSurface.Trim, new Vector3(0f, GarageH + 0.34f, 0f), new Vector3(GarageW + 0.5f, 0.58f, WallThick + 0.42f));
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(s * (GarageW * 0.5f + 0.13f), GarageH * 0.5f, -WallThick * 0.5f - 0.12f),
                      new Vector3(0.2f, GarageH, 0.2f));
        }

        private static void GarageDoor(PieceMesh m, BuildTier tier)
        {
            // A corrugated roll-up shutter: slats with a rolled top edge.
            int slats = 11;
            float sh = GarageH / slats;
            for (int i = 0; i < slats; i++)
            {
                float y = sh * (i + 0.5f);
                m.Box(PieceSurface.Skin, new Vector3(0f, y, 0f), new Vector3(GarageW - 0.2f, sh * 0.92f, 0.13f));
                m.Box(PieceSurface.Trim, new Vector3(0f, y + sh * 0.46f, 0.02f), new Vector3(GarageW - 0.2f, 0.05f, 0.17f));
            }
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Frame, new Vector3(s * (GarageW * 0.5f - 0.16f), GarageH * 0.5f, -0.09f),
                      new Vector3(0.2f, GarageH, 0.12f));
            m.Cylinder(PieceSurface.Trim, new Vector3(0f, GarageH + 0.3f, 0f), 0.28f, GarageW - 0.1f, new Vector3(0f, 0f, 90f));
            m.Box(PieceSurface.Trim, new Vector3(0f, 0.1f, 0f), new Vector3(GarageW - 0.2f, 0.2f, 0.2f));
        }

        private static void Stairs(PieceMesh m, BuildTier tier)
        {
            const int steps = 15;
            float rise = Storey / steps, run = Module / steps;
            float width = Module - 0.6f;

            for (int i = 0; i < steps; i++)
            {
                float y = rise * (i + 0.5f);
                float z = -HalfModule + run * (i + 0.5f);
                m.Box(PieceSurface.Skin, new Vector3(0f, y + rise * 0.42f, z), new Vector3(width, rise * 0.22f, run * 1.05f));
                m.Box(PieceSurface.Frame, new Vector3(0f, y, z - run * 0.45f), new Vector3(width - 0.2f, rise, 0.12f));
            }

            // Stringers running the pitch, and a handrail on each side.
            float diag = Mathf.Sqrt(Module * Module + Storey * Storey);
            float pitch = Mathf.Atan2(Storey, Module) * Mathf.Rad2Deg;
            foreach (float s in new[] { -1f, 1f })
            {
                m.Box(PieceSurface.Frame, new Vector3(s * width * 0.5f, Storey * 0.5f - 0.2f, 0f),
                      new Vector3(0.22f, 0.5f, diag), new Vector3(-pitch, 0f, 0f));
                m.Box(PieceSurface.Trim, new Vector3(s * width * 0.5f, Storey * 0.5f + 1.0f, 0f),
                      new Vector3(0.12f, 0.12f, diag), new Vector3(-pitch, 0f, 0f));
                for (int i = 1; i < 5; i++)
                {
                    float t = i / 5f;
                    m.Box(PieceSurface.Trim, new Vector3(s * width * 0.5f, Storey * t + 0.55f, -HalfModule + Module * t),
                          new Vector3(0.1f, 1.1f, 0.1f));
                }
            }
        }

        private static void Roof(PieceMesh m, BuildTier tier)
        {
            const float pitch = 26f;
            float len = Module / Mathf.Cos(pitch * Mathf.Deg2Rad);

            m.Box(PieceSurface.Frame, new Vector3(0f, Module * 0.5f * Mathf.Tan(pitch * Mathf.Deg2Rad) * 0.5f, 0f),
                  new Vector3(Module, 0.22f, len), new Vector3(pitch, 0f, 0f));

            // Cladding courses running up the pitch, overlapped like shingles.
            int courses = 7;
            for (int i = 0; i < courses; i++)
            {
                float t = (i + 0.5f) / courses;
                float z = -len * 0.5f + len * t;
                var local = Quaternion.Euler(pitch, 0f, 0f) * new Vector3(0f, 0.16f, z);
                m.Box(PieceSurface.Skin,
                      local + new Vector3(0f, Module * 0.5f * Mathf.Tan(pitch * Mathf.Deg2Rad) * 0.5f, 0f),
                      new Vector3(Module, 0.12f, len / courses * 1.12f), new Vector3(pitch, 0f, 0f));
            }

            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim,
                      Quaternion.Euler(pitch, 0f, 0f) * new Vector3(s * (HalfModule - 0.1f), 0.2f, 0f)
                      + new Vector3(0f, Module * 0.5f * Mathf.Tan(pitch * Mathf.Deg2Rad) * 0.5f, 0f),
                      new Vector3(0.2f, 0.2f, len), new Vector3(pitch, 0f, 0f));
        }

        private static void Pillar(PieceMesh m, BuildTier tier)
        {
            if (tier == BuildTier.Wood)
                m.Cylinder(PieceSurface.Skin, new Vector3(0f, Storey * 0.5f, 0f), 0.30f, Storey - 0.36f, Vector3.zero);
            else
                m.Box(PieceSurface.Skin, new Vector3(0f, Storey * 0.5f, 0f), new Vector3(0.58f, Storey - 0.36f, 0.58f));

            m.Box(PieceSurface.Trim, new Vector3(0f, 0.11f, 0f), new Vector3(0.86f, 0.22f, 0.86f));
            m.Box(PieceSurface.Trim, new Vector3(0f, Storey - 0.11f, 0f), new Vector3(0.86f, 0.22f, 0.86f));
            foreach (float y in new[] { Storey * 0.34f, Storey * 0.68f })
                m.Box(PieceSurface.Trim, new Vector3(0f, y, 0f), new Vector3(0.68f, 0.12f, 0.68f));
        }

        private static void HalfWall(PieceMesh m, BuildTier tier)
        {
            const float h = 2.8f;
            float z = WallThick * 0.5f;
            CladSkin(m, tier, new Vector3(0f, h * 0.5f, 0f), Module, h, z, WallThick);
            CladFrame(m, tier, new Vector3(0f, h * 0.5f, 0f), Module, h, -z);
            m.Box(PieceSurface.Trim, new Vector3(0f, h + 0.11f, 0f), new Vector3(Module, 0.22f, WallThick + 0.34f));
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(s * (HalfModule - 0.13f), h * 0.5f, 0f),
                      new Vector3(0.26f, h, WallThick + 0.2f));
        }

        // ══════════════════════════════════════════════════════════════════
        //  ORBITAL STATION
        // ══════════════════════════════════════════════════════════════════
        //  The station set now shares the structural module exactly, so a
        //  habitat corridor lines up with a foundation and a deck lines up with
        //  a floor. It used to be a 2 m kit beside a 3.75 m one, which meant
        //  the two families could never meet at a seam.

        public static void BuildStation(GameObject root, BuildFamily family, BuildTier tier, string meshAssetPath)
        {
            var m = new PieceMesh();
            switch (family)
            {
                case BuildFamily.StationHull:     StationHull(m); break;
                case BuildFamily.StationFloor:    StationDeck(m); break;
                case BuildFamily.StationCorridor: StationCorridor(m); break;
                case BuildFamily.StationJunction: StationJunction(m); break;
                case BuildFamily.StationWindow:   StationViewport(m); break;
                case BuildFamily.StationAirlock:  StationAirlock(m); break;
                case BuildFamily.StationDock:     StationDock(m); break;
                default:                          StationDome(m); break;
            }
            m.Commit(root, tier, meshAssetPath);
            AddStationColliders(root, family);
        }

        /// <summary>Ribbed pressure plating: the station's equivalent of a wall.</summary>
        private static void StationHull(PieceMesh m)
        {
            m.Box(PieceSurface.Skin, new Vector3(0f, Storey * 0.5f, 0f), new Vector3(Module, Storey, 0.26f));
            int ribs = 5;
            for (int i = 0; i <= ribs; i++)
                m.Box(PieceSurface.Trim, new Vector3(-HalfModule + Module * i / ribs, Storey * 0.5f, 0.02f),
                      new Vector3(0.24f, Storey, 0.34f));
            foreach (float y in new[] { 0.18f, Storey - 0.18f })
                m.Box(PieceSurface.Trim, new Vector3(0f, y, 0.02f), new Vector3(Module, 0.36f, 0.38f));
            // Inner liner with service conduit.
            m.Box(PieceSurface.Frame, new Vector3(0f, Storey * 0.5f, -0.2f), new Vector3(Module - 0.1f, Storey - 0.1f, 0.12f));
            m.Cylinder(PieceSurface.Trim, new Vector3(0f, Storey - 0.7f, -0.33f), 0.11f, Module, new Vector3(0f, 0f, 90f));
        }

        private static void StationDeck(PieceMesh m)
        {
            m.Box(PieceSurface.Skin, new Vector3(0f, 0.19f, 0f), new Vector3(Module, 0.38f, Module));
            // Magnetic tread channels.
            for (int i = 1; i < 6; i++)
                m.Box(PieceSurface.Trim, new Vector3(-HalfModule + Module * i / 6f, 0.4f, 0f),
                      new Vector3(0.14f, 0.05f, Module - 0.4f));
            foreach (float s in new[] { -1f, 1f })
            {
                m.Box(PieceSurface.Trim, new Vector3(s * (HalfModule - 0.12f), 0.22f, 0f), new Vector3(0.24f, 0.46f, Module));
                m.Box(PieceSurface.Trim, new Vector3(0f, 0.22f, s * (HalfModule - 0.12f)), new Vector3(Module - 0.48f, 0.46f, 0.24f));
            }
            m.Box(PieceSurface.Frame, new Vector3(0f, -0.16f, 0f), new Vector3(Module - 0.3f, 0.32f, Module - 0.3f));
        }

        private static void StationCorridor(PieceMesh m)
        {
            float w = Module * 0.62f, h = Storey * 0.82f;
            m.Box(PieceSurface.Skin, new Vector3(0f, 0.19f, 0f), new Vector3(w, 0.38f, Module));
            m.Box(PieceSurface.Skin, new Vector3(0f, h, 0f), new Vector3(w, 0.32f, Module));
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Skin, new Vector3(s * w * 0.5f, h * 0.5f, 0f), new Vector3(0.26f, h, Module));
            // Pressure frames every couple of metres.
            for (int i = 0; i <= 3; i++)
            {
                float z = -HalfModule + Module * i / 3f;
                m.Box(PieceSurface.Trim, new Vector3(0f, h + 0.18f, z), new Vector3(w + 0.3f, 0.2f, 0.26f));
                foreach (float s in new[] { -1f, 1f })
                    m.Box(PieceSurface.Trim, new Vector3(s * (w * 0.5f + 0.13f), h * 0.5f, z), new Vector3(0.2f, h, 0.26f));
            }
            m.Box(PieceSurface.Frame, new Vector3(0f, h - 0.34f, 0f), new Vector3(w - 0.5f, 0.16f, Module - 0.4f));
        }

        private static void StationJunction(PieceMesh m)
        {
            float h = Storey * 0.82f;
            m.Box(PieceSurface.Skin, new Vector3(0f, 0.19f, 0f), new Vector3(Module, 0.38f, Module));
            m.Box(PieceSurface.Skin, new Vector3(0f, h, 0f), new Vector3(Module, 0.32f, Module));
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                m.Box(PieceSurface.Trim, new Vector3(x * (HalfModule - 0.3f), h * 0.5f, z * (HalfModule - 0.3f)),
                      new Vector3(0.5f, h, 0.5f));
                m.Box(PieceSurface.Frame, new Vector3(x * (HalfModule - 0.3f), h * 0.5f, z * (HalfModule - 0.3f)),
                      new Vector3(0.24f, h, 0.9f));
            }
            m.Cylinder(PieceSurface.Trim, new Vector3(0f, h - 0.3f, 0f), 0.5f, 0.3f, Vector3.zero);
        }

        private static void StationViewport(PieceMesh m)
        {
            float w = Module * 0.64f, h = Storey * 0.56f, bottom = (Storey - h) * 0.5f;
            float side = (Module - w) * 0.5f;
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Skin, new Vector3(s * (HalfModule - side * 0.5f), Storey * 0.5f, 0f),
                      new Vector3(side, Storey, 0.26f));
            m.Box(PieceSurface.Skin, new Vector3(0f, bottom * 0.5f, 0f), new Vector3(w, bottom, 0.26f));
            m.Box(PieceSurface.Skin, new Vector3(0f, bottom + h + (Storey - bottom - h) * 0.5f, 0f),
                  new Vector3(w, Storey - bottom - h, 0.26f));
            m.Box(PieceSurface.Glass, new Vector3(0f, bottom + h * 0.5f, 0f), new Vector3(w - 0.2f, h - 0.2f, 0.08f));
            m.Box(PieceSurface.Trim, new Vector3(0f, bottom + h * 0.5f, 0f), new Vector3(w + 0.2f, 0.18f, 0.38f));
            m.Box(PieceSurface.Trim, new Vector3(0f, bottom + h * 0.5f, 0f), new Vector3(0.18f, h + 0.2f, 0.38f));
            foreach (float s in new[] { -1f, 1f })
            {
                m.Box(PieceSurface.Trim, new Vector3(0f, bottom + s * h * 0.5f, 0f), new Vector3(w + 0.3f, 0.2f, 0.4f));
                m.Box(PieceSurface.Trim, new Vector3(s * w * 0.5f, bottom + h * 0.5f, 0f), new Vector3(0.2f, h + 0.2f, 0.4f));
            }
        }

        private static void StationAirlock(PieceMesh m)
        {
            float holeW = Module * 0.34f, holeH = Storey * 0.66f;
            float side = (Module - holeW) * 0.5f;
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Skin, new Vector3(s * (HalfModule - side * 0.5f), Storey * 0.5f, 0f),
                      new Vector3(side, Storey, 0.4f));
            m.Box(PieceSurface.Skin, new Vector3(0f, holeH + (Storey - holeH) * 0.5f, 0f),
                  new Vector3(holeW, Storey - holeH, 0.4f));
            // Two hatches, never open at once: outer sealed, inner cracked.
            m.Cylinder(PieceSurface.Frame, new Vector3(0f, holeH * 0.52f, 0.2f), holeW * 0.48f, 0.16f, new Vector3(90f, 0f, 0f));
            m.Cylinder(PieceSurface.Trim, new Vector3(0f, holeH * 0.52f, 0.28f), holeW * 0.16f, 0.12f, new Vector3(90f, 0f, 0f));
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                m.Box(PieceSurface.Trim,
                      new Vector3(Mathf.Cos(a) * holeW * 0.4f, holeH * 0.52f + Mathf.Sin(a) * holeW * 0.4f, 0.28f),
                      new Vector3(0.16f, 0.16f, 0.1f));
            }
            m.Box(PieceSurface.Frame, new Vector3(0f, holeH * 0.52f, -0.2f), new Vector3(holeW, holeH, 0.14f));
            m.Box(PieceSurface.Trim, new Vector3(0f, holeH + 0.12f, 0f), new Vector3(holeW + 0.5f, 0.24f, 0.52f));
        }

        private static void StationDock(PieceMesh m)
        {
            float r = Module * 0.4f;
            for (int i = 0; i < 16; i++)
            {
                float a0 = i / 16f * Mathf.PI * 2f;
                m.Box(PieceSurface.Skin,
                      new Vector3(Mathf.Cos(a0) * r, Storey * 0.5f + Mathf.Sin(a0) * r, 0f),
                      new Vector3(r * 0.42f, 0.34f, 0.7f),
                      new Vector3(0f, 0f, a0 * Mathf.Rad2Deg + 90f));
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                m.Box(PieceSurface.Trim,
                      new Vector3(Mathf.Cos(a) * r * 1.12f, Storey * 0.5f + Mathf.Sin(a) * r * 1.12f, 0.2f),
                      new Vector3(0.34f, 0.34f, 1.1f));
            }
            m.Box(PieceSurface.Frame, new Vector3(0f, 0.2f, 0f), new Vector3(Module, 0.4f, 0.8f));
            m.Box(PieceSurface.Frame, new Vector3(0f, Storey - 0.2f, 0f), new Vector3(Module, 0.4f, 0.8f));
        }

        private static void StationDome(PieceMesh m)
        {
            // A ribbed glazed cap: rings of glass between meridian ribs.
            const int rings = 5, segs = 16;
            float radius = HalfModule * 0.96f, height = Storey * 0.72f;
            for (int r = 0; r < rings; r++)
            {
                float t0 = r / (float)rings, t1 = (r + 1) / (float)rings;
                float y0 = Mathf.Sin(t0 * Mathf.PI * 0.5f) * height;
                float y1 = Mathf.Sin(t1 * Mathf.PI * 0.5f) * height;
                float r0 = Mathf.Cos(t0 * Mathf.PI * 0.5f) * radius;
                float r1 = Mathf.Cos(t1 * Mathf.PI * 0.5f) * radius;
                for (int s = 0; s < segs; s++)
                {
                    float a = (s + 0.5f) / segs * Mathf.PI * 2f;
                    Vector3 c = new(Mathf.Cos(a) * (r0 + r1) * 0.5f, (y0 + y1) * 0.5f, Mathf.Sin(a) * (r0 + r1) * 0.5f);
                    float chord = Mathf.PI * 2f * (r0 + r1) * 0.5f / segs;
                    m.Box(PieceSurface.Glass, c, new Vector3(chord * 0.9f, (y1 - y0) + (r0 - r1) * 0.4f, 0.09f),
                          new Vector3(0f, -a * Mathf.Rad2Deg, 0f));
                }
                m.Cylinder(PieceSurface.Trim, new Vector3(0f, y0, 0f), 0.09f, 0.1f, Vector3.zero);
            }
            for (int s = 0; s < segs; s++)
            {
                float a = s / (float)segs * Mathf.PI * 2f;
                for (int r = 0; r < rings; r++)
                {
                    float t0 = r / (float)rings, t1 = (r + 1) / (float)rings;
                    Vector3 p0 = new(Mathf.Cos(a) * Mathf.Cos(t0 * Mathf.PI * 0.5f) * radius,
                                     Mathf.Sin(t0 * Mathf.PI * 0.5f) * height,
                                     Mathf.Sin(a) * Mathf.Cos(t0 * Mathf.PI * 0.5f) * radius);
                    Vector3 p1 = new(Mathf.Cos(a) * Mathf.Cos(t1 * Mathf.PI * 0.5f) * radius,
                                     Mathf.Sin(t1 * Mathf.PI * 0.5f) * height,
                                     Mathf.Sin(a) * Mathf.Cos(t1 * Mathf.PI * 0.5f) * radius);
                    Vector3 mid = (p0 + p1) * 0.5f;
                    Vector3 dir = p1 - p0;
                    m.Box(PieceSurface.Trim, mid, new Vector3(0.14f, dir.magnitude, 0.14f),
                          Quaternion.FromToRotation(Vector3.up, dir.normalized).eulerAngles);
                }
            }
            m.Box(PieceSurface.Trim, new Vector3(0f, 0.16f, 0f), new Vector3(Module, 0.32f, Module));
        }

        private static void AddStationColliders(GameObject root, BuildFamily family)
        {
            void Box(Vector3 centre, Vector3 size)
            {
                var c = root.AddComponent<BoxCollider>();
                c.center = centre;
                c.size = size;
            }

            switch (family)
            {
                case BuildFamily.StationFloor:
                    Box(new Vector3(0f, 0.19f, 0f), new Vector3(Module, 0.38f, Module));
                    break;
                case BuildFamily.StationCorridor:
                {
                    float w = Module * 0.62f, h = Storey * 0.82f;
                    Box(new Vector3(0f, 0.19f, 0f), new Vector3(w, 0.38f, Module));
                    Box(new Vector3(0f, h, 0f), new Vector3(w, 0.32f, Module));
                    foreach (float s in new[] { -1f, 1f })
                        Box(new Vector3(s * w * 0.5f, h * 0.5f, 0f), new Vector3(0.3f, h, Module));
                    break;
                }
                case BuildFamily.StationJunction:
                {
                    float h = Storey * 0.82f;
                    Box(new Vector3(0f, 0.19f, 0f), new Vector3(Module, 0.38f, Module));
                    Box(new Vector3(0f, h, 0f), new Vector3(Module, 0.32f, Module));
                    break;
                }
                case BuildFamily.StationDome:
                    Box(new Vector3(0f, Storey * 0.36f, 0f), new Vector3(Module, Storey * 0.72f, Module));
                    break;
                case BuildFamily.StationDock:
                    Box(new Vector3(0f, Storey * 0.5f, 0f), new Vector3(Module, Storey, 0.9f));
                    break;
                default:
                    Box(new Vector3(0f, Storey * 0.5f, 0f), new Vector3(Module, Storey, 0.45f));
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  COLLISION
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Box colliders sized to the piece rather than a mesh collider on the
        /// welded detail. A wall is a wall: the player should not catch on a rivet.
        /// </summary>
        private static void AddColliders(GameObject root, BuildFamily family)
        {
            void Box(Vector3 centre, Vector3 size)
            {
                var c = root.AddComponent<BoxCollider>();
                c.center = centre;
                c.size = size;
            }

            switch (family)
            {
                case BuildFamily.Foundation:
                    Box(new Vector3(0f, DeckTop * 0.5f, 0f), new Vector3(Module, DeckTop, Module));
                    break;
                case BuildFamily.Floor:
                    Box(new Vector3(0f, 0.19f, 0f), new Vector3(Module, 0.38f, Module));
                    break;
                case BuildFamily.FloorHatch:
                {
                    float band = (Module - HatchW) * 0.5f;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Box(new Vector3(s * (HalfModule - band * 0.5f), 0.19f, 0f), new Vector3(band, 0.38f, Module));
                        Box(new Vector3(0f, 0.19f, s * (HalfModule - band * 0.5f)), new Vector3(HatchW, 0.38f, band));
                    }
                    break;
                }
                case BuildFamily.Wall:
                    Box(new Vector3(0f, Storey * 0.5f, 0f), new Vector3(Module, Storey, WallThick + 0.2f));
                    break;
                case BuildFamily.HalfWall:
                    Box(new Vector3(0f, 1.45f, 0f), new Vector3(Module, 2.9f, WallThick + 0.3f));
                    break;
                case BuildFamily.Doorway:
                case BuildFamily.WallFrame:
                {
                    float holeW = family == BuildFamily.Doorway ? DoorW : GarageW;
                    float holeH = family == BuildFamily.Doorway ? DoorH : GarageH;
                    float side = (Module - holeW) * 0.5f;
                    foreach (float s in new[] { -1f, 1f })
                        Box(new Vector3(s * (HalfModule - side * 0.5f), Storey * 0.5f, 0f),
                            new Vector3(side, Storey, WallThick + 0.2f));
                    Box(new Vector3(0f, holeH + (Storey - holeH) * 0.5f, 0f),
                        new Vector3(holeW, Storey - holeH, WallThick + 0.2f));
                    break;
                }
                case BuildFamily.Window:
                {
                    const float w = 3.6f, h = 2.1f;
                    float bottom = (Storey - h) * 0.5f;
                    float side = (Module - w) * 0.5f;
                    foreach (float s in new[] { -1f, 1f })
                        Box(new Vector3(s * (HalfModule - side * 0.5f), Storey * 0.5f, 0f),
                            new Vector3(side, Storey, WallThick + 0.2f));
                    Box(new Vector3(0f, bottom * 0.5f, 0f), new Vector3(w, bottom, WallThick + 0.2f));
                    Box(new Vector3(0f, bottom + h + (Storey - bottom - h) * 0.5f, 0f),
                        new Vector3(w, Storey - bottom - h, WallThick + 0.2f));
                    break;
                }
                case BuildFamily.Door:
                    Box(new Vector3(0f, DoorH * 0.5f, 0f), new Vector3(DoorW - 0.16f, DoorH - 0.12f, 0.24f));
                    break;
                case BuildFamily.GarageDoor:
                    Box(new Vector3(0f, GarageH * 0.5f, 0f), new Vector3(GarageW - 0.2f, GarageH, 0.24f));
                    break;
                case BuildFamily.Pillar:
                    Box(new Vector3(0f, Storey * 0.5f, 0f), new Vector3(0.86f, Storey, 0.86f));
                    break;
                case BuildFamily.Roof:
                    Box(new Vector3(0f, Module * 0.24f, 0f), new Vector3(Module, 0.5f, Module * 1.06f));
                    break;
                case BuildFamily.Stairs:
                {
                    const int steps = 15;
                    float rise = Storey / steps, run = Module / steps;
                    for (int i = 0; i < steps; i++)
                        Box(new Vector3(0f, rise * (i + 0.5f) * 0.5f, -HalfModule + run * (i + 0.5f)),
                            new Vector3(Module - 0.6f, rise * (i + 1), run));
                    break;
                }
                default:
                    Box(new Vector3(0f, Storey * 0.5f, 0f), new Vector3(Module, Storey, WallThick + 0.2f));
                    break;
            }
        }
    }
}
#endif
