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
        /// <summary>Hatch opening in a floor slab. Public: the setup step sizes the lid hinge from it.</summary>
        public const float HatchW = 2.60f;

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
            /// <summary>
            /// A log or pipe lying along its local Y, then rotated into place.
            /// <paramref name="uvTiles"/> overrides how many times the surface
            /// repeats along the length: the wall textures are authored as courses
            /// seen face-on, and letting one repeat per texel turns a single post
            /// into a stack of forty rings.
            /// </summary>
            public void Cylinder(PieceSurface surface, Vector3 centre, float radius, float length, Vector3 euler,
                                 float uvTiles = 0f)
                => Add(surface, CylinderMesh(radius, length, 12, uvTiles),
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
                // Wound so cross(p1 - p0, p2 - p0) points ALONG the face normal,
                // which is the convention the rest of the engine's generated meshes
                // use (see WheelMeshFactory.Lathe). Reversed, every box renders
                // inside-out: the near wall is culled and you see the far inner
                // wall instead, which reads as a half-invisible building piece.
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
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

        private static Mesh CylinderMesh(float radius, float length, int sides = 12, float uvTiles = 0f)
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
                float vt = uvTiles > 0f ? uvTiles : length / Texel;
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

        /// <summary>Which edges of a clad panel get the framing timbers / border bars.</summary>
        [System.Flags]
        private enum Edge { None = 0, Left = 1, Right = 2, Top = 4, Bottom = 8, All = 15 }

        /// <summary>
        /// The strong exterior face of a rectangular panel, in the piece's local
        /// frame. <paramref name="z"/> is the outward face plane.
        ///
        /// Every tier starts with a SOLID sheathing box spanning the whole panel.
        /// The cladding on top of it is relief, never the wall itself - laying
        /// rounded logs edge to edge and calling that the wall is what left the
        /// first pass see-through between every course.
        /// </summary>
        private static void CladSkin(PieceMesh m, BuildTier tier, Vector3 centre, float width, float height,
                                     float z, float depth, Edge edges = Edge.All)
        {
            // The one line that makes a piece solid.
            m.Box(PieceSurface.Skin, new Vector3(centre.x, centre.y, z - depth * 0.5f),
                  new Vector3(width, height, depth));

            float left = centre.x - width * 0.5f, right = centre.x + width * 0.5f;
            float bottom = centre.y - height * 0.5f, top = centre.y + height * 0.5f;

            switch (tier)
            {
                case BuildTier.Wood:
                {
                    // Horizontal boards filling the field between round framing
                    // timbers - the shape of a log-cabin wall, not a stack of pipes.
                    float inset = (edges & (Edge.Left | Edge.Right)) != 0 ? 0.38f : 0.06f;
                    float vInset = (edges & (Edge.Top | Edge.Bottom)) != 0 ? 0.34f : 0.04f;
                    float fieldW = Mathf.Max(0.2f, width - inset * 2f);
                    float fieldH = Mathf.Max(0.2f, height - vInset * 2f);

                    int boards = Mathf.Max(2, Mathf.RoundToInt(fieldH / 0.46f));
                    float bh = fieldH / boards;
                    for (int i = 0; i < boards; i++)
                    {
                        float y = bottom + vInset + bh * (i + 0.5f);
                        float relief = (i & 1) == 0 ? 0.015f : 0f;
                        m.Box(PieceSurface.Skin, new Vector3(centre.x, y, z + 0.055f + relief),
                              new Vector3(fieldW, bh * 1.03f, 0.11f));
                    }

                    // Round framing timbers with the ends proud of the panel.
                    const float post = 0.23f;
                    float postTiles = Mathf.Max(1.5f, height * 0.4f);
                    float railTiles = Mathf.Max(1.5f, width * 0.4f);
                    if ((edges & Edge.Left) != 0)
                        m.Cylinder(PieceSurface.Trim, new Vector3(left + post, centre.y, z + 0.10f),
                                   post, height + post * 1.2f, Vector3.zero, postTiles);
                    if ((edges & Edge.Right) != 0)
                        m.Cylinder(PieceSurface.Trim, new Vector3(right - post, centre.y, z + 0.10f),
                                   post, height + post * 1.2f, Vector3.zero, postTiles);
                    if ((edges & Edge.Top) != 0)
                        m.Cylinder(PieceSurface.Trim, new Vector3(centre.x, top - post * 0.85f, z + 0.10f),
                                   post * 0.92f, width, new Vector3(0f, 0f, 90f), railTiles);
                    if ((edges & Edge.Bottom) != 0)
                        m.Cylinder(PieceSurface.Trim, new Vector3(centre.x, bottom + post * 0.85f, z + 0.10f),
                                   post * 0.92f, width, new Vector3(0f, 0f, 90f), railTiles);
                    break;
                }

                case BuildTier.Stone:
                {
                    // Small fitted blocks in running bond inside a heavier quoined
                    // border, the way a dressed stone wall is actually laid.
                    float border = (edges & (Edge.Left | Edge.Right)) != 0 ? 0.34f : 0.04f;
                    float vBorder = (edges & (Edge.Top | Edge.Bottom)) != 0 ? 0.30f : 0.04f;
                    float fieldW = Mathf.Max(0.2f, width - border * 2f);
                    float fieldH = Mathf.Max(0.2f, height - vBorder * 2f);

                    int rows = Mathf.Max(3, Mathf.RoundToInt(fieldH / 0.42f));
                    float rh = fieldH / rows;
                    int cols = Mathf.Max(3, Mathf.RoundToInt(fieldW / 0.62f));
                    float cw = fieldW / cols;
                    for (int r = 0; r < rows; r++)
                    {
                        float off = (r & 1) == 0 ? 0f : cw * 0.5f;
                        for (int c = -1; c <= cols; c++)
                        {
                            float x = left + border + off + cw * (c + 0.5f);
                            float halfBrick = cw * 0.46f;
                            if (x - halfBrick < left + border - 0.01f || x + halfBrick > right - border + 0.01f) continue;
                            float n = Mathf.PerlinNoise(c * 2.3f + r * 0.7f, r * 1.9f);
                            m.Box(PieceSurface.Skin,
                                  new Vector3(x, bottom + vBorder + rh * (r + 0.5f), z + 0.045f + n * 0.022f),
                                  new Vector3(cw * 0.92f, rh * 0.84f, 0.09f));
                        }
                    }
                    StoneBorder(m, edges, left, right, bottom, top, z, border, vBorder);
                    break;
                }

                case BuildTier.Iron:
                {
                    // Salvaged plates of mixed size screwed over the sheathing,
                    // inside a riveted border bar.
                    float border = (edges & (Edge.Left | Edge.Right)) != 0 ? 0.30f : 0.04f;
                    float vBorder = (edges & (Edge.Top | Edge.Bottom)) != 0 ? 0.28f : 0.04f;
                    float fieldW = Mathf.Max(0.2f, width - border * 2f);
                    float fieldH = Mathf.Max(0.2f, height - vBorder * 2f);

                    int rows = Mathf.Max(2, Mathf.RoundToInt(fieldH / 1.35f));
                    float rh = fieldH / rows;
                    for (int r = 0; r < rows; r++)
                    {
                        int cols = 1 + ((r + (int)(width * 3f)) % 2);   // 1 or 2 patches per band
                        float cw = fieldW / cols;
                        for (int c = 0; c < cols; c++)
                        {
                            float x = left + border + cw * (c + 0.5f);
                            float y = bottom + vBorder + rh * (r + 0.5f);
                            float n = Mathf.PerlinNoise(c * 5.1f + r * 2.7f, r * 3.3f);
                            m.Box(PieceSurface.Skin, new Vector3(x, y, z + 0.045f + n * 0.02f),
                                  new Vector3(cw * 0.985f, rh * 0.97f, 0.06f),
                                  new Vector3(0f, 0f, (n - 0.5f) * 1.6f));
                            int screws = Mathf.Max(2, Mathf.RoundToInt(cw / 1.1f));
                            for (int k = 0; k < screws; k++)
                            {
                                float sx = x - cw * 0.45f + cw * 0.9f * (screws == 1 ? 0.5f : k / (float)(screws - 1));
                                m.Box(PieceSurface.Trim, new Vector3(sx, y + rh * 0.42f, z + 0.085f), new Vector3(0.1f, 0.1f, 0.04f));
                                m.Box(PieceSurface.Trim, new Vector3(sx, y - rh * 0.42f, z + 0.085f), new Vector3(0.1f, 0.1f, 0.04f));
                            }
                        }
                    }
                    MetalBorder(m, edges, left, right, bottom, top, z, border, vBorder, 0.075f, 0.6f);
                    break;
                }

                default:
                {
                    // Armour: wide recessed panels with clean seams, a heavy border
                    // bar and a run of vent slots - dark, flat and deliberate.
                    float border = (edges & (Edge.Left | Edge.Right)) != 0 ? 0.32f : 0.04f;
                    float vBorder = (edges & (Edge.Top | Edge.Bottom)) != 0 ? 0.30f : 0.04f;
                    float fieldW = Mathf.Max(0.2f, width - border * 2f);
                    float fieldH = Mathf.Max(0.2f, height - vBorder * 2f);

                    int rows = Mathf.Max(2, Mathf.RoundToInt(fieldH / 1.6f));
                    float rh = fieldH / rows;
                    for (int r = 0; r < rows; r++)
                    {
                        float y = bottom + vBorder + rh * (r + 0.5f);
                        m.Box(PieceSurface.Skin, new Vector3(centre.x, y, z + 0.05f),
                              new Vector3(fieldW * 0.995f, rh * 0.93f, 0.08f));
                        if (r == rows - 1 && fieldW > 1.6f)
                            for (int v = 0; v < 5; v++)
                                m.Box(PieceSurface.Frame,
                                      new Vector3(centre.x - fieldW * 0.16f + v * fieldW * 0.08f, y, z + 0.095f),
                                      new Vector3(0.05f, rh * 0.4f, 0.03f));
                    }
                    MetalBorder(m, edges, left, right, bottom, top, z, border, vBorder, 0.09f, 0.85f);
                    break;
                }
            }
        }

        /// <summary>Quoined stone border: larger blocks turning each framed edge.</summary>
        private static void StoneBorder(PieceMesh m, Edge edges, float left, float right, float bottom, float top,
                                        float z, float border, float vBorder)
        {
            if ((edges & Edge.Left) != 0) QuoinColumn(m, left + border * 0.5f, bottom, top, z, border);
            if ((edges & Edge.Right) != 0) QuoinColumn(m, right - border * 0.5f, bottom, top, z, border);
            if ((edges & Edge.Top) != 0)
                m.Box(PieceSurface.Trim, new Vector3((left + right) * 0.5f, top - vBorder * 0.5f, z + 0.05f),
                      new Vector3(right - left, vBorder, 0.13f));
            if ((edges & Edge.Bottom) != 0)
                m.Box(PieceSurface.Trim, new Vector3((left + right) * 0.5f, bottom + vBorder * 0.5f, z + 0.05f),
                      new Vector3(right - left, vBorder, 0.13f));
        }

        private static void QuoinColumn(PieceMesh m, float x, float bottom, float top, float z, float border)
        {
            int blocks = Mathf.Max(3, Mathf.RoundToInt((top - bottom) / 0.75f));
            float bh = (top - bottom) / blocks;
            for (int i = 0; i < blocks; i++)
                m.Box(PieceSurface.Trim, new Vector3(x, bottom + bh * (i + 0.5f), z + 0.055f),
                      new Vector3(border * (i % 2 == 0 ? 1.9f : 1.35f), bh * 0.9f, 0.14f));
        }

        /// <summary>Flat border bar with a run of rivets along each framed edge.</summary>
        private static void MetalBorder(PieceMesh m, Edge edges, float left, float right, float bottom, float top,
                                        float z, float border, float vBorder, float thick, float rivetSpacing)
        {
            void Bar(Vector3 centre, Vector3 size, bool vertical)
            {
                m.Box(PieceSurface.Trim, centre, size);
                float run = vertical ? size.y : size.x;
                int rivets = Mathf.Max(2, Mathf.RoundToInt(run / rivetSpacing));
                for (int i = 0; i < rivets; i++)
                {
                    float t = rivets == 1 ? 0.5f : i / (float)(rivets - 1);
                    Vector3 p = vertical
                        ? new Vector3(centre.x, centre.y - run * 0.46f + run * 0.92f * t, z + thick + 0.04f)
                        : new Vector3(centre.x - run * 0.46f + run * 0.92f * t, centre.y, z + thick + 0.04f);
                    m.Box(PieceSurface.Trim, p, new Vector3(0.13f, 0.13f, 0.05f));
                }
            }

            float h = top - bottom, w = right - left;
            if ((edges & Edge.Left) != 0) Bar(new Vector3(left + border * 0.5f, (bottom + top) * 0.5f, z + thick * 0.5f), new Vector3(border, h, thick), true);
            if ((edges & Edge.Right) != 0) Bar(new Vector3(right - border * 0.5f, (bottom + top) * 0.5f, z + thick * 0.5f), new Vector3(border, h, thick), true);
            if ((edges & Edge.Top) != 0) Bar(new Vector3((left + right) * 0.5f, top - vBorder * 0.5f, z + thick * 0.5f), new Vector3(w, vBorder, thick), false);
            if ((edges & Edge.Bottom) != 0) Bar(new Vector3((left + right) * 0.5f, bottom + vBorder * 0.5f, z + thick * 0.5f), new Vector3(w, vBorder, thick), false);
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

            // Each sub-panel only frames the edges that are genuinely on the
            // outside of the piece; the edges facing the opening get the reveal
            // lining instead, so the hole reads as cut rather than assembled.
            if (sideW > 0.05f)
            {
                CladSkin(m, tier, new Vector3(-(halfW - sideW * 0.5f), height * 0.5f, 0f), sideW, height, z, thick,
                         Edge.Left | Edge.Top | Edge.Bottom);
                CladFrame(m, tier, new Vector3(-(halfW - sideW * 0.5f), height * 0.5f, 0f), sideW, height, -z);
                CladSkin(m, tier, new Vector3(halfW - sideW * 0.5f, height * 0.5f, 0f), sideW, height, z, thick,
                         Edge.Right | Edge.Top | Edge.Bottom);
                CladFrame(m, tier, new Vector3(halfW - sideW * 0.5f, height * 0.5f, 0f), sideW, height, -z);
            }
            if (holeBottom > 0.05f)
            {
                CladSkin(m, tier, new Vector3(0f, holeBottom * 0.5f, 0f), holeW, holeBottom, z, thick, Edge.Bottom);
                CladFrame(m, tier, new Vector3(0f, holeBottom * 0.5f, 0f), holeW, holeBottom, -z);
            }
            if (height - holeTop > 0.05f)
            {
                float capH = height - holeTop;
                CladSkin(m, tier, new Vector3(0f, holeTop + capH * 0.5f, 0f), holeW, capH, z, thick, Edge.Top);
                CladFrame(m, tier, new Vector3(0f, holeTop + capH * 0.5f, 0f), holeW, capH, -z);
            }

            // Reveal lining: a returned jamb all the way through the wall.
            Reveal(m, tier, new Vector3(0f, holeTop + 0.1f, 0f), new Vector3(holeW + 0.4f, 0.2f, thick + 0.12f));
            if (holeBottom > 0.05f)
                Reveal(m, tier, new Vector3(0f, holeBottom - 0.1f, 0f), new Vector3(holeW + 0.4f, 0.2f, thick + 0.12f));
            foreach (float sx in new[] { -1f, 1f })
                Reveal(m, tier, new Vector3(sx * (holeW * 0.5f + 0.1f), holeBottom + holeH * 0.5f, 0f),
                       new Vector3(0.2f, holeH + 0.2f, thick + 0.12f));
        }

        /// <summary>Jamb lining. Timber tiers get a rounded return, metal a flat bar.</summary>
        private static void Reveal(PieceMesh m, BuildTier tier, Vector3 centre, Vector3 size)
        {
            if (tier == BuildTier.Wood && size.x > size.y)
                m.Cylinder(PieceSurface.Trim, centre, size.y * 0.55f, size.x, new Vector3(0f, 0f, 90f));
            else if (tier == BuildTier.Wood)
                m.Cylinder(PieceSurface.Trim, centre, size.x * 0.55f, size.y, Vector3.zero);
            else
                m.Box(PieceSurface.Trim, centre, size);
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
                case BuildFamily.FloorHatch: Floor(m, tier, true); break;
                case BuildFamily.Wall:       Wall(m, tier); break;
                case BuildFamily.Doorway:    CladPanelWithHole(m, tier, Module, Storey, DoorW, DoorH, 0f, WallThick); break;
                case BuildFamily.WallFrame:  CladPanelWithHole(m, tier, Module, Storey, GarageW, GarageH, 0f, WallThick); GarageTrack(m, tier); break;
                case BuildFamily.Window:     Window(m, tier); break;
                case BuildFamily.Door:       DoorLeaf(m, tier); break;
                case BuildFamily.GarageDoor: GarageDoor(m, tier); break;
                case BuildFamily.WindowPane: WindowPane(m, tier); break;
                case BuildFamily.HatchLid:   HatchLidPanel(m, tier); break;
                case BuildFamily.Stairs:     Stairs(m, tier); break;
                case BuildFamily.Railing:    Railing(m, tier); break;
                case BuildFamily.Roof:       Roof(m, tier); break;
                case BuildFamily.Pillar:     Pillar(m, tier); break;
                case BuildFamily.HalfWall:   HalfWall(m, tier); break;
                default:                     Wall(m, tier); break;
            }
            m.Commit(root, tier, meshAssetPath);
            AddColliders(root, family);
        }

        /// <summary>
        /// The horizontal face of a deck or slab: what you stand on. Relief only —
        /// the solid volume is laid down by the caller first.
        /// </summary>
        private static void DeckSurface(PieceMesh m, BuildTier tier, float top, float size, float hole)
        {
            float holeHalf = hole * 0.5f + 0.1f;
            bool Skip(float x, float z) => hole > 0f && Mathf.Abs(x) < holeHalf && Mathf.Abs(z) < holeHalf;

            // A board or rib runs the full depth of the slab, so it cannot simply
            // be dropped when it crosses the opening - it has to be cut into the
            // two lengths either side of it, or the deck planks its own hatch shut.
            // lift raises an element above the deck plane (tread ribs); zero keeps
            // it flush and inset into it (boards, plates). Sizes stay positive:
            // a negative box extent produces inside-out geometry, not a recess.
            void Run(PieceSurface surface, float x, float widthX, float thick, float depth, float lift = 0f)
            {
                float y = top - thick * 0.5f + lift;
                if (hole <= 0f || Mathf.Abs(x) >= holeHalf)
                {
                    m.Box(surface, new Vector3(x, y, 0f), new Vector3(widthX, thick, depth));
                    return;
                }
                float band = (depth - hole) * 0.5f;
                if (band <= 0.05f) return;
                foreach (float s in new[] { -1f, 1f })
                    m.Box(surface, new Vector3(x, y, s * (depth * 0.5f - band * 0.5f)),
                          new Vector3(widthX, thick, band));
            }

            switch (tier)
            {
                case BuildTier.Wood:
                {
                    int boards = Mathf.Max(6, Mathf.RoundToInt(size / 0.72f));
                    float bw = size / boards;
                    for (int i = 0; i < boards; i++)
                        Run(PieceSurface.Skin, -size * 0.5f + bw * (i + 0.5f), bw * 0.94f, 0.09f, size - 0.1f);
                    break;
                }
                case BuildTier.Stone:
                {
                    int n = Mathf.Max(6, Mathf.RoundToInt(size / 0.68f));
                    float cw = size / n;
                    for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                    {
                        float x = -size * 0.5f + cw * (i + 0.5f);
                        float zz = -size * 0.5f + cw * (j + 0.5f);
                        if (Skip(x, zz)) continue;
                        float k = Mathf.PerlinNoise(i * 1.7f, j * 1.7f);
                        m.Box(PieceSurface.Skin, new Vector3(x, top - 0.04f + k * 0.02f, zz),
                              new Vector3(cw * 0.9f, 0.1f, cw * 0.9f), new Vector3(0f, (k - 0.5f) * 5f, 0f));
                    }
                    break;
                }
                case BuildTier.Iron:
                {
                    // Plate first, then the raised tread ribs on top of it.
                    int plates = Mathf.Max(4, Mathf.RoundToInt(size / 1.6f));
                    float pw = size / plates;
                    for (int i = 0; i < plates; i++)
                        Run(PieceSurface.Skin, -size * 0.5f + pw * (i + 0.5f), pw * 0.98f, 0.08f, size - 0.12f);

                    int ribs = Mathf.Max(6, Mathf.RoundToInt(size / 0.5f));
                    for (int i = 0; i < ribs; i++)
                        Run(PieceSurface.Trim, -size * 0.5f + size * (i + 0.5f) / ribs, 0.09f, 0.04f, size - 0.3f, 0.04f);
                    break;
                }
                default:
                {
                    int panels = 3;
                    float pw = size / panels;
                    for (int i = 0; i < panels; i++)
                    for (int j = 0; j < panels; j++)
                    {
                        float x = -size * 0.5f + pw * (i + 0.5f);
                        float zz = -size * 0.5f + pw * (j + 0.5f);
                        if (Skip(x, zz)) continue;
                        m.Box(PieceSurface.Skin, new Vector3(x, top - 0.035f, zz),
                              new Vector3(pw * 0.94f, 0.07f, pw * 0.94f));
                    }
                    break;
                }
            }
        }

        /// <summary>The visible side of a deck: log beams, block courses, ribs or panels.</summary>
        private static void Skirt(PieceMesh m, BuildTier tier, float top, float bottom, float size)
        {
            float h = Mathf.Max(0.12f, top - bottom), mid = (top + bottom) * 0.5f, half = size * 0.5f;

            for (int side = 0; side < 4; side++)
            {
                bool alongX = side < 2;
                float sign = (side % 2 == 0) ? 1f : -1f;
                Vector3 outward = alongX ? new Vector3(0f, 0f, sign * (half - 0.06f)) : new Vector3(sign * (half - 0.06f), 0f, 0f);
                Vector3 span = alongX ? new Vector3(size, 0f, 0f) : new Vector3(0f, 0f, size);
                float run = size;

                switch (tier)
                {
                    case BuildTier.Wood:
                        m.Cylinder(PieceSurface.Trim, outward + new Vector3(0f, top - 0.22f, 0f), 0.22f, run,
                                   alongX ? new Vector3(0f, 0f, 90f) : new Vector3(90f, 0f, 0f));
                        m.Cylinder(PieceSurface.Trim, outward + new Vector3(0f, bottom + 0.2f, 0f), 0.2f, run,
                                   alongX ? new Vector3(0f, 0f, 90f) : new Vector3(90f, 0f, 0f));
                        break;
                    case BuildTier.Stone:
                    {
                        int rows = Mathf.Max(2, Mathf.RoundToInt(h / 0.42f));
                        int cols = Mathf.Max(4, Mathf.RoundToInt(run / 0.72f));
                        for (int r = 0; r < rows; r++)
                        for (int c = 0; c < cols; c++)
                        {
                            float t = (c + 0.5f) / cols - 0.5f + ((r & 1) == 0 ? 0f : 0.5f / cols);
                            Vector3 p = outward + span * t + new Vector3(0f, bottom + h * (r + 0.5f) / rows, 0f);
                            m.Box(PieceSurface.Skin, p,
                                  alongX ? new Vector3(run / cols * 0.9f, h / rows * 0.85f, 0.12f)
                                         : new Vector3(0.12f, h / rows * 0.85f, run / cols * 0.9f));
                        }
                        break;
                    }
                    case BuildTier.Iron:
                    {
                        int ribs = Mathf.Max(6, Mathf.RoundToInt(run / 0.42f));
                        for (int c = 0; c < ribs; c++)
                        {
                            float t = (c + 0.5f) / ribs - 0.5f;
                            Vector3 p = outward + span * t + new Vector3(0f, mid, 0f);
                            m.Box(PieceSurface.Skin, p,
                                  alongX ? new Vector3(run / ribs * 0.6f, h * 0.94f, 0.1f)
                                         : new Vector3(0.1f, h * 0.94f, run / ribs * 0.6f));
                        }
                        break;
                    }
                    default:
                    {
                        int panels = 3;
                        for (int c = 0; c < panels; c++)
                        {
                            float t = (c + 0.5f) / panels - 0.5f;
                            Vector3 p = outward + span * t + new Vector3(0f, mid, 0f);
                            m.Box(PieceSurface.Skin, p,
                                  alongX ? new Vector3(run / panels * 0.95f, h * 0.9f, 0.1f)
                                         : new Vector3(0.1f, h * 0.9f, run / panels * 0.95f));
                        }
                        break;
                    }
                }
            }

            // Corner quoins / posts, which is what stops a deck reading as a crate.
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
            {
                Vector3 p = new(x * (half - 0.17f), mid, z * (half - 0.17f));
                if (tier == BuildTier.Wood)
                    m.Cylinder(PieceSurface.Trim, p, 0.23f, h * 1.02f, Vector3.zero);
                else
                    m.Box(PieceSurface.Trim, p, new Vector3(0.42f, h * 1.02f, 0.42f));
            }
        }

        private static void Foundation(PieceMesh m, BuildTier tier)
        {
            const float deck = 0.9f;
            float top = DeckTop, bottom = top - deck;

            // Solid slab first. Everything after this is relief on its faces.
            m.Box(PieceSurface.Skin, new Vector3(0f, top - deck * 0.5f, 0f), new Vector3(Module, deck, Module));
            m.Box(PieceSurface.Frame, new Vector3(0f, bottom + 0.06f, 0f), new Vector3(Module - 0.1f, 0.12f, Module - 0.1f));

            DeckSurface(m, tier, top, Module, 0f);
            Skirt(m, tier, top, bottom, Module);

            // Piers carrying the deck off uneven ground.
            foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                m.Box(PieceSurface.Frame,
                      new Vector3(x * (HalfModule - 0.5f), bottom * 0.5f, z * (HalfModule - 0.5f)),
                      new Vector3(0.62f, bottom, 0.62f));
        }

        private static void Floor(PieceMesh m, BuildTier tier, bool hatch)
        {
            const float slab = 0.42f;
            float half = HatchW * 0.5f;

            if (!hatch)
            {
                m.Box(PieceSurface.Skin, new Vector3(0f, slab * 0.5f, 0f), new Vector3(Module, slab, Module));
                DeckSurface(m, tier, slab, Module, 0f);
            }
            else
            {
                float band = (Module - HatchW) * 0.5f;
                foreach (float s in new[] { -1f, 1f })
                {
                    m.Box(PieceSurface.Skin, new Vector3(s * (HalfModule - band * 0.5f), slab * 0.5f, 0f),
                          new Vector3(band, slab, Module));
                    m.Box(PieceSurface.Skin, new Vector3(0f, slab * 0.5f, s * (HalfModule - band * 0.5f)),
                          new Vector3(HatchW, slab, band));
                }
                DeckSurface(m, tier, slab, Module, HatchW);
                foreach (float s in new[] { -1f, 1f })
                {
                    m.Box(PieceSurface.Trim, new Vector3(s * (half + 0.1f), slab * 0.5f + 0.04f, 0f),
                          new Vector3(0.2f, slab + 0.08f, HatchW + 0.4f));
                    m.Box(PieceSurface.Trim, new Vector3(0f, slab * 0.5f + 0.04f, s * (half + 0.1f)),
                          new Vector3(HatchW + 0.4f, slab + 0.08f, 0.2f));
                }
            }

            Skirt(m, tier, slab, -0.34f, Module);

            // Joists: the ceiling of the room below.
            int joists = 6;
            for (int i = 0; i < joists; i++)
            {
                float z = -HalfModule + Module * (i + 0.5f) / joists;
                if (hatch && Mathf.Abs(z) < half + 0.2f) continue;
                m.Box(PieceSurface.Frame, new Vector3(0f, -0.17f, z), new Vector3(Module - 0.5f, 0.32f, 0.26f));
            }
        }

        /// <summary>The glazed insert that fits a Window frame.</summary>
        private static void WindowPane(PieceMesh m, BuildTier tier)
        {
            const float w = 3.6f, h = 2.1f;
            float y = (Storey - h) * 0.5f + h * 0.5f;

            m.Box(PieceSurface.Glass, new Vector3(0f, y, 0f), new Vector3(w - 0.22f, h - 0.22f, 0.06f));
            // Sash: a perimeter frame plus glazing bars, so the pane is a made
            // object rather than a floating sheet.
            foreach (float sy in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(0f, y + sy * (h * 0.5f - 0.06f), 0f), new Vector3(w, 0.13f, 0.14f));
            foreach (float sx in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(sx * (w * 0.5f - 0.06f), y, 0f), new Vector3(0.13f, h, 0.14f));
            m.Box(PieceSurface.Trim, new Vector3(0f, y, 0f), new Vector3(0.09f, h - 0.12f, 0.11f));
            m.Box(PieceSurface.Trim, new Vector3(0f, y, 0f), new Vector3(w - 0.12f, 0.09f, 0.11f));
        }

        /// <summary>
        /// The hatch lid itself. Modelled closed and flush at the origin: the
        /// setup step re-parents it under a hinge at the rear edge, and the
        /// runtime component swings it from there.
        /// </summary>
        private static void HatchLidPanel(PieceMesh m, BuildTier tier)
        {
            float w = HatchW - 0.14f;
            m.Box(PieceSurface.Skin, new Vector3(0f, 0.07f, 0f), new Vector3(w, 0.14f, w));
            m.Box(PieceSurface.Frame, new Vector3(0f, 0.15f, 0f), new Vector3(w - 0.3f, 0.05f, w - 0.3f));
            foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(sx * w * 0.4f, 0.16f, sz * w * 0.4f), new Vector3(0.16f, 0.06f, 0.16f));
            // Hinge barrel along the rear edge and a pull handle at the front.
            m.Cylinder(PieceSurface.Trim, new Vector3(0f, 0.12f, -w * 0.5f), 0.08f, w * 0.85f,
                       new Vector3(0f, 0f, 90f), 2f);
            m.Cylinder(PieceSurface.Trim, new Vector3(0f, 0.2f, w * 0.34f), 0.05f, w * 0.34f,
                       new Vector3(0f, 0f, 90f), 1.5f);
        }

        /// <summary>
        /// The fold-out ladder, built pointing DOWN from its own origin with a
        /// unit height of one metre, so the runtime component can unroll it by
        /// scaling Y rather than rebuilding geometry every frame.
        /// </summary>
        public static void BuildLadder(GameObject root, BuildTier tier, string meshAssetPath)
        {
            var m = new PieceMesh();
            const float width = 0.9f, rungs = 10f;

            foreach (float sx in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(sx * width * 0.5f, -0.5f, 0f), new Vector3(0.09f, 1f, 0.09f));
            for (int i = 0; i < rungs; i++)
            {
                float y = -(i + 0.5f) / rungs;
                m.Cylinder(PieceSurface.Trim, new Vector3(0f, y, 0f), 0.045f, width, new Vector3(0f, 0f, 90f), 1f);
            }
            m.Commit(root, tier, meshAssetPath);
        }

        private static void Wall(PieceMesh m, BuildTier tier)
        {
            float z = WallThick * 0.5f;
            CladSkin(m, tier, new Vector3(0f, Storey * 0.5f, 0f), Module, Storey, z, WallThick, Edge.All);
            CladFrame(m, tier, new Vector3(0f, Storey * 0.5f, 0f), Module, Storey, -z);
        }

        private static void Window(PieceMesh m, BuildTier tier)
        {
            const float w = 3.6f, h = 2.1f;
            float bottom = (Storey - h) * 0.5f;
            // Frame only. The glazing is a Window Pane the player fits themselves,
            // the same way a Doorway holds a Door: an empty frame is a firing port,
            // and a broken pane should not cost you the wall.
            CladPanelWithHole(m, tier, Module, Storey, w, h, bottom, WallThick);
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Trim, new Vector3(s * (w * 0.5f - 0.06f), bottom + h * 0.5f, 0f),
                      new Vector3(0.1f, h - 0.12f, WallThick + 0.16f));
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

            // Treads and risers form the flight; the underside remains a clean
            // diagonal carried by two stringers instead of a full solid wedge.
            for (int i = 0; i < steps; i++)
            {
                float y = rise * (i + 0.5f);
                float z = -HalfModule + run * (i + 0.5f);
                m.Box(PieceSurface.Skin, new Vector3(0f, y + rise * 0.42f, z), new Vector3(width, rise * 0.22f, run * 1.05f));
                m.Box(PieceSurface.Frame, new Vector3(0f, y, z - run * 0.45f), new Vector3(width - 0.2f, rise, 0.12f));
            }

            // Two exposed diagonal stringers give the open underside its slope.
            float diag = Mathf.Sqrt(Module * Module + Storey * Storey);
            float pitch = Mathf.Atan2(Storey, Module) * Mathf.Rad2Deg;
            foreach (float s in new[] { -1f, 1f })
                m.Box(PieceSurface.Frame, new Vector3(s * width * 0.5f, Storey * 0.5f - 0.2f, 0f),
                      new Vector3(0.22f, 0.5f, diag), new Vector3(-pitch, 0f, 0f));
        }

        private static void Railing(PieceMesh m, BuildTier tier)
        {
            const float height = 1.18f;
            const float post = 0.14f;
            const int bays = 4;
            float railThickness = tier >= BuildTier.Iron ? 0.12f : 0.16f;

            for (int i = 0; i <= bays; i++)
            {
                float x = -HalfModule + Module * i / bays;
                m.Box(PieceSurface.Frame, new Vector3(x, height * 0.5f, 0f),
                    new Vector3(post, height, post));
            }

            m.Box(PieceSurface.Trim, new Vector3(0f, height, 0f),
                new Vector3(Module, railThickness, railThickness));
            m.Box(PieceSurface.Trim, new Vector3(0f, height * 0.53f, 0f),
                new Vector3(Module, railThickness * 0.8f, railThickness * 0.8f));
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
                m.Cylinder(PieceSurface.Trim, new Vector3(0f, Storey * 0.5f, 0f), 0.30f, Storey - 0.36f,
                           Vector3.zero, 3f);
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
            CladSkin(m, tier, new Vector3(0f, h * 0.5f, 0f), Module, h, z, WallThick, Edge.All);
            CladFrame(m, tier, new Vector3(0f, h * 0.5f, 0f), Module, h, -z);
            // A capping rail you can rest a rifle on.
            if (tier == BuildTier.Wood)
                m.Cylinder(PieceSurface.Trim, new Vector3(0f, h + 0.12f, 0f), 0.2f, Module, new Vector3(0f, 0f, 90f));
            else
                m.Box(PieceSurface.Trim, new Vector3(0f, h + 0.11f, 0f), new Vector3(Module, 0.22f, WallThick + 0.34f));
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

        /// <summary>
        /// A habitat module, not a glass bubble. A panelled drum you can actually
        /// stand inside carries a door bay and a glazed bay at eye level, banded
        /// top and bottom, under a shallow ribbed cap with viewport arcs and a
        /// lit strip running the join. The first pass was a squashed sphere on a
        /// plate, which read as a drop of water rather than somewhere to live.
        /// </summary>
        private static void StationDome(PieceMesh m)
        {
            const int segs = 16;
            float radius = HalfModule * 0.94f;
            float drum = Storey * 0.46f;
            float cap = Storey * 0.34f;

            // ── Drum: panel bays around the circumference ────────────────
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) / segs * Mathf.PI * 2f;
                Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float chord = Mathf.PI * 2f * radius / segs;
                Vector3 centre = dir * radius + new Vector3(0f, drum * 0.5f, 0f);
                // A panel's local +Z must point along the radius. Rotating +Z about
                // Y by t gives (sin t, 0, cos t), so matching (cos a, 0, sin a)
                // needs t = 90 - a. Using -a instead stands every panel on edge and
                // fans the drum open, which is exactly how the first dome looked.
                float yaw = 90f - a * Mathf.Rad2Deg;

                // Two bays face outward as a door and a viewport; the rest are hull.
                bool door = i == 0;
                bool glazed = i == 4 || i == 12;

                m.Box(PieceSurface.Skin, centre, new Vector3(chord * 0.99f, drum, 0.22f), new Vector3(0f, yaw, 0f));

                if (glazed)
                {
                    m.Box(PieceSurface.Glass, centre + new Vector3(0f, drum * 0.08f, 0f),
                          new Vector3(chord * 0.74f, drum * 0.5f, 0.1f), new Vector3(0f, yaw, 0f));
                    m.Box(PieceSurface.Trim, centre + new Vector3(0f, drum * 0.08f, 0f),
                          new Vector3(chord * 0.82f, drum * 0.58f, 0.06f), new Vector3(0f, yaw, 0f));
                }
                else if (door)
                {
                    m.Box(PieceSurface.Frame, centre + dir * 0.06f + new Vector3(0f, -drum * 0.04f, 0f),
                          new Vector3(chord * 0.78f, drum * 0.82f, 0.12f), new Vector3(0f, yaw, 0f));
                    // The cross bracing that marks an airlock face at a glance.
                    float diag = Mathf.Sqrt(chord * chord + drum * drum) * 0.62f;
                    float lean = Mathf.Atan2(drum * 0.8f, chord * 0.8f) * Mathf.Rad2Deg;
                    m.Box(PieceSurface.Trim, centre + dir * 0.13f, new Vector3(0.07f, diag, 0.05f),
                          new Vector3(0f, yaw, lean - 90f));
                    m.Box(PieceSurface.Trim, centre + dir * 0.13f, new Vector3(0.07f, diag, 0.05f),
                          new Vector3(0f, yaw, 90f - lean));
                }
                else
                {
                    m.Box(PieceSurface.Frame, centre + dir * 0.05f,
                          new Vector3(chord * 0.8f, drum * 0.66f, 0.07f), new Vector3(0f, yaw, 0f));
                }

                // Mullion between every bay.
                float ma = i / (float)segs * Mathf.PI * 2f;
                Vector3 mdir = new(Mathf.Cos(ma), 0f, Mathf.Sin(ma));
                m.Box(PieceSurface.Trim, mdir * (radius + 0.03f) + new Vector3(0f, drum * 0.5f, 0f),
                      new Vector3(0.12f, drum, 0.22f), new Vector3(0f, 90f - ma * Mathf.Rad2Deg, 0f));
            }

            // ── Banding: skirt, waist and the lit strip under the cap ────
            Ring(m, PieceSurface.Trim, radius + 0.1f, 0.16f, 0.34f, segs);
            Ring(m, PieceSurface.Trim, radius + 0.08f, drum - 0.12f, 0.2f, segs);
            Ring(m, PieceSurface.Frame, radius + 0.12f, drum + 0.02f, 0.12f, segs);

            // ── Cap: shallow ribbed dome with viewport arcs ──────────────
            const int rings = 4;
            for (int r = 0; r < rings; r++)
            {
                float t0 = r / (float)rings, t1 = (r + 1) / (float)rings;
                float y0 = drum + Mathf.Sin(t0 * Mathf.PI * 0.5f) * cap;
                float y1 = drum + Mathf.Sin(t1 * Mathf.PI * 0.5f) * cap;
                float r0 = Mathf.Cos(t0 * Mathf.PI * 0.5f) * radius;
                float r1 = Mathf.Cos(t1 * Mathf.PI * 0.5f) * radius;
                float rm = (r0 + r1) * 0.5f;

                for (int i = 0; i < segs; i++)
                {
                    float a = (i + 0.5f) / segs * Mathf.PI * 2f;
                    Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    Vector3 centre = dir * rm + new Vector3(0f, (y0 + y1) * 0.5f, 0f);
                    float chord = Mathf.PI * 2f * rm / segs;
                    float slab = Mathf.Sqrt((y1 - y0) * (y1 - y0) + (r0 - r1) * (r0 - r1)) * 1.05f;
                    // Lean the panel so its local Y follows the band's slope: the
                    // band rises by (y1 - y0) while drawing in by (r1 - r0), and
                    // Quaternion.Euler applies X before Y, so the panel tilts in its
                    // own frame and is then swung round the drum.
                    float lean = Mathf.Atan2(r1 - r0, y1 - y0) * Mathf.Rad2Deg;
                    var euler = new Vector3(lean, 90f - a * Mathf.Rad2Deg, 0f);

                    // Four skylight arcs on the lower ring, hull everywhere else.
                    bool skylight = r == 0 && (i == 2 || i == 6 || i == 10 || i == 14);
                    m.Box(skylight ? PieceSurface.Glass : PieceSurface.Skin, centre,
                          new Vector3(chord * 0.98f, slab, 0.16f), euler);
                }

                Ring(m, PieceSurface.Trim, r1 + 0.04f, y1, 0.1f, segs);
            }

            // Radial ribs over the cap and a crown plate.
            for (int i = 0; i < segs; i += 2)
            {
                float a = i / (float)segs * Mathf.PI * 2f;
                Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (int r = 0; r < rings; r++)
                {
                    float t0 = r / (float)rings, t1 = (r + 1) / (float)rings;
                    Vector3 p0 = dir * (Mathf.Cos(t0 * Mathf.PI * 0.5f) * radius)
                                 + new Vector3(0f, drum + Mathf.Sin(t0 * Mathf.PI * 0.5f) * cap, 0f);
                    Vector3 p1 = dir * (Mathf.Cos(t1 * Mathf.PI * 0.5f) * radius)
                                 + new Vector3(0f, drum + Mathf.Sin(t1 * Mathf.PI * 0.5f) * cap, 0f);
                    Vector3 seg = p1 - p0;
                    m.Box(PieceSurface.Trim, (p0 + p1) * 0.5f + dir * 0.06f,
                          new Vector3(0.11f, seg.magnitude, 0.11f),
                          Quaternion.FromToRotation(Vector3.up, seg.normalized).eulerAngles);
                }
            }
            m.Box(PieceSurface.Trim, new Vector3(0f, drum + cap + 0.04f, 0f), new Vector3(1.1f, 0.12f, 1.1f));
            m.Box(PieceSurface.Frame, new Vector3(0f, drum + cap + 0.14f, 0f), new Vector3(0.34f, 0.18f, 0.34f));

            // Deck plate so the module reads as sitting on something.
            m.Box(PieceSurface.Frame, new Vector3(0f, 0.09f, 0f), new Vector3(Module, 0.18f, Module));
        }

        /// <summary>A banding ring made of short chords around the circumference.</summary>
        private static void Ring(PieceMesh m, PieceSurface surface, float radius, float y, float thickness, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = (i + 0.5f) / segs * Mathf.PI * 2f;
                Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float chord = Mathf.PI * 2f * radius / segs;
                m.Box(surface, dir * radius + new Vector3(0f, y, 0f),
                      new Vector3(chord * 1.02f, thickness, 0.12f),
                      new Vector3(0f, 90f - a * Mathf.Rad2Deg, 0f));
            }
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
                    Box(new Vector3(0f, 0.09f, 0f), new Vector3(Module, 0.18f, Module));
                    Box(new Vector3(0f, Storey * 0.23f, 0f), new Vector3(Module * 0.94f, Storey * 0.46f, Module * 0.94f));
                    Box(new Vector3(0f, Storey * 0.58f, 0f), new Vector3(Module * 0.66f, Storey * 0.28f, Module * 0.66f));
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
                    Box(new Vector3(0f, 0.21f, 0f), new Vector3(Module, 0.42f, Module));
                    break;
                case BuildFamily.FloorHatch:
                {
                    float band = (Module - HatchW) * 0.5f;
                    foreach (float s in new[] { -1f, 1f })
                    {
                        Box(new Vector3(s * (HalfModule - band * 0.5f), 0.21f, 0f), new Vector3(band, 0.42f, Module));
                        Box(new Vector3(0f, 0.21f, s * (HalfModule - band * 0.5f)), new Vector3(HatchW, 0.42f, band));
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
                case BuildFamily.WindowPane:
                {
                    const float w = 3.6f, h = 2.1f;
                    Box(new Vector3(0f, (Storey - h) * 0.5f + h * 0.5f, 0f), new Vector3(w, h, 0.18f));
                    break;
                }
                case BuildFamily.HatchLid:
                    // The lid's collider lives on the swinging pivot, added by the
                    // setup step; the root keeps none or a shut hatch would block
                    // the opening it is hinged into even when standing open.
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
                        Box(new Vector3(0f, rise * (i + 1), -HalfModule + run * (i + 0.5f)),
                            new Vector3(Module - 0.6f, 0.16f, run));
                    break;
                }
                case BuildFamily.Railing:
                    Box(new Vector3(0f, 0.59f, 0f), new Vector3(Module, 1.18f, 0.22f));
                    break;
                default:
                    Box(new Vector3(0f, Storey * 0.5f, 0f), new Vector3(Module, Storey, WallThick + 0.2f));
                    break;
            }
        }
    }
}
#endif
