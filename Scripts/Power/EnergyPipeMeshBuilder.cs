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

        public static List<EndpointInfo> GetLocalEndpoints(EnergyPipeVariant variant, int straightLength = 1)
        {
            var list = new List<EndpointInfo>();
            straightLength = Mathf.Clamp(straightLength, 1, 5);
            switch (variant)
            {
                case EnergyPipeVariant.Straight:
                    list.Add(new EndpointInfo { Position = Vector3.zero, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.forward * straightLength, Normal = Vector3.forward, Right = Vector3.right, Up = Vector3.up });
                    break;
                case EnergyPipeVariant.BendRight:
                    list.Add(new EndpointInfo { Position = Vector3.zero, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = new Vector3(1f, 0f, 1f), Normal = Vector3.right, Right = Vector3.back, Up = Vector3.up });
                    break;
                case EnergyPipeVariant.BendUp:
                    list.Add(new EndpointInfo { Position = Vector3.zero, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = new Vector3(0f, 1f, 1f), Normal = Vector3.up, Right = Vector3.right, Up = Vector3.back });
                    break;
                case EnergyPipeVariant.StepUp:
                    list.Add(new EndpointInfo { Position = Vector3.zero, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = new Vector3(0f, 1f, 2f), Normal = Vector3.forward, Right = Vector3.right, Up = Vector3.up });
                    break;
                case EnergyPipeVariant.StepRight:
                    list.Add(new EndpointInfo { Position = Vector3.zero, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = new Vector3(1f, 0f, 2f), Normal = Vector3.forward, Right = Vector3.right, Up = Vector3.up });
                    break;
                case EnergyPipeVariant.BendLeftToUp:
                    list.Add(new EndpointInfo { Position = new Vector3(-1f, 0f, 0f), Normal = Vector3.left, Right = Vector3.forward, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = new Vector3(0f, 1f, 0f), Normal = Vector3.up, Right = Vector3.forward, Up = Vector3.left });
                    break;
                case EnergyPipeVariant.BendRightToUp:
                    list.Add(new EndpointInfo { Position = new Vector3(1f, 0f, 0f), Normal = Vector3.right, Right = Vector3.back, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = new Vector3(0f, 1f, 0f), Normal = Vector3.up, Right = Vector3.back, Up = Vector3.right });
                    break;
                case EnergyPipeVariant.Junction4Way:
                    list.Add(new EndpointInfo { Position = Vector3.forward * 0.5f, Normal = Vector3.forward, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.back * 0.5f, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.right * 0.5f, Normal = Vector3.right, Right = Vector3.back, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.left * 0.5f, Normal = Vector3.left, Right = Vector3.forward, Up = Vector3.up });
                    break;
                case EnergyPipeVariant.Junction6Way:
                    list.Add(new EndpointInfo { Position = Vector3.forward * 0.5f, Normal = Vector3.forward, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.back * 0.5f, Normal = Vector3.back, Right = Vector3.right, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.right * 0.5f, Normal = Vector3.right, Right = Vector3.back, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.left * 0.5f, Normal = Vector3.left, Right = Vector3.forward, Up = Vector3.up });
                    list.Add(new EndpointInfo { Position = Vector3.up * 0.5f, Normal = Vector3.up, Right = Vector3.right, Up = Vector3.back });
                    list.Add(new EndpointInfo { Position = Vector3.down * 0.5f, Normal = Vector3.down, Right = Vector3.right, Up = Vector3.forward });
                    break;
            }
            return list;
        }

        public static Mesh BuildMesh(EnergyPipeVariant variant, int straightLength = 1, List<Vector3> machineTargetsLocal = null)
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

            // If connected to machine endpoints with a small gap, extend the conduit flush to the machine face
            if (machineTargetsLocal != null && machineTargetsLocal.Count > 0)
            {
                var endpoints = GetLocalEndpoints(variant, straightLength);
                for (int m = 0; m < machineTargetsLocal.Count; m++)
                {
                    Vector3 targetLocal = machineTargetsLocal[m];
                    float bestDist = float.MaxValue;
                    EndpointInfo closestEp = default;
                    for (int e = 0; e < endpoints.Count; e++)
                    {
                        float d = (endpoints[e].Position - targetLocal).sqrMagnitude;
                        if (d < bestDist)
                        {
                            bestDist = d;
                            closestEp = endpoints[e];
                        }
                    }

                    Vector3 p0 = closestEp.Position;
                    Vector3 p1 = targetLocal;
                    Vector3 diff = p1 - p0;
                    float len = diff.magnitude;
                    if (len > 0.03f && len < 1.4f)
                    {
                        Vector3 dir = diff / len;
                        Vector3 right = closestEp.Right;
                        Vector3 up = Vector3.Normalize(Vector3.Cross(dir, right));
                        if (up.sqrMagnitude < 0.01f) up = closestEp.Up;
                        right = Vector3.Normalize(Vector3.Cross(up, dir));

                        BuildCableSegment(verts, normals, uvs, indices, p0 - right * (CableSeparation * 0.5f), p1 - right * (CableSeparation * 0.5f), CableRadius, 12, len * 2.5f);
                        BuildCableSegment(verts, normals, uvs, indices, p0 + right * (CableSeparation * 0.5f), p1 + right * (CableSeparation * 0.5f), CableRadius, 12, len * 2.5f);
                        BuildConnectorHousing(verts, normals, uvs, indices, p1, dir, right, up);
                    }
                }
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

            var pathLeft = new Vector3[Steps + 1];
            var pathRight = new Vector3[Steps + 1];

            for (int i = 0; i <= Steps; i++)
            {
                float t = i / (float)Steps;
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

            float halfW = FlangeWidth * 0.5f;   // 0.135m
            float halfH = FlangeHeight * 0.5f;  // 0.068m
            float depth = FlangeDepth;          // 0.045m
            float cornerR = 0.022f;

            Vector3 frontCenter = origin;
            Vector3 backCenter = origin - normal * depth;

            // 8-point rounded rectangle profile in (right, up) coordinates (Clockwise from top)
            float rx = halfW - cornerR;
            float ry = halfH - cornerR;
            Vector2[] p = new Vector2[8];
            p[0] = new Vector2(-rx, halfH);   // top-left
            p[1] = new Vector2(rx, halfH);    // top-right
            p[2] = new Vector2(halfW, ry);    // right-top
            p[3] = new Vector2(halfW, -ry);   // right-bottom
            p[4] = new Vector2(rx, -halfH);   // bottom-right
            p[5] = new Vector2(-rx, -halfH);  // bottom-left
            p[6] = new Vector2(-halfW, -ry);  // left-bottom
            p[7] = new Vector2(-halfW, ry);   // left-top

            // ── 1. Front Face (Opaque, Solid, Wound Clockwise) ───────
            int frontCenterIdx = verts.Count;
            verts.Add(frontCenter);
            normals.Add(normal);
            uvs.Add(new Vector2(0.5f, 0.65f));

            int startFront = verts.Count;
            for (int i = 0; i < 8; i++)
            {
                verts.Add(frontCenter + right * p[i].x + up * p[i].y);
                normals.Add(normal);
                uvs.Add(new Vector2(0.5f + (p[i].x / FlangeWidth) * 0.4f, 0.65f + (p[i].y / FlangeHeight) * 0.2f));
            }

            // Front fan triangles (Clockwise: Center -> i -> next)
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                indices.Add(frontCenterIdx);
                indices.Add(startFront + i);
                indices.Add(startFront + next);
            }

            // ── 2. Back Face (Opaque, Solid, Wound Clockwise from back) ─
            int backCenterIdx = verts.Count;
            verts.Add(backCenter);
            normals.Add(-normal);
            uvs.Add(new Vector2(0.5f, 0.65f));

            int startBack = verts.Count;
            for (int i = 0; i < 8; i++)
            {
                verts.Add(backCenter + right * p[i].x + up * p[i].y);
                normals.Add(-normal);
                uvs.Add(new Vector2(0.5f + (p[i].x / FlangeWidth) * 0.4f, 0.65f + (p[i].y / FlangeHeight) * 0.2f));
            }

            // Back fan triangles (Clockwise when viewed from -normal: Center -> next -> i)
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                indices.Add(backCenterIdx);
                indices.Add(startBack + next);
                indices.Add(startBack + i);
            }

            // ── 3. Side Walls (Extruding Front to Back) ───────────────
            for (int i = 0; i < 8; i++)
            {
                int next = (i + 1) % 8;
                Vector3 f0 = frontCenter + right * p[i].x + up * p[i].y;
                Vector3 f1 = frontCenter + right * p[next].x + up * p[next].y;
                Vector3 b0 = backCenter + right * p[i].x + up * p[i].y;
                Vector3 b1 = backCenter + right * p[next].x + up * p[next].y;

                Vector3 wallNormal = Vector3.Normalize(Vector3.Cross(f1 - f0, -normal));
                int bIdx = verts.Count;
                verts.Add(f0); normals.Add(wallNormal); uvs.Add(new Vector2(0.4f, 0.65f));
                verts.Add(f1); normals.Add(wallNormal); uvs.Add(new Vector2(0.6f, 0.65f));
                verts.Add(b1); normals.Add(wallNormal); uvs.Add(new Vector2(0.6f, 0.60f));
                verts.Add(b0); normals.Add(wallNormal); uvs.Add(new Vector2(0.4f, 0.60f));

                indices.Add(bIdx); indices.Add(bIdx + 1); indices.Add(bIdx + 2);
                indices.Add(bIdx); indices.Add(bIdx + 2); indices.Add(bIdx + 3);
            }

            // ── 4. Dual Circular Recessed Sockets & Dark Grommet Bezels (Pic 5) ──
            float[] offsets = { -CableSeparation * 0.5f, CableSeparation * 0.5f };
            const int Sides = 14;
            float outerR = PortRadius;          // 0.042m (outer dark rubber bezel)
            float innerR = PortRadius * 0.78f;  // 0.033m (inner socket cup)
            float pinR = 0.018f;                // 0.018m (central metal pin)
            float cupDepth = 0.012f;

            for (int k = 0; k < offsets.Length; k++)
            {
                Vector3 portCenter = frontCenter + right * offsets[k] + normal * 0.002f;
                Vector3 socketFloor = portCenter - normal * cupDepth;

                // A. Raised dark circular bezel ring
                int bezelStart = verts.Count;
                for (int s = 0; s <= Sides; s++)
                {
                    float ang = (s / (float)Sides) * Mathf.PI * 2f;
                    Vector3 rad = (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)).normalized;

                    verts.Add(portCenter + rad * outerR);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.25f, 0.88f + Mathf.Sin(ang) * 0.08f));

                    verts.Add(portCenter + rad * innerR);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.18f, 0.88f + Mathf.Sin(ang) * 0.06f));
                }

                for (int s = 0; s < Sides; s++)
                {
                    int i0 = bezelStart + s * 2;
                    int i1 = bezelStart + s * 2 + 1;
                    int i2 = bezelStart + (s + 1) * 2;
                    int i3 = bezelStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
                }

                // B. Recessed socket cup cylinder wall
                int cupStart = verts.Count;
                for (int s = 0; s <= Sides; s++)
                {
                    float ang = (s / (float)Sides) * Mathf.PI * 2f;
                    Vector3 rad = (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)).normalized;

                    verts.Add(portCenter + rad * innerR);
                    normals.Add(-rad);
                    uvs.Add(new Vector2(s / (float)Sides, 0.90f));

                    verts.Add(socketFloor + rad * innerR);
                    normals.Add(-rad);
                    uvs.Add(new Vector2(s / (float)Sides, 0.82f));
                }

                for (int s = 0; s < Sides; s++)
                {
                    int i0 = cupStart + s * 2;
                    int i1 = cupStart + s * 2 + 1;
                    int i2 = cupStart + (s + 1) * 2;
                    int i3 = cupStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i1); indices.Add(i2);
                    indices.Add(i1); indices.Add(i3); indices.Add(i2);
                }

                // C. Central metal terminal contact pin
                int pinStart = verts.Count;
                for (int s = 0; s <= Sides; s++)
                {
                    float ang = (s / (float)Sides) * Mathf.PI * 2f;
                    Vector3 rad = (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)).normalized;

                    // Socket floor
                    verts.Add(socketFloor + rad * innerR);
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.15f, 0.88f + Mathf.Sin(ang) * 0.05f));

                    // Pin top
                    verts.Add(socketFloor + rad * pinR + normal * (cupDepth * 0.7f));
                    normals.Add(normal);
                    uvs.Add(new Vector2(0.5f + Mathf.Cos(ang) * 0.10f, 0.65f + Mathf.Sin(ang) * 0.04f));
                }

                for (int s = 0; s < Sides; s++)
                {
                    int i0 = pinStart + s * 2;
                    int i1 = pinStart + s * 2 + 1;
                    int i2 = pinStart + (s + 1) * 2;
                    int i3 = pinStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
                }

                // Pin top cap
                int pinCapIdx = verts.Count;
                verts.Add(socketFloor + normal * (cupDepth * 0.7f));
                normals.Add(normal);
                uvs.Add(new Vector2(0.5f, 0.65f));

                for (int s = 0; s < Sides; s++)
                {
                    int i1 = pinStart + s * 2 + 1;
                    int i2 = pinStart + (s + 1) * 2 + 1;
                    indices.Add(pinCapIdx);
                    indices.Add(i1);
                    indices.Add(i2);
                }

                // D. Rear strain-relief collar boot
                Vector3 bootBase = backCenter + right * offsets[k];
                Vector3 bootTip = bootBase - normal * 0.022f;
                int bootStart = verts.Count;
                for (int s = 0; s <= Sides; s++)
                {
                    float ang = (s / (float)Sides) * Mathf.PI * 2f;
                    Vector3 rad = (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)).normalized;

                    verts.Add(bootBase + rad * (CableRadius * 1.30f));
                    normals.Add(-normal + rad * 0.5f);
                    uvs.Add(new Vector2(s / (float)Sides, 0.88f));

                    verts.Add(bootTip + rad * CableRadius);
                    normals.Add(-normal + rad * 0.5f);
                    uvs.Add(new Vector2(s / (float)Sides, 0.94f));
                }

                for (int s = 0; s < Sides; s++)
                {
                    int i0 = bootStart + s * 2;
                    int i1 = bootStart + s * 2 + 1;
                    int i2 = bootStart + (s + 1) * 2;
                    int i3 = bootStart + (s + 1) * 2 + 1;

                    indices.Add(i0); indices.Add(i2); indices.Add(i1);
                    indices.Add(i1); indices.Add(i2); indices.Add(i3);
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

                int baseIdx = verts.Count;
                verts.Add(p0); normals.Add(n); uvs.Add(new Vector2(0.4f, 0.65f));
                verts.Add(p1); normals.Add(n); uvs.Add(new Vector2(0.6f, 0.65f));
                verts.Add(p2); normals.Add(n); uvs.Add(new Vector2(0.6f, 0.60f));
                verts.Add(p3); normals.Add(n); uvs.Add(new Vector2(0.4f, 0.60f));

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

            Texture2D tex = GetOrCreateCableTexture(norm);
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            }

            switch (norm)
            {
                case "copper":
                    // Rich warm oxidized copper bronze (matching Pic 5)
                    mat.color = new Color(0.85f, 0.44f, 0.20f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.35f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.30f);
                    break;
                case "iron":
                    // Dark rusted iron charcoal with warm rust highlights
                    mat.color = new Color(0.45f, 0.44f, 0.42f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.25f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.25f);
                    break;
                case "gold":
                    // Radiant yellow metallic gold
                    mat.color = new Color(0.95f, 0.80f, 0.18f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.50f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
                    break;
                case "superconductor":
                    // Sleek white and cyan ceramic with glowing accents
                    mat.color = new Color(0.90f, 0.96f, 1.0f, 1f);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.40f);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                    if (mat.HasProperty("_EmissionColor"))
                        mat.SetColor("_EmissionColor", new Color(0.15f, 0.65f, 0.95f) * 0.6f);
                    break;
                default:
                    mat.color = fallbackTint;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", fallbackTint);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.30f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.30f);
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
