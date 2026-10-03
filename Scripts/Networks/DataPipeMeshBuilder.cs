// Assets/Scripts/VoxelEngine/Networks/DataPipeMeshBuilder.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║                 DATA PIPE MESH BUILDER (14.41.0)                 ║
// ║  Procedural geometry for the Data Pipe's nine fitting shapes -  ║
// ║  the SAME shapes as the energy pipe (endpoints come from        ║
// ║  EnergyPipeMeshBuilder.GetLocalEndpoints), drawn as an actual   ║
// ║  DATA CABLE: one slim braided trunk with ribbed strain collars, ║
// ║  RJ45-style plug heads on every open end and emissive activity  ║
// ║  rings that read as light pulses running down the line.         ║
// ║                                                                  ║
// ║  Submesh 0 = dark sheath (lit material)                         ║
// ║  Submesh 1 = emissive phosphor (plug windows + pulse rings)     ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Networks
{
    public static class DataPipeMeshBuilder
    {
        public const float CableRadius   = 0.055f;  // slim single trunk
        public const float RingRadius    = 0.072f;  // pulse ring bulge
        public const float RingLength    = 0.030f;
        public const float RingSpacing   = 0.45f;   // metres between pulse rings
        public const float CornerRadius  = 0.28f;   // rounded corner arc radius
        public const int   RadialSegs    = 10;
        public const int   CornerSegs    = 6;

        // RJ45-style plug head dimensions.
        public const float PlugWidth  = 0.20f;
        public const float PlugHeight = 0.15f;
        public const float PlugDepth  = 0.17f;

        // Junction hub (little network switch).
        public const float HubSize = 0.36f;

        private struct MeshData
        {
            public List<Vector3> V;
            public List<Vector3> N;
            public List<Vector2> UV;
            public List<int> Sheath;  // submesh 0 triangles
            public List<int> Glow;    // submesh 1 triangles
        }

        // ────────────────────────────────────────────────────────────
        //  PUBLIC API
        // ────────────────────────────────────────────────────────────

        /// <summary>Build the full two-submesh data cable mesh for a variant.</summary>
        public static Mesh BuildMesh(EnergyPipeVariant variant, int straightLength = 1)
        {
            straightLength = Mathf.Clamp(straightLength, 1, 5);
            var md = new MeshData
            {
                V = new List<Vector3>(), N = new List<Vector3>(), UV = new List<Vector2>(),
                Sheath = new List<int>(), Glow = new List<int>()
            };

            if (variant == EnergyPipeVariant.Junction4Way || variant == EnergyPipeVariant.Junction6Way)
            {
                BuildJunction(md, variant);
            }
            else
            {
                var path = SamplePath(GetControlPoints(variant, straightLength));
                BuildTrunk(md, path);
                // Plug heads on both open ends, facing out along the endpoint normals.
                var eps = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength);
                foreach (var ep in eps)
                    BuildPlugHead(md, ep.Position, ep.Normal, ep.Right, ep.Up);
            }

            var mesh = new Mesh { name = $"DataPipe_{variant}_{straightLength}" };
            if (md.V.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(md.V);
            mesh.SetNormals(md.N);
            mesh.SetUVs(0, md.UV);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(md.Sheath, 0);
            mesh.SetTriangles(md.Glow, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Straight segments of the variant's centreline - used to lay
        /// box colliders along the run so every part of a long or bent pipe can
        /// be aimed at, wrenched and broken.</summary>
        public static List<(Vector3 a, Vector3 b)> GetColliderSegments(
            EnergyPipeVariant variant, int straightLength = 1)
        {
            straightLength = Mathf.Clamp(straightLength, 1, 5);
            var segs = new List<(Vector3, Vector3)>();
            if (variant == EnergyPipeVariant.Junction4Way || variant == EnergyPipeVariant.Junction6Way)
            {
                foreach (var ep in EnergyPipeMeshBuilder.GetLocalEndpoints(variant, straightLength))
                    segs.Add((Vector3.zero, ep.Position));
                return segs;
            }
            var pts = GetControlPoints(variant, straightLength);
            for (int i = 0; i < pts.Count - 1; i++) segs.Add((pts[i], pts[i + 1]));
            return segs;
        }

        // ────────────────────────────────────────────────────────────
        //  CENTRELINE PATHS (same endpoints as the energy pipe)
        // ────────────────────────────────────────────────────────────

        private static List<Vector3> GetControlPoints(EnergyPipeVariant variant, int len)
        {
            switch (variant)
            {
                case EnergyPipeVariant.Straight:
                    return new List<Vector3> { Vector3.zero, Vector3.forward * len };
                case EnergyPipeVariant.BendRight:
                    return new List<Vector3> { Vector3.zero, new(0, 0, 1), new(1, 0, 1) };
                case EnergyPipeVariant.BendUp:
                    return new List<Vector3> { Vector3.zero, new(0, 0, 1), new(0, 1, 1) };
                case EnergyPipeVariant.StepUp:
                    return new List<Vector3> { Vector3.zero, new(0, 0, 1), new(0, 1, 1), new(0, 1, 2) };
                case EnergyPipeVariant.StepRight:
                    return new List<Vector3> { Vector3.zero, new(0, 0, 1), new(1, 0, 1), new(1, 0, 2) };
                case EnergyPipeVariant.BendLeftToUp:
                    return new List<Vector3> { new(-1, 0, 0), Vector3.zero, new(0, 1, 0) };
                case EnergyPipeVariant.BendRightToUp:
                    return new List<Vector3> { new(1, 0, 0), Vector3.zero, new(0, 1, 0) };
                default:
                    return new List<Vector3> { Vector3.zero, Vector3.forward };
            }
        }

        /// <summary>Polyline → smooth sample list with rounded corners.</summary>
        private static List<Vector3> SamplePath(List<Vector3> pts)
        {
            var outPts = new List<Vector3>();
            if (pts.Count < 2) return pts;
            outPts.Add(pts[0]);
            for (int i = 1; i < pts.Count - 1; i++)
            {
                Vector3 prev = pts[i - 1], corner = pts[i], next = pts[i + 1];
                Vector3 inDir = (corner - prev).normalized;
                Vector3 outDir = (next - corner).normalized;
                float r = Mathf.Min(CornerRadius,
                    (corner - prev).magnitude * 0.45f, (next - corner).magnitude * 0.45f);
                Vector3 arcStart = corner - inDir * r;
                Vector3 arcEnd = corner + outDir * r;
                outPts.Add(arcStart);
                // Quadratic bezier through the corner for a tidy cable bend.
                for (int s = 1; s < CornerSegs; s++)
                {
                    float t = s / (float)CornerSegs;
                    Vector3 a = Vector3.Lerp(arcStart, corner, t);
                    Vector3 b = Vector3.Lerp(corner, arcEnd, t);
                    outPts.Add(Vector3.Lerp(a, b, t));
                }
                outPts.Add(arcEnd);
            }
            outPts.Add(pts[pts.Count - 1]);
            return outPts;
        }

        // ────────────────────────────────────────────────────────────
        //  TRUNK (swept tube + pulse rings)
        // ────────────────────────────────────────────────────────────

        private static void BuildTrunk(MeshData md, List<Vector3> path)
        {
            if (path.Count < 2) return;

            // Parallel-transport frames along the path.
            var frames = new List<(Vector3 pos, Vector3 fwd, Vector3 right, Vector3 up)>();
            Vector3 fwd0 = (path[1] - path[0]).normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(fwd0, Vector3.up)) > 0.92f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(up, fwd0).normalized;
            up = Vector3.Cross(fwd0, right).normalized;
            frames.Add((path[0], fwd0, right, up));
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 fwd = (i < path.Count - 1 ? path[i + 1] - path[i - 1] : path[i] - path[i - 1]).normalized;
                var rot = Quaternion.FromToRotation(frames[i - 1].fwd, fwd);
                right = (rot * frames[i - 1].right).normalized;
                up = Vector3.Cross(fwd, right).normalized;
                frames.Add((path[i], fwd, right, up));
            }

            // Tube rings.
            int ringStart = md.V.Count;
            float dist = 0f;
            var ringDists = new List<float>();
            for (int i = 0; i < frames.Count; i++)
            {
                if (i > 0) dist += (frames[i].pos - frames[i - 1].pos).magnitude;
                ringDists.Add(dist);
                for (int s = 0; s <= RadialSegs; s++)
                {
                    float ang = s / (float)RadialSegs * Mathf.PI * 2f;
                    Vector3 rad = frames[i].right * Mathf.Cos(ang) + frames[i].up * Mathf.Sin(ang);
                    md.V.Add(frames[i].pos + rad * CableRadius);
                    md.N.Add(rad);
                    md.UV.Add(new Vector2(s / (float)RadialSegs, dist * 2f));
                }
            }
            // NOTE: rings are sampled counter-clockwise (right·cos + up·sin
            // with right×up = fwd), so the quad winding is mirrored relative
            // to EnergyPipeMeshBuilder's clockwise rings - (a,b,c)/(b,d,c)
            // here equals its proven (i0,i2,i1)/(i1,i2,i3) orientation.
            int stride = RadialSegs + 1;
            for (int i = 0; i < frames.Count - 1; i++)
            for (int s = 0; s < RadialSegs; s++)
            {
                int a = ringStart + i * stride + s;
                int b = a + 1;
                int c = a + stride;
                int d = c + 1;
                md.Sheath.Add(a); md.Sheath.Add(b); md.Sheath.Add(c);
                md.Sheath.Add(b); md.Sheath.Add(d); md.Sheath.Add(c);
            }

            // Pulse rings (glow) + strain collars near the ends (sheath).
            float total = ringDists[ringDists.Count - 1];
            for (float d2 = RingSpacing; d2 < total - 0.12f; d2 += RingSpacing)
            {
                var (p, f, r, u) = FrameAt(frames, ringDists, d2);
                BuildRing(md, p, f, r, u, RingRadius, RingLength, glow: true);
            }
            // Ribbed collar: two sheath rings just behind each cable end.
            var (p0, f0, r0, u0) = FrameAt(frames, ringDists, Mathf.Min(0.10f, total * 0.25f));
            BuildRing(md, p0, f0, r0, u0, CableRadius * 1.28f, 0.045f, glow: false);
            var (p1, f1, r1, u1) = FrameAt(frames, ringDists, Mathf.Max(total - 0.10f, total * 0.75f));
            BuildRing(md, p1, f1, r1, u1, CableRadius * 1.28f, 0.045f, glow: false);
        }

        private static (Vector3, Vector3, Vector3, Vector3) FrameAt(
            List<(Vector3 pos, Vector3 fwd, Vector3 right, Vector3 up)> frames,
            List<float> dists, float d)
        {
            for (int i = 1; i < frames.Count; i++)
            {
                if (dists[i] < d) continue;
                float span = dists[i] - dists[i - 1];
                float t = span > 0.0001f ? (d - dists[i - 1]) / span : 0f;
                return (Vector3.Lerp(frames[i - 1].pos, frames[i].pos, t),
                        Vector3.Slerp(frames[i - 1].fwd, frames[i].fwd, t),
                        Vector3.Slerp(frames[i - 1].right, frames[i].right, t),
                        Vector3.Slerp(frames[i - 1].up, frames[i].up, t));
            }
            var last = frames[frames.Count - 1];
            return (last.pos, last.fwd, last.right, last.up);
        }

        /// <summary>Short fat cylinder around the trunk - pulse ring (glow) or
        /// ribbed strain collar (sheath).</summary>
        private static void BuildRing(MeshData md, Vector3 center, Vector3 fwd,
            Vector3 right, Vector3 up, float radius, float halfLen, bool glow)
        {
            var tris = glow ? md.Glow : md.Sheath;
            int start = md.V.Count;
            for (int cap = 0; cap < 2; cap++)
            {
                Vector3 c = center + fwd * (cap == 0 ? -halfLen : halfLen);
                for (int s = 0; s <= RadialSegs; s++)
                {
                    float ang = s / (float)RadialSegs * Mathf.PI * 2f;
                    Vector3 rad = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    md.V.Add(c + rad * radius);
                    md.N.Add(rad);
                    md.UV.Add(new Vector2(s / (float)RadialSegs, cap));
                }
            }
            int stride = RadialSegs + 1;
            for (int s = 0; s < RadialSegs; s++)
            {
                int a = start + s, b = a + 1, c = a + stride, d = c + 1;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
            // End discs so the ring reads as a solid sleeve.
            for (int cap = 0; cap < 2; cap++)
            {
                Vector3 n = cap == 0 ? -fwd : fwd;
                Vector3 c = center + fwd * (cap == 0 ? -halfLen : halfLen);
                int discStart = md.V.Count;
                md.V.Add(c); md.N.Add(n); md.UV.Add(new Vector2(0.5f, 0.5f));
                for (int s = 0; s <= RadialSegs; s++)
                {
                    float ang = s / (float)RadialSegs * Mathf.PI * 2f;
                    Vector3 rad = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                    md.V.Add(c + rad * radius); md.N.Add(n);
                    md.UV.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
                }
                for (int s = 0; s < RadialSegs; s++)
                {
                    if (cap == 0) { tris.Add(discStart); tris.Add(discStart + 1 + s); tris.Add(discStart + 2 + s); }
                    else          { tris.Add(discStart); tris.Add(discStart + 2 + s); tris.Add(discStart + 1 + s); }
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        //  PLUG HEAD (RJ45-style connector on every open end)
        // ────────────────────────────────────────────────────────────

        private static void BuildPlugHead(MeshData md, Vector3 tip, Vector3 normal,
            Vector3 right, Vector3 up)
        {
            normal = normal.normalized; right = right.normalized; up = up.normalized;
            Vector3 center = tip - normal * (PlugDepth * 0.5f);

            // Body.
            BuildBox(md.V, md.N, md.UV, md.Sheath, center, right, up, normal,
                PlugWidth, PlugHeight, PlugDepth);
            // Latch clip ridge on top.
            BuildBox(md.V, md.N, md.UV, md.Sheath,
                center + up * (PlugHeight * 0.5f + 0.012f) - normal * 0.015f,
                right, up, normal, PlugWidth * 0.45f, 0.024f, PlugDepth * 0.55f);
            // Emissive link window on the face - the "port light".
            BuildBox(md.V, md.N, md.UV, md.Glow,
                tip - normal * 0.006f, right, up, normal,
                PlugWidth * 0.62f, PlugHeight * 0.34f, 0.016f);
            // Strain-relief boot stepping down to the cable.
            BuildBox(md.V, md.N, md.UV, md.Sheath,
                center - normal * (PlugDepth * 0.5f + 0.035f),
                right, up, normal, PlugWidth * 0.55f, PlugHeight * 0.55f, 0.07f);
        }

        // ────────────────────────────────────────────────────────────
        //  JUNCTIONS (network-switch hub with plug stubs)
        // ────────────────────────────────────────────────────────────

        private static void BuildJunction(MeshData md, EnergyPipeVariant variant)
        {
            // Central switch body.
            BuildBox(md.V, md.N, md.UV, md.Sheath, Vector3.zero,
                Vector3.right, Vector3.up, Vector3.forward, HubSize, HubSize, HubSize);
            // Status strip around the hub's waist.
            BuildBox(md.V, md.N, md.UV, md.Glow, Vector3.zero,
                Vector3.right, Vector3.up, Vector3.forward,
                HubSize + 0.012f, 0.028f, HubSize + 0.012f);

            // A short cable stub + plug head toward every port.
            var eps = EnergyPipeMeshBuilder.GetLocalEndpoints(variant, 1);
            foreach (var ep in eps)
            {
                var path = new List<Vector3> { ep.Normal * (HubSize * 0.5f - 0.02f), ep.Position };
                BuildTrunk(md, path);
                BuildPlugHead(md, ep.Position, ep.Normal, ep.Right, ep.Up);
            }
        }

        // ────────────────────────────────────────────────────────────
        //  BOX PRIMITIVE
        // ────────────────────────────────────────────────────────────

        private static void BuildBox(List<Vector3> V, List<Vector3> N, List<Vector2> UV,
            List<int> tris, Vector3 center, Vector3 right, Vector3 up, Vector3 fwd,
            float w, float h, float d)
        {
            Vector3 x = right * (w * 0.5f), y = up * (h * 0.5f), z = fwd * (d * 0.5f);
            void Face(Vector3 n, Vector3 a, Vector3 b, Vector3 c, Vector3 e)
            {
                int s = V.Count;
                V.Add(center + a); V.Add(center + b); V.Add(center + c); V.Add(center + e);
                for (int i = 0; i < 4; i++) N.Add(n);
                UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(1, 0));
                UV.Add(new Vector2(1, 1)); UV.Add(new Vector2(0, 1));
                tris.Add(s); tris.Add(s + 2); tris.Add(s + 1);
                tris.Add(s); tris.Add(s + 3); tris.Add(s + 2);
            }
            Face(fwd,   -x - y + z,  x - y + z,  x + y + z, -x + y + z);
            Face(-fwd,   x - y - z, -x - y - z, -x + y - z,  x + y - z);
            Face(right,  x - y + z,  x - y - z,  x + y - z,  x + y + z);
            Face(-right,-x - y - z, -x - y + z, -x + y + z, -x + y - z);
            Face(up,    -x + y + z,  x + y + z,  x + y - z, -x + y - z);
            Face(-up,   -x - y - z,  x - y - z,  x - y + z, -x - y + z);
        }
    }
}
