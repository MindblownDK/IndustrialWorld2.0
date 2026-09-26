// Assets/Scripts/VoxelEngine/Power/EnergyPipeMeshBuilder.cs

using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Power
{
    public enum EnergyPipeVariant
    {
        Straight = 0,       // 1m-5m adjustable straight length
        BendRight = 1,      // 90° horizontal bend (straight to right)
        BendUp = 2,         // 90° vertical bend (straight to up)
        StepUp = 3,         // Vertical S-step (straight -> 1 up -> straight)
        StepRight = 4,      // Horizontal S-curve (straight -> 1 right -> straight)
        BendLeftToUp = 5,   // 3D compound curve (left to up)
        BendRightToUp = 6,  // 3D compound curve (right to up)
        Junction4Way = 7,   // 4-way planar cross
        Junction6Way = 8    // 6-way 3D omni hub
    }

    /// <summary>
    /// Builds procedural dual-conduit industrial energy pipes matching reference
    /// geometry with bolted terminal flanges, twin parallel shafts, and tier-specific materials.
    /// </summary>
    public static class EnergyPipeMeshBuilder
    {
        public const float TubeRadius = 0.042f;
        public const float TubeSeparation = 0.16f;
        public const float FlangeWidth = 0.30f;
        public const float FlangeHeight = 0.15f;
        public const float FlangeDepth = 0.04f;

        private static readonly Dictionary<string, Material> s_materialCache = new();
        private static readonly Dictionary<string, Texture2D> s_textureCache = new();

        public static Mesh BuildMesh(EnergyPipeVariant variant, int straightLength = 1)
        {
            var mesh = new Mesh { name = $"EnergyPipe_{variant}_{straightLength}" };
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            straightLength = Mathf.Clamp(straightLength, 1, 5);

            switch (variant)
            {
                case EnergyPipeVariant.Straight:
                    BuildStraightConduit(verts, normals, uvs, indices, straightLength);
                    break;
                case EnergyPipeVariant.BendRight:
                    BuildHorizontalBend(verts, normals, uvs, indices);
                    break;
                case EnergyPipeVariant.BendUp:
                    BuildVerticalBend(verts, normals, uvs, indices);
                    break;
                case EnergyPipeVariant.StepUp:
                    BuildVerticalStep(verts, normals, uvs, indices);
                    break;
                case EnergyPipeVariant.StepRight:
                    BuildHorizontalStep(verts, normals, uvs, indices);
                    break;
                case EnergyPipeVariant.BendLeftToUp:
                    BuildCompoundBend(verts, normals, uvs, indices, isLeft: true);
                    break;
                case EnergyPipeVariant.BendRightToUp:
                    BuildCompoundBend(verts, normals, uvs, indices, isLeft: false);
                    break;
                case EnergyPipeVariant.Junction4Way:
                    BuildJunction4Way(verts, normals, uvs, indices);
                    break;
                case EnergyPipeVariant.Junction6Way:
                    BuildJunction6Way(verts, normals, uvs, indices);
                    break;
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ════════════════════════════════════════════════════════════
        //  GEOMETRY BUILDERS
        // ════════════════════════════════════════════════════════════

        private static void BuildStraightConduit(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices, float length)
        {
            Vector3 p0 = Vector3.zero;
            Vector3 p1 = Vector3.forward * length;
            Vector3 dir = Vector3.forward;
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;

            // Twin tubes
            BuildTubeSegment(verts, normals, uvs, indices, p0 - right * (TubeSeparation * 0.5f), p1 - right * (TubeSeparation * 0.5f), TubeRadius, 12, length * 2f);
            BuildTubeSegment(verts, normals, uvs, indices, p0 + right * (TubeSeparation * 0.5f), p1 + right * (TubeSeparation * 0.5f), TubeRadius, 12, length * 2f);

            // Flange plates at endpoints
            BuildEndFlange(verts, normals, uvs, indices, p0, -dir, right, up);
            BuildEndFlange(verts, normals, uvs, indices, p1, dir, right, up);
        }

        private static void BuildHorizontalBend(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            // 90° bend from +Z to +X centered at (1, 0, 0) relative to start
            const int Steps = 14;
            Vector3 center = new Vector3(1f, 0f, 0f);
            float radius = 1.0f;

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float angle = Mathf.PI * (1f - 0.5f * t); // from 180° to 90°
                Vector3 basePt = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)).normalized;
                Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;

                pathLeft[i] = basePt - normal * (TubeSeparation * 0.5f);
                pathRight[i] = basePt + normal * (TubeSeparation * 0.5f);
            }

            BuildTubeAlongPath(verts, normals, uvs, indices, pathLeft, TubeRadius, 12, 2.5f);
            BuildTubeAlongPath(verts, normals, uvs, indices, pathRight, TubeRadius, 12, 2.5f);

            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildEndFlange(verts, normals, uvs, indices, new Vector3(1f, 0f, 1f), Vector3.right, Vector3.forward, Vector3.up);
        }

        private static void BuildVerticalBend(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            // 90° bend from +Z to +Y
            const int Steps = 14;
            Vector3 center = new Vector3(0f, 1f, 0f);
            float radius = 1.0f;

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float angle = Mathf.PI * (1.5f - 0.5f * t); // from 270° to 180°
                Vector3 basePt = center + new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle)) * radius;

                pathLeft[i] = basePt - Vector3.right * (TubeSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.right * (TubeSeparation * 0.5f);
            }

            BuildTubeAlongPath(verts, normals, uvs, indices, pathLeft, TubeRadius, 12, 2.5f);
            BuildTubeAlongPath(verts, normals, uvs, indices, pathRight, TubeRadius, 12, 2.5f);

            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 1f, 1f), Vector3.up, Vector3.right, Vector3.forward);
        }

        private static void BuildVerticalStep(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            // S-curve going from (0, 0, 0) forward to (0, 1, 2)
            const int Steps = 18;
            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float z = t * 2.0f;
                float y = 0.5f * (1f - Mathf.Cos(t * Mathf.PI)); // smooth S-curve from 0 to 1

                Vector3 basePt = new Vector3(0f, y, z);
                pathLeft[i] = basePt - Vector3.right * (TubeSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.right * (TubeSeparation * 0.5f);
            }

            BuildTubeAlongPath(verts, normals, uvs, indices, pathLeft, TubeRadius, 12, 3.5f);
            BuildTubeAlongPath(verts, normals, uvs, indices, pathRight, TubeRadius, 12, 3.5f);

            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 1f, 2f), Vector3.forward, Vector3.right, Vector3.up);
        }

        private static void BuildHorizontalStep(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            // S-curve going from (0, 0, 0) to (1, 0, 2)
            const int Steps = 18;
            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float z = t * 2.0f;
                float x = 0.5f * (1f - Mathf.Cos(t * Mathf.PI)); // smooth S-curve from 0 to 1

                Vector3 basePt = new Vector3(x, 0f, z);
                pathLeft[i] = basePt - Vector3.right * (TubeSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.right * (TubeSeparation * 0.5f);
            }

            BuildTubeAlongPath(verts, normals, uvs, indices, pathLeft, TubeRadius, 12, 3.5f);
            BuildTubeAlongPath(verts, normals, uvs, indices, pathRight, TubeRadius, 12, 3.5f);

            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildEndFlange(verts, normals, uvs, indices, new Vector3(1f, 0f, 2f), Vector3.forward, Vector3.right, Vector3.up);
        }

        private static void BuildCompoundBend(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices, bool isLeft)
        {
            const int Steps = 16;
            float sign = isLeft ? -1f : 1f;

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float x = sign * (1f - t);
                float y = Mathf.Sin(t * Mathf.PI * 0.5f);
                float z = (1f - Mathf.Cos(t * Mathf.PI * 0.5f)) * 0.5f;

                Vector3 basePt = new Vector3(x, y, z);
                pathLeft[i] = basePt - Vector3.forward * (TubeSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.forward * (TubeSeparation * 0.5f);
            }

            BuildTubeAlongPath(verts, normals, uvs, indices, pathLeft, TubeRadius, 12, 2.5f);
            BuildTubeAlongPath(verts, normals, uvs, indices, pathRight, TubeRadius, 12, 2.5f);

            BuildEndFlange(verts, normals, uvs, indices, new Vector3(sign, 0f, 0f), -Vector3.right * sign, Vector3.forward, Vector3.up);
            BuildEndFlange(verts, normals, uvs, indices, new Vector3(0f, 1f, 0.5f), Vector3.up, Vector3.right, Vector3.forward);
        }

        private static void BuildJunction4Way(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            Vector3 center = Vector3.zero;
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            float armLen = 0.5f;

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector3 dir = dirs[d];
                Vector3 right = (Mathf.Abs(dir.x) > 0.5f) ? Vector3.forward : Vector3.right;
                Vector3 p0 = center;
                Vector3 p1 = center + dir * armLen;

                BuildTubeSegment(verts, normals, uvs, indices, p0 - right * (TubeSeparation * 0.5f), p1 - right * (TubeSeparation * 0.5f), TubeRadius, 12, 1.0f);
                BuildTubeSegment(verts, normals, uvs, indices, p0 + right * (TubeSeparation * 0.5f), p1 + right * (TubeSeparation * 0.5f), TubeRadius, 12, 1.0f);
                BuildEndFlange(verts, normals, uvs, indices, p1, dir, right, Vector3.up);
            }

            // Central hub block
            BuildBox(verts, normals, uvs, indices, center, Vector3.one * 0.28f);
        }

        private static void BuildJunction6Way(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            Vector3 center = Vector3.zero;
            Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            float armLen = 0.5f;

            for (int d = 0; d < dirs.Length; d++)
            {
                Vector3 dir = dirs[d];
                Vector3 right = Mathf.Abs(dir.y) > 0.5f ? Vector3.right : (Mathf.Abs(dir.x) > 0.5f ? Vector3.forward : Vector3.right);
                Vector3 up = Vector3.Cross(dir, right).normalized;
                Vector3 p0 = center;
                Vector3 p1 = center + dir * armLen;

                BuildTubeSegment(verts, normals, uvs, indices, p0 - right * (TubeSeparation * 0.5f), p1 - right * (TubeSeparation * 0.5f), TubeRadius, 12, 1.0f);
                BuildTubeSegment(verts, normals, uvs, indices, p0 + right * (TubeSeparation * 0.5f), p1 + right * (TubeSeparation * 0.5f), TubeRadius, 12, 1.0f);
                BuildEndFlange(verts, normals, uvs, indices, p1, dir, right, up);
            }

            // Central hub block
            BuildBox(verts, normals, uvs, indices, center, Vector3.one * 0.32f);
        }

        // ════════════════════════════════════════════════════════════
        //  PRIMITIVE BUILDERS
        // ════════════════════════════════════════════════════════════

        private static void BuildTubeSegment(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                             Vector3 start, Vector3 end, float radius, int sides, float uvLength)
        {
            Vector3 dir = (end - start).normalized;
            Vector3 seed = Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Normalize(Vector3.Cross(dir, seed));
            Vector3 v = Vector3.Normalize(Vector3.Cross(dir, u));

            int startIdx = verts.Count;
            for (int r = 0; r <= 1; r++)
            {
                Vector3 center = r == 0 ? start : end;
                float vCoord = r == 0 ? 0f : uvLength;
                for (int s = 0; s <= sides; s++)
                {
                    float angle = (s / (float)sides) * Mathf.PI * 2f;
                    Vector3 n = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    verts.Add(center + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)sides, vCoord));
                }
            }

            int stride = sides + 1;
            for (int s = 0; s < sides; s++)
            {
                int i0 = startIdx + s;
                int i1 = startIdx + s + 1;
                int i2 = startIdx + stride + s;
                int i3 = startIdx + stride + s + 1;

                indices.Add(i0); indices.Add(i2); indices.Add(i1);
                indices.Add(i1); indices.Add(i2); indices.Add(i3);
            }
        }

        private static void BuildTubeAlongPath(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                               Vector3[] path, float radius, int sides, float uvScale)
        {
            if (path == null || path.Length < 2) return;

            int rings = path.Length;
            int startIdx = verts.Count;
            float totalLen = 0f;
            for (int i = 0; i < rings - 1; i++) totalLen += Vector3.Distance(path[i], path[i + 1]);

            float curLen = 0f;
            for (int r = 0; r < rings; r++)
            {
                if (r > 0) curLen += Vector3.Distance(path[r - 1], path[r]);
                Vector3 dir = r < rings - 1 ? (path[r + 1] - path[r]).normalized : (path[r] - path[r - 1]).normalized;
                Vector3 seed = Mathf.Abs(dir.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 u = Vector3.Normalize(Vector3.Cross(dir, seed));
                Vector3 v = Vector3.Normalize(Vector3.Cross(dir, u));

                float vCoord = (curLen / Mathf.Max(0.001f, totalLen)) * uvScale;
                for (int s = 0; s <= sides; s++)
                {
                    float angle = (s / (float)sides) * Mathf.PI * 2f;
                    Vector3 n = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    verts.Add(path[r] + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)sides, vCoord));
                }
            }

            int stride = sides + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int i0 = startIdx + r * stride + s;
                    int i1 = startIdx + r * stride + s + 1;
                    int i2 = startIdx + (r + 1) * stride + s;
                    int i3 = startIdx + (r + 1) * stride + s + 1;

                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
                }
            }
        }

        private static void BuildEndFlange(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                           Vector3 center, Vector3 normal, Vector3 right, Vector3 up)
        {
            Vector3 fwd = normal.normalized;
            Vector3 r = right.normalized * (FlangeWidth * 0.5f);
            Vector3 u = up.normalized * (FlangeHeight * 0.5f);
            Vector3 d = fwd * FlangeDepth;

            // Rounded plate / box
            BuildBoxOriented(verts, normals, uvs, indices, center - fwd * (FlangeDepth * 0.5f), r * 2f, u * 2f, d);

            // Two circular collar bezels around where tubes enter
            Vector3 pLeft = center - r * (TubeSeparation / FlangeWidth);
            Vector3 pRight = center + r * (TubeSeparation / FlangeWidth);
            BuildCollarRing(verts, normals, uvs, indices, pLeft, fwd, TubeRadius * 1.35f, FlangeDepth * 0.6f);
            BuildCollarRing(verts, normals, uvs, indices, pRight, fwd, TubeRadius * 1.35f, FlangeDepth * 0.6f);

            // 4 Corner bolts
            float boltX = FlangeWidth * 0.42f;
            float boltY = FlangeHeight * 0.35f;
            BuildBolt(verts, normals, uvs, indices, center - r * (boltX / (FlangeWidth * 0.5f)) - u * (boltY / (FlangeHeight * 0.5f)), fwd);
            BuildBolt(verts, normals, uvs, indices, center + r * (boltX / (FlangeWidth * 0.5f)) - u * (boltY / (FlangeHeight * 0.5f)), fwd);
            BuildBolt(verts, normals, uvs, indices, center - r * (boltX / (FlangeWidth * 0.5f)) + u * (boltY / (FlangeHeight * 0.5f)), fwd);
            BuildBolt(verts, normals, uvs, indices, center + r * (boltX / (FlangeWidth * 0.5f)) + u * (boltY / (FlangeHeight * 0.5f)), fwd);
        }

        private static void BuildCollarRing(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                            Vector3 center, Vector3 normal, float radius, float depth)
        {
            Vector3 seed = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Normalize(Vector3.Cross(normal, seed));
            Vector3 v = Vector3.Normalize(Vector3.Cross(normal, u));
            const int sides = 8;
            int startIdx = verts.Count;

            for (int r = 0; r <= 1; r++)
            {
                Vector3 c = center + normal * (r * depth);
                for (int s = 0; s <= sides; s++)
                {
                    float angle = (s / (float)sides) * Mathf.PI * 2f;
                    Vector3 n = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    verts.Add(c + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)sides, r));
                }
            }

            int stride = sides + 1;
            for (int s = 0; s < sides; s++)
            {
                int i0 = startIdx + s;
                int i1 = startIdx + s + 1;
                int i2 = startIdx + stride + s;
                int i3 = startIdx + stride + s + 1;

                indices.Add(i0); indices.Add(i2); indices.Add(i1);
                indices.Add(i1); indices.Add(i2); indices.Add(i3);
            }
        }

        private static void BuildBolt(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                      Vector3 center, Vector3 normal)
        {
            float size = 0.016f;
            Vector3 seed = Mathf.Abs(normal.y) > 0.9f ? Vector3.right : Vector3.up;
            Vector3 u = Vector3.Normalize(Vector3.Cross(normal, seed)) * size;
            Vector3 v = Vector3.Normalize(Vector3.Cross(normal, u)) * size;
            Vector3 d = normal * (size * 1.2f);
            BuildBoxOriented(verts, normals, uvs, indices, center + normal * (size * 0.5f), u * 2f, v * 2f, d);
        }

        private static void BuildBox(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                     Vector3 center, Vector3 size)
        {
            Vector3 r = Vector3.right * (size.x * 0.5f);
            Vector3 u = Vector3.up * (size.y * 0.5f);
            Vector3 f = Vector3.forward * (size.z * 0.5f);
            BuildBoxOriented(verts, normals, uvs, indices, center, r * 2f, u * 2f, f * 2f);
        }

        private static void BuildBoxOriented(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                             Vector3 center, Vector3 rVector, Vector3 uVector, Vector3 fVector)
        {
            Vector3 r = rVector * 0.5f;
            Vector3 u = uVector * 0.5f;
            Vector3 f = fVector * 0.5f;

            Vector3[] faceNormals = { f.normalized, -f.normalized, r.normalized, -r.normalized, u.normalized, -u.normalized };
            Vector3[][] faceVerts =
            {
                new[] { center - r - u + f, center + r - u + f, center + r + u + f, center - r + u + f }, // Front
                new[] { center + r - u - f, center - r - u - f, center - r + u - f, center + r + u - f }, // Back
                new[] { center + r - u + f, center + r - u - f, center + r + u - f, center + r + u + f }, // Right
                new[] { center - r - u - f, center - r - u + f, center - r + u + f, center - r + u - f }, // Left
                new[] { center - r + u + f, center + r + u + f, center + r + u - f, center - r + u - f }, // Top
                new[] { center - r - u - f, center + r - u - f, center + r - u + f, center - r - u + f }  // Bottom
            };

            for (int i = 0; i < 6; i++)
            {
                int baseIdx = verts.Count;
                Vector3 n = faceNormals[i];
                for (int v = 0; v < 4; v++)
                {
                    verts.Add(faceVerts[i][v]);
                    normals.Add(n);
                }
                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));

                indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
                indices.Add(baseIdx); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  MATERIALS & TIER STYLING
        // ════════════════════════════════════════════════════════════

        public static string NormalizeTier(string tierName)
        {
            if (string.IsNullOrEmpty(tierName)) return "copper";
            string lower = tierName.ToLowerInvariant();
            if (lower.Contains("super")) return "superconductor";
            if (lower.Contains("copper")) return "copper";
            if (lower.Contains("iron")) return "iron";
            if (lower.Contains("gold")) return "gold";
            return lower;
        }

        public static Material GetMaterialForTier(string tierName, Color fallbackTint)
        {
            string norm = NormalizeTier(tierName);
            string key = $"EnergyPipeMat_{norm}";
            if (s_materialCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { name = key };

            Texture2D hazardTex = GetOrCreateHazardTexture(norm);
            if (hazardTex != null)
            {
                mat.mainTexture = hazardTex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", hazardTex);
            }

            switch (norm)
            {
                case "copper":
                    // Oxidized copper with warm brownish-bronze and subtle patina touches
                    mat.color = new Color(0.72f, 0.44f, 0.28f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.85f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.65f);
                    break;
                case "iron":
                    // Dark rusted iron charcoal with warm rust highlights
                    mat.color = new Color(0.42f, 0.40f, 0.38f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.70f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.45f);
                    break;
                case "gold":
                    // Radiant yellow metallic gold
                    mat.color = new Color(0.96f, 0.82f, 0.20f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.95f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.88f);
                    break;
                case "superconductor":
                    // Sleek white and cyan with glowing accents
                    mat.color = new Color(0.88f, 0.95f, 1.0f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.30f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", new Color(0.20f, 0.75f, 1.0f) * 0.8f);
                    break;
                default:
                    mat.color = fallbackTint;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", fallbackTint);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.80f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.70f);
                    break;
            }

            s_materialCache[key] = mat;
            return mat;
        }

        private static Texture2D GetOrCreateHazardTexture(string tierName)
        {
            string norm = NormalizeTier(tierName);
            string key = $"Tex_Hazard_{norm}";
            if (s_textureCache.TryGetValue(key, out var tex) && tex != null) return tex;

            const int width = 128;
            const int height = 128;
            tex = new Texture2D(width, height, TextureFormat.RGBA32, true)
            {
                name = key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color32 baseColor;
            Color32 stripeColor;

            switch (norm)
            {
                case "copper":
                    baseColor = new Color32(45, 42, 38, 255);       // Dark industrial charcoal body
                    stripeColor = new Color32(185, 110, 65, 255);   // Copper hazard band
                    break;
                case "iron":
                    baseColor = new Color32(40, 40, 42, 255);       // Dark cast iron body
                    stripeColor = new Color32(165, 85, 40, 255);    // Rust-orange hazard band
                    break;
                case "gold":
                    baseColor = new Color32(35, 32, 25, 255);       // Dark polished body
                    stripeColor = new Color32(235, 195, 45, 255);   // Vibrant gold hazard band
                    break;
                case "superconductor":
                    baseColor = new Color32(230, 245, 255, 255);   // Pristine white ceramic
                    stripeColor = new Color32(40, 195, 255, 255);   // Cyan energy band
                    break;
                default:
                    baseColor = new Color32(40, 40, 40, 255);
                    stripeColor = new Color32(220, 180, 40, 255);
                    break;
            }

            var pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Diagonal spiral stripe pattern
                    int stripeVal = (x + y * 2) % 32;
                    bool isStripe = stripeVal >= 8 && stripeVal <= 18;
                    pixels[y * width + x] = isStripe ? stripeColor : baseColor;
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            s_textureCache[key] = tex;
            return tex;
        }
    }
}
