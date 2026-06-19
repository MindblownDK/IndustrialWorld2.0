// Assets/Scripts/VoxelEngine/Cosmos/SpaceBodyRenderer.cs
//
// Renders distant celestial bodies (planets, moons, sun) as low-quality LOD spheres in the sky.
//
// Like Space Engineers: you see the whole solar system around you — other planets hang in the
// distance, the sun glows, moons orbit. When far away, bodies are simple coloured spheres (a
// few hundred polys); they cost almost nothing because they're ONE draw call each.
//
// This reads the CosmicRegistry's body layout each frame and positions scaled-down LOD spheres
// at the correct direction + apparent size. The km-scale cosmic positions are compressed to a
// manageable visual range so you can actually SEE the other planets without floating-origin.
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Renders the solar system's bodies as distant LOD spheres. Attach anywhere in the scene;
    /// it reads CosmicRegistry.Instance for positions and spawns one sphere per body.
    /// </summary>
    public class SpaceBodyRenderer : MonoBehaviour
    {
        [Header("Scaling")]
        [Tooltip("Cosmic distances (km) are compressed to this visual range (metres) so other " +
                 "planets are actually visible in the sky without floating-origin.")]
        public float visualRange = 8000f;

        [Tooltip("Base visual size of a planet (metres at 1× scale).")]
        public float planetVisualScale = 200f;

        [Tooltip("Base visual size of a moon.")]
        public float moonVisualScale = 60f;

        [Tooltip("Visual size of the sun (always large + glowing).")]
        public float sunVisualScale = 800f;

        [Header("Quality")]
        [Tooltip("LOD sphere resolution (higher = smoother distant planets).")]
        [Range(8, 64)] public int sphereResolution = 24;

        [Tooltip("Rebuild body positions every N frames (lower = smoother orbits, higher = cheaper).")]
        public int updateEveryNFrames = 3;

        private struct BodyVisual
        {
            public GameObject go;
            public MeshFilter mf;
            public MeshRenderer mr;
            public Mesh mesh;
        }

        private readonly List<BodyVisual> _sunVisuals = new();
        private readonly List<BodyVisual> _bodyVisuals = new();
        private int _frameCount;

        private void Update()
        {
            _frameCount++;
            if (_frameCount % updateEveryNFrames != 0) return;

            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            // Render the sun(s).
            EnsureCount(_sunVisuals, registry.Sun != null ? 1 : 0, "SpaceSun");
            if (registry.Sun != null)
            {
                var sun = registry.Sun;
                float intensity = sun.settings != null ? sun.settings.intensity : 1f;
                Color glow = sun.settings != null ? sun.settings.glowColor : new Color(1f, 0.9f, 0.7f);
                PositionBody(_sunVisuals[0], Vector3.zero, sunVisualScale * intensity, glow, emissive: true);
            }

            // Render planets + moons.
            int bodyCount = registry.Bodies.Count;
            EnsureCount(_bodyVisuals, bodyCount, "SpaceBody");

            // Player position in cosmic space (approximate: use the active body's position).
            Vector3 viewerKm = Vector3.zero;
            var activeBody = GravityProvider.ActiveBody;
            if (activeBody != null)
            {
                // Find this body in the registry to get its cosmic position.
                for (int i = 0; i < registry.Bodies.Count; i++)
                {
                    if (registry.Bodies[i].settings == activeBody.settings)
                    {
                        viewerKm = registry.Bodies[i].positionKm;
                        break;
                    }
                }
            }

            for (int i = 0; i < bodyCount; i++)
            {
                var b = registry.Bodies[i];
                if (b == null || b.settings == null) continue;

                // Direction from viewer to this body (cosmic space).
                Vector3 dir = b.positionKm - viewerKm;
                float distKm = dir.magnitude;
                if (distKm < 1f) dir = Random.onUnitSphere;  // we're ON this body — pick a sky direction
                else dir /= distKm;

                // Compress cosmic distance to visual range.
                // Use a logarithmic compression so very distant planets are still visible but smaller.
                float compressedDist = Mathf.Lerp(visualRange * 0.3f, visualRange, Mathf.Clamp01(distKm / 5000f));

                // Visual position: direction × compressed distance (relative to the viewer).
                Vector3 visualPos = transform.position + dir * compressedDist;

                // Visual size: based on body radius (km), scaled down.
                float radiusKm = b.settings.radiusKm;
                float visualSize = (b.isPlanet ? planetVisualScale : moonVisualScale) *
                                   Mathf.Clamp01(radiusKm / 8f);
                // Distant bodies appear smaller (perspective).
                visualSize *= Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(distKm / 8000f));

                // Colour: use the body's characteristics.
                Color color = GetBodyColor(b);

                PositionBody(_bodyVisuals[i], visualPos, visualSize, color, emissive: false);
            }
        }

        private static Color GetBodyColor(BodyInstance b)
        {
            if (b.settings == null) return Color.gray;
            // Earth-like → blue-green. Desert → sandy. Ice → white. Moon → grey.
            if (!b.settings.HasOxygen) return new Color(0.5f, 0.5f, 0.55f);  // moon/airless grey
            if (b.settings.temperature < -5f) return new Color(0.8f, 0.85f, 0.9f);  // ice world
            if (b.settings.temperature > 30f) return new Color(0.8f, 0.65f, 0.4f);  // desert
            return new Color(0.2f, 0.4f, 0.6f);  // earth-like blue
        }

        private void EnsureCount(List<BodyVisual> list, int count, string namePrefix)
        {
            while (list.Count < count)
            {
                var go = new GameObject(namePrefix + "_" + list.Count);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                var mesh = CreateSphere(sphereResolution);
                mf.sharedMesh = mesh;
                list.Add(new BodyVisual { go = go, mf = mf, mr = mr, mesh = mesh });
            }
            while (list.Count > count)
            {
                var v = list[list.Count - 1];
                if (v.mesh != null) Destroy(v.mesh);
                if (v.go != null) Destroy(v.go);
                list.RemoveAt(list.Count - 1);
            }
        }

        private void PositionBody(BodyVisual v, Vector3 pos, float size, Color color, bool emissive)
        {
            if (v.go == null) return;
            v.go.transform.position = pos;
            v.go.transform.localScale = Vector3.one * size;

            // Ensure material exists.
            if (v.mr.sharedMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                v.mr.sharedMaterial = new Material(shader) { name = "Mat_SpaceBody" };
            }
            var mat = v.mr.sharedMaterial;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            if (emissive)
            {
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 2f);
                }
            }
        }

        private static Mesh CreateSphere(int resolution)
        {
            var verts = new List<Vector3>(IcosphereVerts());
            var tris = new List<int>(IcosphereTris());
            int sub = 0;
            while (verts.Count < resolution * resolution && sub < 5)
            {
                Subdivide(verts, tris);
                sub++;
            }
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var mesh = new Mesh { name = "SpaceSphere" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Vector3> IcosphereVerts()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            return new List<Vector3>
            {
                N(-1,  t,  0), N( 1,  t,  0), N(-1, -t,  0), N( 1, -t,  0),
                N( 0, -1,  t), N( 0,  1,  t), N( 0, -1, -t), N( 0,  1, -t),
                N( t,  0, -1), N( t,  0,  1), N(-t,  0, -1), N(-t,  0,  1),
            };
        }
        private static Vector3 N(float x, float y, float z) => new Vector3(x, y, z).normalized;
        private static List<int> IcosphereTris()
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
            var nt = new List<int>(tris.Count * 4);
            int Mid(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(k, out int idx)) return idx;
                Vector3 m = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count; verts.Add(m); cache[k] = idx; return idx;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.Add(a); nt.Add(ab); nt.Add(ca);
                nt.Add(b); nt.Add(bc); nt.Add(ab);
                nt.Add(c); nt.Add(ca); nt.Add(bc);
                nt.Add(ab); nt.Add(bc); nt.Add(ca);
            }
            tris.Clear(); tris.AddRange(nt);
        }
    }
}
