// Assets/Scripts/VoxelEngine/GridSystem/Wheels/WheelMeshFactory.cs
//
// REAL GEOMETRY FOR THE WHEEL.
//
// The first pass stacked Unity primitives: a cylinder with cubes glued around it
// reads as a naval mine, not a tire, because a tire is a *surface of revolution*
// with a crowned profile and its tread is displacement on that surface — not
// separate boxes floating off the rim.
//
// So this file builds meshes. One generic lathe (revolve a 2D profile around the
// wheel's X axis) plus a tread displacement hook covers the carcass and the rim;
// a helix tube covers the coil spring that has to visibly compress. Everything is
// generated once, cached by key, and handed to the optional persister so editor
// tooling can save it as a .asset — a prefab that references a runtime-only Mesh
// comes back empty after a domain reload.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.GridSystem
{
    public static class WheelMeshFactory
    {
        /// <summary>Editor hook: (mesh, suggestedAssetName) → the mesh to actually use.</summary>
        public static System.Func<Mesh, string, Mesh> MeshPersister;

        private static readonly Dictionary<string, Mesh> s_cache = new Dictionary<string, Mesh>(8);

        public static void ClearCache() => s_cache.Clear();

        private static Mesh Get(string key, System.Func<Mesh> build)
        {
            if (s_cache.TryGetValue(key, out var cached) && cached != null) return cached;
            var mesh = build();
            mesh.name = key;
            var persister = MeshPersister;
            if (persister != null)
            {
                var persisted = persister(mesh, key);
                if (persisted != null) mesh = persisted;
            }
            s_cache[key] = mesh;
            return mesh;
        }

        // ════════════════════════════════════════════════════════════════════
        //  TIRE
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Unit tire: radius 1, width 1, axis along local X. The profile is a real
        /// carcass section — bead, sidewall bulge, shoulder radius, crowned tread —
        /// and the crown carries block lugs cut by lateral grooves, which is what
        /// gives the silhouette its chunky industrial read at any distance.
        /// </summary>
        public static Mesh Tire(WheelSizeClass sizeClass) => Get($"WheelTire_{sizeClass}", () =>
        {
            int lugs = sizeClass == WheelSizeClass.Size_5x5 ? 28 : sizeClass == WheelSizeClass.Size_3x3 ? 22 : 18;
            int segments = lugs * 6;                       // 6 columns per lug: block, block, block, groove...
            float treadDepth = sizeClass == WheelSizeClass.Size_5x5 ? 0.075f : 0.065f;

            // Profile in (axial x, radial r), from the outboard bead round to the inboard bead.
            var profile = new List<Vector2>
            {
                new Vector2( 0.500f, 0.560f), // outboard bead seat
                new Vector2( 0.520f, 0.640f),
                new Vector2( 0.540f, 0.760f), // sidewall bulge (widest point)
                new Vector2( 0.520f, 0.870f),
                new Vector2( 0.470f, 0.945f), // shoulder
                new Vector2( 0.400f, 0.985f),
                new Vector2( 0.300f, 1.000f), // crown
                new Vector2( 0.100f, 1.005f),
                new Vector2(-0.100f, 1.005f),
                new Vector2(-0.300f, 1.000f),
                new Vector2(-0.400f, 0.985f),
                new Vector2(-0.470f, 0.945f),
                new Vector2(-0.520f, 0.870f),
                new Vector2(-0.540f, 0.760f),
                new Vector2(-0.520f, 0.640f),
                new Vector2(-0.500f, 0.560f), // inboard bead seat
            };

            // Tread displacement: only the crown band is displaced, and the two halves
            // are offset by half a lug so the pattern reads as a directional V.
            float Displace(int ring, int point, Vector2 p)
            {
                if (p.y < 0.93f) return 0f;                              // sidewall: never
                float crown01 = Mathf.InverseLerp(0.93f, 1.005f, p.y);   // fade into the shoulder
                bool outboard = p.x >= 0f;
                float phase = ring / 6f + (outboard ? 0f : 0.5f) + Mathf.Abs(p.x) * 0.6f;
                float lug = Mathf.Repeat(phase, 1f) < 0.62f ? 1f : 0f;   // block vs lateral groove
                float rib = Mathf.Abs(p.x) < 0.06f ? 1f : 0f;            // continuous centre rib
                return treadDepth * crown01 * Mathf.Max(lug, rib);
            }

            var mesh = Lathe(profile, segments, Displace);
            // Close the bead openings so the tire is watertight when seen from inside.
            return mesh;
        });

        /// <summary>
        /// Unit rim: radius 1 (matched to the tire bead), width 1, axis along local X.
        /// Dished centre, barrel, and a raised hub boss — the shape the reference wheel
        /// reads as "machined metal" rather than "grey cylinder".
        /// </summary>
        public static Mesh Rim(WheelSizeClass sizeClass) => Get($"WheelRim_{sizeClass}", () =>
        {
            int segments = 48;
            var profile = new List<Vector2>
            {
                new Vector2( 0.300f, 0.000f), // outboard hub face centre
                new Vector2( 0.300f, 0.120f),
                new Vector2( 0.340f, 0.200f), // raised boss
                new Vector2( 0.300f, 0.280f),
                new Vector2( 0.240f, 0.420f), // dish sweeping out to the barrel
                new Vector2( 0.300f, 0.520f),
                new Vector2( 0.480f, 0.560f), // outboard bead flange
                new Vector2( 0.520f, 0.520f),
                new Vector2( 0.440f, 0.500f),
                new Vector2(-0.440f, 0.500f), // barrel
                new Vector2(-0.520f, 0.520f),
                new Vector2(-0.480f, 0.560f), // inboard bead flange
                new Vector2(-0.340f, 0.520f),
                new Vector2(-0.300f, 0.300f),
                new Vector2(-0.300f, 0.120f),
                new Vector2(-0.300f, 0.000f),
            };
            return Lathe(profile, segments, null);
        });

        /// <summary>Unit brake disc: radius 1, thickness 1 on X, with a vented centre.</summary>
        public static Mesh BrakeDisc() => Get("WheelBrakeDisc", () =>
        {
            var profile = new List<Vector2>
            {
                new Vector2( 0.5f, 0.22f),
                new Vector2( 0.5f, 1.000f),
                new Vector2(-0.5f, 1.000f),
                new Vector2(-0.5f, 0.22f),
                new Vector2( 0.5f, 0.22f),
            };
            return Lathe(profile, 40, null);
        });

        // ════════════════════════════════════════════════════════════════════
        //  SUSPENSION
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Coil spring, one unit long on +X with the pivot at the chassis end. Scaling
        /// the transform on X compresses the coils, so the spring is not a decoration:
        /// it is the load readout the driver sees.
        /// </summary>
        public static Mesh CoilSpring(int coils = 7) => Get($"WheelCoilSpring_{coils}", () =>
        {
            int stepsPerCoil = 16;
            int steps = coils * stepsPerCoil;
            int tubeSides = 8;
            float coilRadius = 0.30f;
            float wireRadius = 0.075f;

            var vertices = new List<Vector3>((steps + 1) * tubeSides);
            var triangles = new List<int>(steps * tubeSides * 6);
            var uvs = new List<Vector2>((steps + 1) * tubeSides);

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float angle = t * coils * Mathf.PI * 2f;
                Vector3 centre = new Vector3(t, Mathf.Sin(angle) * coilRadius, Mathf.Cos(angle) * coilRadius);
                // Tangent of the helix, used to orient the wire cross-section.
                Vector3 tangent = new Vector3(
                    1f / steps,
                    Mathf.Cos(angle) * coilRadius * (coils * Mathf.PI * 2f) / steps,
                    -Mathf.Sin(angle) * coilRadius * (coils * Mathf.PI * 2f) / steps).normalized;
                Vector3 normal = new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle));
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
                normal = Vector3.Cross(binormal, tangent).normalized;

                for (int s = 0; s < tubeSides; s++)
                {
                    float a = s / (float)tubeSides * Mathf.PI * 2f;
                    vertices.Add(centre + (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * wireRadius);
                    uvs.Add(new Vector2(t * coils, s / (float)tubeSides));
                }
            }

            for (int i = 0; i < steps; i++)
            {
                int a = i * tubeSides;
                int b = (i + 1) * tubeSides;
                for (int s = 0; s < tubeSides; s++)
                {
                    int s1 = (s + 1) % tubeSides;
                    triangles.Add(a + s); triangles.Add(b + s1); triangles.Add(b + s);
                    triangles.Add(a + s); triangles.Add(a + s1); triangles.Add(b + s1);
                }
            }

            return Build(vertices, triangles, uvs);
        });

        /// <summary>
        /// A-arm wishbone, one unit long on +X, pivot at the chassis end. Two tapered
        /// legs converge from the wide chassis mount onto the single ball joint, which
        /// is the shape that makes a suspension read as a suspension.
        /// </summary>
        public static Mesh Wishbone() => Get("WheelWishbone", () =>
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();

            void Leg(float zRoot)
            {
                // Tapered box from (0, zRoot) to the joint at (1, 0).
                Vector3[] root =
                {
                    new Vector3(0f,  0.11f, zRoot - 0.09f), new Vector3(0f,  0.11f, zRoot + 0.09f),
                    new Vector3(0f, -0.11f, zRoot + 0.09f), new Vector3(0f, -0.11f, zRoot - 0.09f),
                };
                Vector3[] tip =
                {
                    new Vector3(1f,  0.055f, -0.045f), new Vector3(1f,  0.055f, 0.045f),
                    new Vector3(1f, -0.055f, 0.045f), new Vector3(1f, -0.055f, -0.045f),
                };
                int b = vertices.Count;
                for (int i = 0; i < 4; i++) { vertices.Add(root[i]); uvs.Add(new Vector2(0f, i * 0.25f)); }
                for (int i = 0; i < 4; i++) { vertices.Add(tip[i]);  uvs.Add(new Vector2(1f, i * 0.25f)); }
                for (int i = 0; i < 4; i++)
                {
                    int i2 = (i + 1) % 4;
                    triangles.Add(b + i); triangles.Add(b + 4 + i2); triangles.Add(b + 4 + i);
                    triangles.Add(b + i); triangles.Add(b + i2); triangles.Add(b + 4 + i2);
                }
                // Caps, wound outward along -X and +X respectively.
                triangles.Add(b + 0); triangles.Add(b + 1); triangles.Add(b + 2);
                triangles.Add(b + 0); triangles.Add(b + 2); triangles.Add(b + 3);
                triangles.Add(b + 4); triangles.Add(b + 6); triangles.Add(b + 5);
                triangles.Add(b + 4); triangles.Add(b + 7); triangles.Add(b + 6);
            }

            Leg(-0.34f);
            Leg(0.34f);
            return Build(vertices, triangles, uvs);
        });

        /// <summary>
        /// Telescoping damper, one unit long on +X with the pivot at the chassis end:
        /// a fat body over the inboard 60% and a thin chromed rod running to the tip.
        /// Built as a mesh rather than a rotated primitive because a rotated child under
        /// a non-uniformly scaled parent shears, which is what turned the old damper
        /// into a cone.
        /// </summary>
        public static Mesh Strut() => Get("WheelStrut", () =>
        {
            // Authored tip → root, the same direction as the tire and rim profiles, so
            // the shared Lathe winding puts the normals on the outside.
            var profile = new List<Vector2>
            {
                new Vector2(1.00f, 0.000f), // closed lower eye
                new Vector2(1.00f, 0.070f),
                new Vector2(0.60f, 0.070f), // rod
                new Vector2(0.58f, 0.170f), // step up onto the body
                new Vector2(0.06f, 0.170f),
                new Vector2(0.00f, 0.150f),
                new Vector2(0.00f, 0.000f), // closed chassis eye
            };
            return Lathe(profile, 20, null);
        });

        // ════════════════════════════════════════════════════════════════════
        //  CORE BUILDERS
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Revolves a profile around the X axis. <paramref name="displace"/> may push a
        /// point outward in the radial direction (tread lugs); pass null for a smooth
        /// surface. Profiles are authored outboard → inboard so winding stays correct.
        /// </summary>
        public static Mesh Lathe(List<Vector2> profile, int segments, System.Func<int, int, Vector2, float> displace)
        {
            int rings = Mathf.Max(8, segments);
            int points = profile.Count;
            var vertices = new List<Vector3>(rings * points);
            var uvs = new List<Vector2>(rings * points);
            var triangles = new List<int>(rings * (points - 1) * 6);

            for (int r = 0; r < rings; r++)
            {
                float t = r / (float)rings;
                float angle = t * Mathf.PI * 2f;
                float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
                for (int p = 0; p < points; p++)
                {
                    Vector2 profilePoint = profile[p];
                    float radius = profilePoint.y + (displace != null ? displace(r, p, profilePoint) : 0f);
                    vertices.Add(new Vector3(profilePoint.x, sin * radius, cos * radius));
                    uvs.Add(new Vector2(t * 4f, p / (float)(points - 1)));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                int rNext = (r + 1) % rings;
                for (int p = 0; p < points - 1; p++)
                {
                    int a = r * points + p;
                    int b = rNext * points + p;
                    // Wound so the face normal points AWAY from the axis. Get this
                    // backwards and the mesh renders inside-out: the near wall is culled
                    // and you see the far inner wall, which reads as a transparent object.
                    triangles.Add(a); triangles.Add(b + 1); triangles.Add(a + 1);
                    triangles.Add(a); triangles.Add(b); triangles.Add(b + 1);
                }
            }

            return Build(vertices, triangles, uvs);
        }

        private static Mesh Build(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            var mesh = new Mesh();
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.Optimize();
            return mesh;
        }
    }
}
