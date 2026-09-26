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
        BendUp = 2,         // 90° vertical bend (straight to up riser)
        StepUp = 3,         // Vertical S-step (straight -> 1 up -> straight)
        StepRight = 4,      // Horizontal S-curve (straight -> 1 right -> straight)
        BendLeftToUp = 5,   // 3D compound curve (left to up)
        BendRightToUp = 6,  // 3D compound curve (right to up)
        Junction4Way = 7,   // 4-way planar cross
        Junction6Way = 8    // 6-way 3D omni hub
    }

    /// <summary>
    /// Builds procedural dual-conduit industrial energy cables matching reference
    /// geometry with authentic rounded rectangular connector housings, recessed dual circular
    /// port bezels, strain-relief boots, and tier-specific materials.
    /// </summary>
    public static class EnergyPipeMeshBuilder
    {
        public const float CableRadius = 0.036f;
        public const float CableSeparation = 0.14f;
        public const float FlangeWidth = 0.27f;
        public const float FlangeHeight = 0.135f;
        public const float FlangeDepth = 0.045f;
        public const float PortRadius = 0.042f;

        private static readonly Dictionary<string, Material> s_materialCache = new();
        private static readonly Dictionary<string, Texture2D> s_textureCache = new();

        public struct EndpointInfo
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector3 Right;
            public Vector3 Up;
        }

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
            Vector3 right = Vector3.right;
            Vector3 up = Vector3.up;

            // Dual parallel cables
            BuildCableSegment(verts, normals, uvs, indices, p0 - right * (CableSeparation * 0.5f), p1 - right * (CableSeparation * 0.5f), CableRadius, 12, length * 2.5f);
            BuildCableSegment(verts, normals, uvs, indices, p0 + right * (CableSeparation * 0.5f), p1 + right * (CableSeparation * 0.5f), CableRadius, 12, length * 2.5f);

            // Connectors at both ends
            BuildConnectorHousing(verts, normals, uvs, indices, p0, Vector3.back, right, up);
            BuildConnectorHousing(verts, normals, uvs, indices, p1, Vector3.forward, right, up);
        }

        private static void BuildHorizontalBend(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            const int Steps = 16;
            Vector3 center = new Vector3(1f, 0f, 0f);
            float radius = 1.0f;

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float angle = Mathf.PI * (1f - 0.5f * t); // 180° to 90°
                Vector3 basePt = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                Vector3 tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle)).normalized;
                Vector3 normal = Vector3.Cross(Vector3.up, tangent).normalized;

                pathLeft[i] = basePt - normal * (CableSeparation * 0.5f);
                pathRight[i] = basePt + normal * (CableSeparation * 0.5f);
            }

            BuildCablePath(verts, normals, uvs, indices, pathLeft, CableRadius, 12, 3.0f);
            BuildCablePath(verts, normals, uvs, indices, pathRight, CableRadius, 12, 3.0f);

            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(1f, 0f, 1f), Vector3.right, Vector3.back, Vector3.up);
        }

        private static void BuildVerticalBend(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            // 90° vertical bend from +Z to +Y
            const int Steps = 16;
            Vector3 center = new Vector3(0f, 1f, 0f);
            float radius = 1.0f;

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float angle = Mathf.PI * (1.5f + 0.5f * t); // 270° to 360° (0°)
                Vector3 basePt = center + new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle)) * radius;

                pathLeft[i] = basePt - Vector3.right * (CableSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.right * (CableSeparation * 0.5f);
            }

            BuildCablePath(verts, normals, uvs, indices, pathLeft, CableRadius, 12, 3.0f);
            BuildCablePath(verts, normals, uvs, indices, pathRight, CableRadius, 12, 3.0f);

            // Connectors on BOTH ends: bottom entry at (0,0,0) and top riser exit at (0,1,1)
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 1f, 1f), Vector3.up, Vector3.right, Vector3.back);
        }

        private static void BuildVerticalStep(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            const int Steps = 20;
            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float z = t * 2.0f;
                float y = 0.5f * (1f - Mathf.Cos(t * Mathf.PI));

                Vector3 basePt = new Vector3(0f, y, z);
                pathLeft[i] = basePt - Vector3.right * (CableSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.right * (CableSeparation * 0.5f);
            }

            BuildCablePath(verts, normals, uvs, indices, pathLeft, CableRadius, 12, 4.0f);
            BuildCablePath(verts, normals, uvs, indices, pathRight, CableRadius, 12, 4.0f);

            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 1f, 2f), Vector3.forward, Vector3.right, Vector3.up);
        }

        private static void BuildHorizontalStep(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices)
        {
            const int Steps = 20;
            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float z = t * 2.0f;
                float x = 0.5f * (1f - Mathf.Cos(t * Mathf.PI));

                Vector3 basePt = new Vector3(x, 0f, z);
                Vector3 tangent = new Vector3(0.5f * Mathf.PI * Mathf.Sin(t * Mathf.PI), 0f, 2.0f).normalized;
                Vector3 perp = Vector3.Cross(Vector3.up, tangent).normalized;

                pathLeft[i] = basePt - perp * (CableSeparation * 0.5f);
                pathRight[i] = basePt + perp * (CableSeparation * 0.5f);
            }

            BuildCablePath(verts, normals, uvs, indices, pathLeft, CableRadius, 12, 4.0f);
            BuildCablePath(verts, normals, uvs, indices, pathRight, CableRadius, 12, 4.0f);

            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 0f, 0f), Vector3.back, Vector3.right, Vector3.up);
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(1f, 0f, 2f), Vector3.forward, Vector3.right, Vector3.up);
        }

        private static void BuildCompoundBend(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices, bool isLeft)
        {
            const int Steps = 16;
            float sign = isLeft ? -1f : 1f;
            Vector3 center = new Vector3(sign, 1f, 0f);

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
                float angle = Mathf.PI * (1.5f + 0.5f * t); // 270° to 360°
                float x = sign * (1f - Mathf.Sin(t * Mathf.PI * 0.5f));
                float y = 1f - Mathf.Cos(t * Mathf.PI * 0.5f);

                Vector3 basePt = new Vector3(x, y, 0f);
                pathLeft[i] = basePt - Vector3.forward * (CableSeparation * 0.5f);
                pathRight[i] = basePt + Vector3.forward * (CableSeparation * 0.5f);
            }

            BuildCablePath(verts, normals, uvs, indices, pathLeft, CableRadius, 12, 3.0f);
            BuildCablePath(verts, normals, uvs, indices, pathRight, CableRadius, 12, 3.0f);

            // Entry connector on the side at (sign, 0, 0) and exit connector at top (0, 1, 0)
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(sign, 0f, 0f), Vector3.right * sign, Vector3.forward * (isLeft ? 1f : -1f), Vector3.up);
            BuildConnectorHousing(verts, normals, uvs, indices, new Vector3(0f, 1f, 0f), Vector3.up, Vector3.forward * (isLeft ? 1f : -1f), Vector3.right * (isLeft ? -1f : 1f));
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

                BuildCableSegment(verts, normals, uvs, indices, p0 - right * (CableSeparation * 0.5f), p1 - right * (CableSeparation * 0.5f), CableRadius, 12, 1.2f);
                BuildCableSegment(verts, normals, uvs, indices, p0 + right * (CableSeparation * 0.5f), p1 + right * (CableSeparation * 0.5f), CableRadius, 12, 1.2f);
                BuildConnectorHousing(verts, normals, uvs, indices, p1, dir, right, Vector3.up);
            }

            // Central junction cube
            BuildRoundedBox(verts, normals, uvs, indices, center, new Vector3(0.24f, 0.16f, 0.24f));
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

                BuildCableSegment(verts, normals, uvs, indices, p0 - right * (CableSeparation * 0.5f), p1 - right * (CableSeparation * 0.5f), CableRadius, 12, 1.2f);
                BuildCableSegment(verts, normals, uvs, indices, p0 + right * (CableSeparation * 0.5f), p1 + right * (CableSeparation * 0.5f), CableRadius, 12, 1.2f);
                BuildConnectorHousing(verts, normals, uvs, indices, p1, dir, right, up);
            }

            // Central omni hub
            BuildRoundedBox(verts, normals, uvs, indices, center, new Vector3(0.26f, 0.26f, 0.26f));
        }

        // ════════════════════════════════════════════════════════════
        //  CONNECTOR HOUSING & SOCKETS (MATCHING REFERENCE PIC 5)
        // ════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the solid rounded rectangular connector block shown in Pic 5 with:
        /// 1. Rounded rectangular body matching cable tier color.
        /// 2. Two recessed dark circular port socket cups with contact core pins.
        /// 3. Strain-relief boot collars on the rear face connecting to each cable.
        /// </summary>
        public static void BuildConnectorHousing(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                                Vector3 origin, Vector3 normal, Vector3 right, Vector3 up)
        {
            normal = normal.normalized;
            right = right.normalized;
            up = up.normalized;

            float halfW = FlangeWidth * 0.5f;
            float halfH = FlangeHeight * 0.5f;
            float depth = FlangeDepth;
            float cornerR = 0.024f;

            Vector3 frontCenter = origin;
            Vector3 backCenter = origin - normal * depth;

            // ── 1. Rounded Rectangular Housing ────────────────────────
            // 8-sided rounded rectangle profile on the (right, up) plane
            Vector2[] profile = new Vector2[8];
            float rx = halfW - cornerR;
            float ry = halfH - cornerR;
            profile[0] = new Vector2(halfW, ry);
            profile[1] = new Vector2(rx, halfH);
            profile[2] = new Vector2(-rx, halfH);
            profile[3] = new Vector2(-halfW, ry);
            profile[4] = new Vector2(-halfW, -ry);
            profile[5] = new Vector2(-rx, -halfH);
            profile[6] = new Vector2(rx, -halfH);
            profile[7] = new Vector2(halfW, -ry);

            int startFront = verts.Count;
            // Front face vertices
            for (int i = 0; i < 8; i++)
            {
                verts.Add(frontCenter + right * profile[i].x + up * profile[i].y);
                normals.Add(normal);
                uvs.Add(new Vector2(0.5f + (profile[i].x / FlangeWidth) * 0.4f, 0.65f + (profile[i].y / FlangeHeight) * 0.2f));
            }
            // Front center
            int frontCenterIdx = verts.Count;
            verts.Add(frontCenter);
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.65f));

            // Front face fan triangles
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                indices.Add(frontCenterIdx);
                indices.Add(startFront + i);
                indices.Add(startFront + next);
            }

            int startBack = verts.Count;
            // Back face vertices
            for (int i = 0; i < 8; i++)
            {
                verts.Add(backCenter + right * profile[i].x + up * profile[i].y);
                normals.Add(-normal);
                uvs.Add(new Vector2(0.5f + (profile[i].x / FlangeWidth) * 0.4f, 0.65f + (profile[i].y / FlangeHeight) * 0.2f));
            }
            int backCenterIdx = verts.Count;
            verts.Add(backCenter);
            normals.Add(-normal);
            uvs.Add(new Vector2(0.5f, 0.65f));

            // Back face fan triangles
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                indices.Add(backCenterIdx);
                indices.Add(startBack + next);
                indices.Add(startBack + i);
            }

            // Housing side walls (extruding front to back)
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                Vector3 f0 = frontCenter + right * profile[i].x + up * profile[i].y;
                Vector3 f1 = frontCenter + right * profile[next].x + up * profile[next].y;
                Vector3 b0 = backCenter + right * profile[i].x + up * profile[i].y;
                Vector3 b1 = backCenter + right * profile[next].x + up * profile[next].y;

                Vector3 wallNormal = Vector3.Normalize(Vector3.Cross(f1 - f0, normal));
                AddQuad(verts, normals, uvs, indices, f0, f1, b1, b0, wallNormal, new Vector2(0.5f, 0.65f));
            }

            // ── 2. Dual Recessed Circular Port Sockets (Pic 5) ────────
            float[] offsets = { -CableSeparation * 0.5f, CableSeparation * 0.5f };
            const int PortSides = 12;
            float outerR = PortRadius;
            float innerR = PortRadius * 0.82f;
            float cupDepth = 0.015f;
            float pinR = 0.020f;

            for (int p = 0; p < offsets.Length; p++)
            {
                Vector3 portCenterFront = frontCenter + right * offsets[p] + normal * 0.001f;
                Vector3 portCenterRecess = portCenterFront - normal * cupDepth;

                // Dark outer grommet / bezel ring sitting slightly raised on the front face
                int bezelStart = verts.Count;
                for (int s = 0; s <= PortSides; s++)
                {
                    float angle = (s / (float)PortSides) * Mathf.PI * 2f;
                    Vector3 radial = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).normalized;

                    // Outer bezel ring (Region C - dark trim)
                    verts.Add(portCenterFront + radial * outerR);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.35f, 0.88f + Mathf.Sin(angle) * 0.09f));

                    // Inner socket lip (Region C - dark trim)
                    verts.Add(portCenterFront + radial * innerR);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.25f, 0.88f + Mathf.Sin(angle) * 0.06f));
                }

                for (int s = 0; s < PortSides; s++)
                {
                    int i0 = bezelStart + s * 2;
                    int i1 = bezelStart + s * 2 + 1;
                    int i2 = bezelStart + (s + 1) * 2;
                    int i3 = bezelStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    indices.Add(i1); indices.Add(i3); indices.Add(i2);
                }

                // Cylindrical recessed cup wall
                int cupStart = verts.Count;
                for (int s = 0; s <= PortSides; s++)
                {
                    float angle = (s / (float)PortSides) * Mathf.PI * 2f;
                    Vector3 radial = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).normalized;

                    verts.Add(portCenterFront + radial * innerR);
                    normals.Add(-radial);
                    uvs.Add(new Vector2(s / (float)PortSides, 0.92f));

                    verts.Add(portCenterRecess + radial * innerR);
                    normals.Add(-radial);
                    uvs.Add(new Vector2(s / (float)PortSides, 0.84f));
                }

                for (int s = 0; s < PortSides; s++)
                {
                    int i0 = cupStart + s * 2;
                    int i1 = cupStart + s * 2 + 1;
                    int i2 = cupStart + (s + 1) * 2;
                    int i3 = cupStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    indices.Add(i1); indices.Add(i3); indices.Add(i2);
                }

                // Recessed cup bottom floor + central contact pin
                int pinStart = verts.Count;
                for (int s = 0; s <= PortSides; s++)
                {
                    float angle = (s / (float)PortSides) * Mathf.PI * 2f;
                    Vector3 radial = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).normalized;

                    // Inner cup floor
                    verts.Add(portCenterRecess + radial * innerR);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.2f, 0.88f + Mathf.Sin(angle) * 0.05f));

                    // Central metallic contact pin
                    verts.Add(portCenterRecess + radial * pinR + normal * (cupDepth * 0.6f));
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.15f, 0.65f + Mathf.Sin(angle) * 0.05f));
                }

                for (int s = 0; s < PortSides; s++)
                {
                    int i0 = pinStart + s * 2;
                    int i1 = pinStart + s * 2 + 1;
                    int i2 = pinStart + (s + 1) * 2;
                    int i3 = pinStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    indices.Add(i1); indices.Add(i3); indices.Add(i2);
                }

                // Central pin top cap
                int pinCenterIdx = verts.Count;
                verts.Add(portCenterRecess + normal * (cupDepth * 0.6f));
                normals.Add(normal);
                uvs.Add(new Vector2(0.5f, 0.65f));

                for (int s = 0; s < PortSides; s++)
                {
                    int i1 = pinStart + s * 2 + 1;
                    int i2 = pinStart + (s + 1) * 2 + 1;
                    indices.Add(pinCenterIdx);
                    indices.Add(i1);
                    indices.Add(i2);
                }

                // ── 3. Strain-Relief Cable Collar Boots on Rear Face ──
                Vector3 bootBase = backCenter + right * offsets[p];
                Vector3 bootTip = bootBase - normal * 0.025f;
                int bootStart = verts.Count;
                for (int s = 0; s <= PortSides; s++)
                {
                    float angle = (s / (float)PortSides) * Mathf.PI * 2f;
                    Vector3 radial = (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)).normalized;

                    verts.Add(bootBase + radial * (CableRadius * 1.35f));
                    normals.Add(-normal + radial * 0.5f);
                    uvs.Add(new Vector2(s / (float)PortSides, 0.88f));

                    verts.Add(bootTip + radial * CableRadius);
                    normals.Add(-normal + radial * 0.5f);
                    uvs.Add(new Vector2(s / (float)PortSides, 0.94f));
                }

                for (int s = 0; s < PortSides; s++)
                {
                    int i0 = bootStart + s * 2;
                    int i1 = bootStart + s * 2 + 1;
                    int i2 = bootStart + (s + 1) * 2;
                    int i3 = bootStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    indices.Add(i1); indices.Add(i3); indices.Add(i2);
                }
            }
        }

        // ════════════════════════════════════════════════════════════
        //  CABLE CYLINDERS & SMOOTH PATHS
        // ════════════════════════════════════════════════════════════

        private static void BuildCableSegment(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
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
                    uvs.Add(new Vector2(s / (float)sides, (vCoord % 1.0f) * 0.48f));
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

        private static void BuildCablePath(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                          Vector3[] path, float radius, int sides, float uvLength)
        {
            if (path == null || path.Length < 2) return;

            int rings = path.Length;
            int startIdx = verts.Count;

            for (int r = 0; r < rings; r++)
            {
                Vector3 p = path[r];
                Vector3 tangent = (r == 0) ? (path[1] - path[0]).normalized :
                                  (r == rings - 1) ? (path[rings - 1] - path[rings - 2]).normalized :
                                  ((path[r + 1] - path[r - 1]) * 0.5f).normalized;

                Vector3 seed = Mathf.Abs(tangent.y) > 0.9f ? Vector3.right : Vector3.up;
                Vector3 u = Vector3.Normalize(Vector3.Cross(tangent, seed));
                Vector3 v = Vector3.Normalize(Vector3.Cross(tangent, u));

                float t = r / (float)(rings - 1);
                float vCoord = t * uvLength;

                for (int s = 0; s <= sides; s++)
                {
                    float angle = (s / (float)sides) * Mathf.PI * 2f;
                    Vector3 n = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                    verts.Add(p + n * radius);
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)sides, (vCoord % 1.0f) * 0.48f));
                }
            }

            int stride = sides + 1;
            for (int r = 0; r < rings - 1; r++)
            {
                int base0 = startIdx + r * stride;
                int base1 = startIdx + (r + 1) * stride;

                for (int s = 0; s < sides; s++)
                {
                    int i0 = base0 + s;
                    int i1 = base0 + s + 1;
                    int i2 = base1 + s;
                    int i3 = base1 + s + 1;

                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
                }
            }
        }

        private static void BuildRoundedBox(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                           Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3[] faceNormals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };
            Vector3[] faceRights = { Vector3.right, Vector3.left, Vector3.back, Vector3.forward, Vector3.right, Vector3.right };
            Vector3[] faceUps = { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward, Vector3.back };

            for (int f = 0; f < 6; f++)
            {
                Vector3 n = faceNormals[f];
                Vector3 r = faceRights[f];
                Vector3 u = faceUps[f];
                float extR = Mathf.Abs(Vector3.Dot(r, h));
                float extU = Mathf.Abs(Vector3.Dot(u, h));
                float extN = Mathf.Abs(Vector3.Dot(n, h));

                Vector3 p0 = center + n * extN - r * extR - u * extU;
                Vector3 p1 = center + n * extN + r * extR - u * extU;
                Vector3 p2 = center + n * extN + r * extR + u * extU;
                Vector3 p3 = center + n * extN - r * extR + u * extU;

                AddQuad(verts, normals, uvs, indices, p0, p1, p2, p3, n, new Vector2(0.5f, 0.65f));
            }
        }

        private static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> indices,
                                    Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal, Vector2 uvCenter)
        {
            int baseIdx = verts.Count;
            verts.Add(p0); normals.Add(normal); uvs.Add(uvCenter + new Vector2(-0.1f, -0.1f));
            verts.Add(p1); normals.Add(normal); uvs.Add(uvCenter + new Vector2(0.1f, -0.1f));
            verts.Add(p2); normals.Add(normal); uvs.Add(uvCenter + new Vector2(0.1f, 0.1f));
            verts.Add(p3); normals.Add(normal); uvs.Add(uvCenter + new Vector2(-0.1f, 0.1f));

            indices.Add(baseIdx); indices.Add(baseIdx + 1); indices.Add(baseIdx + 2);
            indices.Add(baseIdx); indices.Add(baseIdx + 2); indices.Add(baseIdx + 3);
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

            Texture2D tex = GetOrCreateCableTexture(norm);
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }

            switch (norm)
            {
                case "copper":
                    // Oxidized copper with warm brownish-bronze and subtle patina touches
                    mat.color = new Color(0.85f, 0.48f, 0.22f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.85f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.65f);
                    break;
                case "iron":
                    // Dark rusted iron charcoal with warm rust highlights
                    mat.color = new Color(0.48f, 0.45f, 0.42f, 1f);
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
                    // Sleek white and cyan ceramic with glowing accents
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

        private static Texture2D GetOrCreateCableTexture(string normTier)
        {
            string key = $"Tex_EnergyCable_{normTier}";
            if (s_textureCache.TryGetValue(key, out var tex) && tex != null) return tex;

            const int size = 256;
            tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = key,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color32 tierMetalColor;
            Color32 cableSheathColor;
            Color32 cableStripeColor;
            Color32 darkTrimColor = new Color32(28, 28, 32, 255);

            switch (normTier)
            {
                case "copper":
                    tierMetalColor = new Color32(215, 115, 55, 255);      // Rich copper orange (Pic 5)
                    cableSheathColor = new Color32(42, 40, 38, 255);     // Dark insulated cable sheath
                    cableStripeColor = new Color32(185, 95, 45, 255);    // Copper hazard/rib band
                    break;
                case "iron":
                    tierMetalColor = new Color32(120, 115, 110, 255);    // Cast iron metal
                    cableSheathColor = new Color32(36, 36, 38, 255);     // Dark charcoal sheath
                    cableStripeColor = new Color32(145, 75, 35, 255);    // Rust hazard band
                    break;
                case "gold":
                    tierMetalColor = new Color32(245, 205, 45, 255);     // Metallic gold
                    cableSheathColor = new Color32(38, 34, 26, 255);     // Deep gold-tinged sheath
                    cableStripeColor = new Color32(230, 190, 40, 255);   // Gold band
                    break;
                case "superconductor":
                    tierMetalColor = new Color32(225, 245, 255, 255);    // Ceramic white
                    cableSheathColor = new Color32(20, 30, 45, 255);     // Deep space blue sheath
                    cableStripeColor = new Color32(35, 190, 255, 255);   // Cyan energy band
                    break;
                default:
                    tierMetalColor = new Color32(180, 180, 180, 255);
                    cableSheathColor = new Color32(40, 40, 40, 255);
                    cableStripeColor = new Color32(220, 180, 40, 255);
                    break;
            }

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (y < 128)
                    {
                        // ── Region A: Cable Sheath & Ribs (y: 0..127) ──
                        float diag = (x + y * 2) % 32;
                        bool isStripe = diag < 10;
                        pixels[y * size + x] = isStripe ? cableStripeColor : cableSheathColor;
                    }
                    else if (y < 200)
                    {
                        // ── Region B: Tier Metal Housing Body (y: 128..199) ──
                        // Subtle horizontal brushed metal grain
                        int grain = (x % 4 == 0) ? 6 : 0;
                        byte r = (byte)Mathf.Clamp(tierMetalColor.r + grain, 0, 255);
                        byte g = (byte)Mathf.Clamp(tierMetalColor.g + grain, 0, 255);
                        byte b = (byte)Mathf.Clamp(tierMetalColor.b + grain, 0, 255);
                        pixels[y * size + x] = new Color32(r, g, b, 255);
                    }
                    else
                    {
                        // ── Region C: Dark Grommet / Socket Trim (y: 200..255) ──
                        pixels[y * size + x] = darkTrimColor;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            s_textureCache[key] = tex;
            return tex;
        }
    }
}
