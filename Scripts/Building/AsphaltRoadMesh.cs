// Assets/Scripts/VoxelEngine/Building/AsphaltRoadMesh.cs
//
// PROCEDURAL ROAD GEOMETRY — pure, static, no Unity scene access.
//
// One road cell is a 1 m slab that DRAPES over the terrain instead of floating above it or
// burying itself in it. The design line is "a road follows the ground instead of fighting
// it", and the only way to honour that on a voxel planet with terraces and gullies is to
// sample the ground under the cell and build the surface through those samples.
//
// Shape is not a prefab variant the player picks. It is read off the neighbour mask:
// a cell with two opposite neighbours is a straight, two adjacent neighbours a bend, three a
// junction, four a crossing, and none an isolated patch. What the mask actually changes here
// is the SHOULDER: an unconnected edge gets a raised aggregate kerb, a connected edge does
// not, so a run reads as one continuous paved strip rather than a row of butted slabs. Ramps
// need no case at all — the drape IS the ramp.
//
// Two submeshes on one mesh: [0] asphalt, [1] aggregate shoulder. Both are covered by the
// single MeshCollider, so the walkable surface and the visible surface can never disagree.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Building
{
    /// <summary>Which sides of a road cell continue into a neighbour. Flags, so a mask of
    /// North|South is a straight and North|East is a bend.</summary>
    [System.Flags]
    public enum RoadEdgeMask
    {
        None  = 0,
        North = 1,   // local +Z
        East  = 2,   // local +X
        South = 4,   // local -Z
        West  = 8    // local -X
    }

    public static class AsphaltRoadMesh
    {
        /// <summary>Subdivisions per axis on the top surface. 3 gives a 4x4 vertex grid, which is
        /// enough to drape a gully without costing anything measurable on a highway. Public so a
        /// caller can size its height cache to match the grid this file builds and the two can
        /// never drift apart.</summary>
        public const int SUBDIVISIONS = 3;

        /// <summary>Samples a ground height in LOCAL space: x/z are metres from the cell centre,
        /// the return is metres above the cell origin. Supplied by the block so this file stays
        /// free of physics and voxel queries and is trivially testable.</summary>
        public delegate float HeightSampler(float localX, float localZ);

        // ════════════════════════════════════════════════════════════════
        //  SURFACE
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds (or rebuilds) the draped slab for one road cell.
        /// </summary>
        /// <param name="target">Mesh to write into. Created by the caller and owned by it.</param>
        /// <param name="cellSize">Cell edge in metres (1 for a standard road cell).</param>
        /// <param name="thickness">Slab thickness in metres.</param>
        /// <param name="mask">Connected edges — the complement gets an aggregate shoulder.</param>
        /// <param name="sampleHeight">Ground height sampler in local space.</param>
        /// <param name="shoulderWidth">How far the raised aggregate kerb reaches in from an
        /// unconnected edge.</param>
        /// <param name="shoulderRise">How proud of the asphalt the kerb stands.</param>
        public static void BuildSurface(Mesh target, float cellSize, float thickness, RoadEdgeMask mask,
                                        HeightSampler sampleHeight, float shoulderWidth = 0.09f,
                                        float shoulderRise = 0.022f)
        {
            if (target == null) return;
            float half = cellSize * 0.5f;
            var sampler = sampleHeight ?? ((x, z) => 0f);

            var positions = new List<Vector3>((SUBDIVISIONS + 1) * (SUBDIVISIONS + 1) + 64);
            var normals   = new List<Vector3>(positions.Capacity);
            var uvs       = new List<Vector2>(positions.Capacity);
            var asphalt   = new List<int>(384);
            var shoulder  = new List<int>(96);

            // ── 1) Draped top surface ────────────────────────────────────
            // Heights are sampled once into a grid so the shoulder pass and the skirt pass
            // read exactly the same numbers the surface was built from.
            int vertsPerAxis = SUBDIVISIONS + 1;
            var topHeights = new float[vertsPerAxis * vertsPerAxis];
            int firstTop = positions.Count;

            for (int iz = 0; iz < vertsPerAxis; iz++)
            {
                float z = Mathf.Lerp(-half, half, iz / (float)SUBDIVISIONS);
                for (int ix = 0; ix < vertsPerAxis; ix++)
                {
                    float x = Mathf.Lerp(-half, half, ix / (float)SUBDIVISIONS);
                    float ground = sampler(x, z);
                    topHeights[iz * vertsPerAxis + ix] = ground;

                    positions.Add(new Vector3(x, ground + thickness, z));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(x, z));   // 1 UV unit per metre: the speckle tiles true to scale
                }
            }

            for (int iz = 0; iz < SUBDIVISIONS; iz++)
            for (int ix = 0; ix < SUBDIVISIONS; ix++)
            {
                int a = firstTop + iz * vertsPerAxis + ix;
                int b = a + 1;
                int c = a + vertsPerAxis;
                int d = c + 1;
                // Two triangles per quad, wound for +Y.
                asphalt.Add(a); asphalt.Add(c); asphalt.Add(b);
                asphalt.Add(b); asphalt.Add(c); asphalt.Add(d);
            }

            // Smooth the top normals from the actual drape so a slope shades as a slope.
            RecomputeNormals(positions, asphalt, normals, firstTop, vertsPerAxis);

            // ── 2) Aggregate shoulder on every unconnected edge ──────────
            AddShoulder(positions, normals, uvs, shoulder, sampler, half, thickness,
                        RoadEdgeMask.North, (mask & RoadEdgeMask.North) != 0, shoulderWidth, shoulderRise);
            AddShoulder(positions, normals, uvs, shoulder, sampler, half, thickness,
                        RoadEdgeMask.East,  (mask & RoadEdgeMask.East)  != 0, shoulderWidth, shoulderRise);
            AddShoulder(positions, normals, uvs, shoulder, sampler, half, thickness,
                        RoadEdgeMask.South, (mask & RoadEdgeMask.South) != 0, shoulderWidth, shoulderRise);
            AddShoulder(positions, normals, uvs, shoulder, sampler, half, thickness,
                        RoadEdgeMask.West,  (mask & RoadEdgeMask.West)  != 0, shoulderWidth, shoulderRise);

            // ── 3) Skirt and base ────────────────────────────────────────
            // The skirt hangs below the LOWEST ground sample in the cell so a draped slab can
            // never show daylight underneath itself on uneven ground.
            float lowest = float.MaxValue;
            for (int i = 0; i < topHeights.Length; i++)
                if (topHeights[i] < lowest) lowest = topHeights[i];
            float baseY = lowest - Mathf.Max(thickness, 0.22f);

            AddSkirtAndBase(positions, normals, uvs, asphalt, sampler, half, thickness, baseY);

            target.Clear();
            target.indexFormat = positions.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            target.SetVertices(positions);
            target.SetNormals(normals);
            target.SetUVs(0, uvs);
            target.subMeshCount = 2;
            target.SetTriangles(asphalt, 0);
            target.SetTriangles(shoulder, 1);
            target.RecalculateBounds();
            target.RecalculateTangents();
        }

        private static void RecomputeNormals(List<Vector3> positions, List<int> triangles,
                                             List<Vector3> normals, int firstTop, int vertsPerAxis)
        {
            // Averaged per-vertex normals over the top grid only: the skirt and shoulder keep
            // their own flat normals, and smoothing across those seams would round the kerb off.
            int count = vertsPerAxis * vertsPerAxis;
            for (int i = 0; i < count; i++) normals[firstTop + i] = Vector3.zero;

            for (int t = 0; t < triangles.Count; t += 3)
            {
                int ia = triangles[t], ib = triangles[t + 1], ic = triangles[t + 2];
                if (ia < firstTop || ib < firstTop || ic < firstTop) continue;
                if (ia >= firstTop + count || ib >= firstTop + count || ic >= firstTop + count) continue;
                Vector3 faceNormal = Vector3.Cross(
                    positions[ib] - positions[ia],
                    positions[ic] - positions[ia]).normalized;
                normals[ia] += faceNormal;
                normals[ib] += faceNormal;
                normals[ic] += faceNormal;
            }

            for (int i = 0; i < count; i++)
            {
                int idx = firstTop + i;
                normals[idx] = normals[idx].sqrMagnitude > 0.0001f ? normals[idx].normalized : Vector3.up;
            }
        }

        private static void AddShoulder(List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs,
                                        List<int> triangles, HeightSampler sampler, float half, float thickness,
                                        RoadEdgeMask edge, bool connected, float width, float rise)
        {
            if (connected || width <= 0.001f) return;

            // Build the kerb as a strip inset from the edge: outer vertices on the cell border
            // at the asphalt level, inner vertices raised by `rise`, so the shoulder reads as a
            // low aggregate lip rather than a wall.
            Vector3 up = Vector3.up;
            for (int i = 0; i < SUBDIVISIONS; i++)
            {
                float t0 = Mathf.Lerp(-half, half, i / (float)SUBDIVISIONS);
                float t1 = Mathf.Lerp(-half, half, (i + 1) / (float)SUBDIVISIONS);

                GetEdgeQuad(edge, t0, t1, half, width,
                    out Vector3 outerA, out Vector3 outerB, out Vector3 innerA, out Vector3 innerB);

                float hOuterA = sampler(outerA.x, outerA.z);
                float hOuterB = sampler(outerB.x, outerB.z);
                float hInnerA = sampler(innerA.x, innerA.z);
                float hInnerB = sampler(innerB.x, innerB.z);

                int baseIndex = positions.Count;
                positions.Add(new Vector3(outerA.x, hOuterA + thickness, outerA.z));
                positions.Add(new Vector3(outerB.x, hOuterB + thickness, outerB.z));
                positions.Add(new Vector3(innerA.x, hInnerA + thickness + rise, innerA.z));
                positions.Add(new Vector3(innerB.x, hInnerB + thickness + rise, innerB.z));

                for (int v = 0; v < 4; v++) normals.Add(up);
                uvs.Add(new Vector2(outerA.x, outerA.z));
                uvs.Add(new Vector2(outerB.x, outerB.z));
                uvs.Add(new Vector2(innerA.x, innerA.z));
                uvs.Add(new Vector2(innerB.x, innerB.z));

                triangles.Add(baseIndex);     triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 3);
            }
        }

        private static void GetEdgeQuad(RoadEdgeMask edge, float t0, float t1, float half, float width,
                                        out Vector3 outerA, out Vector3 outerB, out Vector3 innerA, out Vector3 innerB)
        {
            switch (edge)
            {
                case RoadEdgeMask.North:
                    outerA = new Vector3(t0, 0f,  half); outerB = new Vector3(t1, 0f,  half);
                    innerA = new Vector3(t0, 0f,  half - width); innerB = new Vector3(t1, 0f, half - width);
                    break;
                case RoadEdgeMask.South:
                    outerA = new Vector3(t1, 0f, -half); outerB = new Vector3(t0, 0f, -half);
                    innerA = new Vector3(t1, 0f, -half + width); innerB = new Vector3(t0, 0f, -half + width);
                    break;
                case RoadEdgeMask.East:
                    outerA = new Vector3(half, 0f, t1); outerB = new Vector3(half, 0f, t0);
                    innerA = new Vector3(half - width, 0f, t1); innerB = new Vector3(half - width, 0f, t0);
                    break;
                default: // West
                    outerA = new Vector3(-half, 0f, t0); outerB = new Vector3(-half, 0f, t1);
                    innerA = new Vector3(-half + width, 0f, t0); innerB = new Vector3(-half + width, 0f, t1);
                    break;
            }
        }

        private static void AddSkirtAndBase(List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs,
                                            List<int> triangles, HeightSampler sampler, float half,
                                            float thickness, float baseY)
        {
            // Four walls plus a flat underside. Sampled at the cell corners so the skirt follows
            // the same drape as the top and never leaves a slot of daylight on a slope.
            var corners = new[]
            {
                new Vector3(-half, 0f, -half), new Vector3(half, 0f, -half),
                new Vector3(half, 0f, half),   new Vector3(-half, 0f, half)
            };
            var sideNormals = new[] { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };

            int ringStart = positions.Count;
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = corners[i];
                float top = sampler(c.x, c.z) + thickness;
                positions.Add(new Vector3(c.x, top, c.z));
                normals.Add(sideNormals[i]);
                uvs.Add(new Vector2(c.x + c.z, top));
            }
            int baseStart = positions.Count;
            for (int i = 0; i < 4; i++)
            {
                Vector3 c = corners[i];
                positions.Add(new Vector3(c.x, baseY, c.z));
                normals.Add(sideNormals[i]);
                uvs.Add(new Vector2(c.x + c.z, baseY));
            }

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                int t0 = ringStart + i, t1 = ringStart + next;
                int b0 = baseStart + i, b1 = baseStart + next;
                triangles.Add(t0); triangles.Add(b0); triangles.Add(t1);
                triangles.Add(t1); triangles.Add(b0); triangles.Add(b1);
            }

            // Underside, wound for -Y. Nobody sees it, but an open shell leaks light and breaks
            // the MeshCollider's watertightness on thin slabs.
            int u0 = baseStart;
            triangles.Add(u0); triangles.Add(u0 + 2); triangles.Add(u0 + 1);
            triangles.Add(u0); triangles.Add(u0 + 3); triangles.Add(u0 + 2);
            for (int i = 0; i < 4; i++) normals[baseStart + i] = Vector3.down;
        }

        // ════════════════════════════════════════════════════════════════
        //  WEAR DECALS
        // ════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds the pothole patch mesh. Deterministic per cell: the seed comes from the cell's
        /// world position, so a highway shows varied but STABLE damage — patches that jumped
        /// around every rebuild would read as a rendering bug.
        /// </summary>
        /// <summary>
        /// Builds the damage a worn run shows: a FRACTURE NETWORK in submesh 0 and POTHOLES in
        /// submesh 1. Both are real geometry draped on the same ground samples the surface uses,
        /// so a crack follows the slab down a ramp instead of floating above it.
        ///
        /// Why geometry rather than a decal or a texture swap: a decal cannot show a pothole as a
        /// HOLE, and a texture swap cannot show the broken rim where the surface tore away. The
        /// failure the design wants the player to read at a glance — hairline, then mapped, then
        /// torn, then gone — is a change in the silhouette, so it has to be in the mesh.
        ///
        /// <paramref name="wear01"/> drives density, width and depth. The caller rebuilds on a
        /// quantised step, not per frame, so this is allowed to be a little expensive.
        /// </summary>
        public static void BuildWearPatches(Mesh target, float cellSize, float thickness,
                                            HeightSampler sampleHeight, int seed, float wear01)
        {
            if (target == null) return;
            float half = cellSize * 0.5f;
            float wear = Mathf.Clamp01(wear01);
            var sampler = sampleHeight ?? ((x, z) => 0f);
            var random = new System.Random(seed);

            var crackPos  = new List<Vector3>(512);
            var crackNrm  = new List<Vector3>(512);
            var crackUv   = new List<Vector2>(512);
            var crackTri  = new List<int>(768);

            var holePos   = new List<Vector3>(128);
            var holeNrm   = new List<Vector3>(128);
            var holeUv    = new List<Vector2>(128);
            var holeTri   = new List<int>(192);

            BuildCracks(crackPos, crackNrm, crackUv, crackTri, sampler, random,
                        half, thickness, wear, cellSize);
            BuildPotholes(holePos, holeNrm, holeUv, holeTri, sampler, random,
                          half, thickness, wear, cellSize);

            target.Clear();

            // Both submeshes must exist even when empty: the renderer carries two material slots
            // and a missing submesh would leave the second slot drawing whatever came before.
            if (crackPos.Count >= 3 && crackTri.Count >= 3)
            {
                target.subMeshCount = 1;
                target.SetVertices(crackPos);
                target.SetNormals(crackNrm);
                target.SetUVs(0, crackUv);
                target.SetTriangles(crackTri, 0);
            }
            if (holePos.Count >= 3 && holeTri.Count >= 3)
            {
                int firstVert = crackPos.Count;
                var allPos = new List<Vector3>(crackPos); allPos.AddRange(holePos);
                var allNrm = new List<Vector3>(crackNrm); allNrm.AddRange(holeNrm);
                var allUv  = new List<Vector2>(crackUv);  allUv.AddRange(holeUv);

                var shifted = new List<int>(holeTri.Count);
                for (int i = 0; i < holeTri.Count; i++) shifted.Add(holeTri[i] + firstVert);

                target.subMeshCount = 2;
                target.SetVertices(allPos);
                target.SetNormals(allNrm);
                target.SetUVs(0, allUv);
                target.SetTriangles(crackTri.Count >= 3 ? crackTri : new List<int>(), 0);
                target.SetTriangles(shifted, 1);
            }
            else if (crackPos.Count >= 3)
            {
                target.subMeshCount = 2;
                target.SetVertices(crackPos);
                target.SetNormals(crackNrm);
                target.SetUVs(0, crackUv);
                target.SetTriangles(crackTri, 0);
                target.SetTriangles(new List<int>(), 1);
            }
            else
            {
                target.subMeshCount = 2;
                target.SetVertices(new List<Vector3>());
                target.SetTriangles(new List<int>(), 0);
                target.SetTriangles(new List<int>(), 1);
            }

            target.RecalculateBounds();
        }

        /// <summary>
        /// The fracture network. Cracks start at the cell edges as often as they start inside it,
        /// because that is where a real slab fails first — the joint is the weak line. Each crack is
        /// a random walk whose heading wanders, emitted as a tapered quad strip so it reads as a
        /// jagged fissure and not as a stripe. Branches spawn off the walk, and both count and width
        /// grow with wear: a few hairlines at WORN, a full map of open cracks by BROKEN UP.
        /// </summary>
        private static void BuildCracks(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv,
                                        List<int> tri, HeightSampler sampler, System.Random random,
                                        float half, float thickness, float wear, float cellSize)
        {
            if (wear <= 0.02f) return;

            // Density scales with AREA, not with cell count: a 4 m cell holds sixteen times the
            // pavement of a 1 m cell, so it needs sixteen times the cracking to look equally worn.
            float areaUnits = Mathf.Max(1f, (cellSize * cellSize));
            int crackCount = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(2f, 7f, wear) * areaUnits * 0.55f), 2, 90);

            // A hairline is ~1 cm; a torn crack is ~7 cm across. Independent of cell size, because
            // a crack is a real-world feature — a bigger slab does not get fatter cracks, it gets
            // more of them.
            float widthAt = Mathf.Lerp(0.012f, 0.075f, wear);
            float top = thickness + 0.004f;

            for (int c = 0; c < crackCount; c++)
            {
                float startX, startZ, heading;
                if (random.Next(2) == 0)
                {
                    // Edge-initiated: start on a joint and walk inward, as a slab actually splits.
                    int edge = random.Next(4);
                    float along = ((float)random.NextDouble() * 2f - 1f) * half * 0.92f;
                    switch (edge)
                    {
                        case 0:  startX = -half * 0.96f; startZ = along; heading = 0f; break;
                        case 1:  startX =  half * 0.96f; startZ = along; heading = Mathf.PI; break;
                        case 2:  startX = along; startZ = -half * 0.96f; heading = Mathf.PI * 0.5f; break;
                        default: startX = along; startZ =  half * 0.96f; heading = -Mathf.PI * 0.5f; break;
                    }
                }
                else
                {
                    startX = ((float)random.NextDouble() * 2f - 1f) * half * 0.8f;
                    startZ = ((float)random.NextDouble() * 2f - 1f) * half * 0.8f;
                    heading = (float)random.NextDouble() * Mathf.PI * 2f;
                }

                float reach = cellSize * Mathf.Lerp(0.22f, 0.70f, wear)
                                       * (0.55f + (float)random.NextDouble() * 0.9f);
                EmitCrackWalk(pos, nrm, uv, tri, sampler, random,
                              startX, startZ, heading, reach, widthAt, top, half);

                // One branch in three, more often as the surface fails, so a worn cell grows a
                // connected map instead of a set of unrelated scratches.
                if (random.NextDouble() < 0.20 + wear * 0.55)
                {
                    float t = 0.35f + (float)random.NextDouble() * 0.45f;
                    float bx = startX + Mathf.Cos(heading) * reach * t;
                    float bz = startZ + Mathf.Sin(heading) * reach * t;
                    float branchHeading = heading + (random.NextDouble() < 0.5 ? -1f : 1f)
                                                * (0.6f + (float)random.NextDouble() * 0.9f);
                    EmitCrackWalk(pos, nrm, uv, tri, sampler, random,
                                  bx, bz, branchHeading, reach * 0.55f, widthAt * 0.65f, top, half);
                }
            }
        }

        /// <summary>Walks one fissure and emits it as a tapered, wandering quad strip.</summary>
        private static void EmitCrackWalk(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv,
                                          List<int> tri, HeightSampler sampler, System.Random random,
                                          float startX, float startZ, float heading, float reach,
                                          float width, float top, float half)
        {
            float step = Mathf.Max(0.05f, reach / 9f);
            int steps = Mathf.Clamp(Mathf.RoundToInt(reach / step), 2, 26);

            float x = startX, z = startZ;
            int firstVertex = pos.Count;
            int emitted = 0;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                // Taper: narrow at the tip, widest in the middle, closing again at the end. This is
                // what stops a crack looking like a drawn line with two parallel edges.
                float taper = Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                float w = Mathf.Max(0.004f, width * (0.30f + 0.70f * taper));

                float px = Mathf.Clamp(x, -half * 0.98f, half * 0.98f);
                float pz = Mathf.Clamp(z, -half * 0.98f, half * 0.98f);

                float perpX = -Mathf.Sin(heading);
                float perpZ =  Mathf.Cos(heading);
                float ax = px + perpX * w * 0.5f;
                float az = pz + perpZ * w * 0.5f;
                float bx = px - perpX * w * 0.5f;
                float bz = pz - perpZ * w * 0.5f;

                pos.Add(new Vector3(ax, sampler(ax, az) + top, az));
                nrm.Add(Vector3.up);
                uv.Add(new Vector2(ax, az));

                pos.Add(new Vector3(bx, sampler(bx, bz) + top, bz));
                nrm.Add(Vector3.up);
                uv.Add(new Vector2(bx, bz));

                if (emitted > 0)
                {
                    int v = firstVertex + (emitted - 1) * 2;
                    tri.Add(v); tri.Add(v + 1); tri.Add(v + 2);
                    tri.Add(v + 1); tri.Add(v + 3); tri.Add(v + 2);
                }
                emitted++;

                // Wander the heading and the centreline, so the fissure kinks like broken slab.
                heading += ((float)random.NextDouble() * 2f - 1f) * 0.55f;
                x += Mathf.Cos(heading) * step;
                z += Mathf.Sin(heading) * step;
            }
        }

        /// <summary>
        /// Potholes: the surface has torn through and the base course shows. Only appears past the
        /// POTHOLED threshold. Each hole is a depressed, jagged floor plus a wall of broken rim
        /// quads connecting it back to the intact surface — which is the part that sells it, because
        /// a hole with no rim reads as a dark sticker.
        /// </summary>
        private static void BuildPotholes(List<Vector3> pos, List<Vector3> nrm, List<Vector2> uv,
                                          List<int> tri, HeightSampler sampler, System.Random random,
                                          float half, float thickness, float wear, float cellSize)
        {
            const float ONSET = 0.62f;
            if (wear <= ONSET) return;

            float severity = Mathf.InverseLerp(ONSET, 1f, wear);
            float areaUnits = Mathf.Max(1f, cellSize * cellSize);
            int holes = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 3f, severity) * areaUnits * 0.35f), 1, 30);

            for (int h = 0; h < holes; h++)
            {
                float cx = ((float)random.NextDouble() * 2f - 1f) * half * 0.66f;
                float cz = ((float)random.NextDouble() * 2f - 1f) * half * 0.66f;
                float radius = cellSize * Mathf.Lerp(0.055f, 0.155f, severity)
                                        * (0.7f + (float)random.NextDouble() * 0.6f);
                // Never punch through the slab: a pothole is a tear in the wearing course, not a
                // core drill, and the base under it still has to be there to be seen.
                float depth = thickness * Mathf.Lerp(0.45f, 0.88f, severity);

                int spokes = 11;
                float spin = (float)random.NextDouble() * Mathf.PI * 2f;

                int rimStart = pos.Count;
                var rimOuter = new float[spokes];
                var rimInner = new float[spokes];

                for (int s = 0; s < spokes; s++)
                {
                    float angle = spin + s / (float)spokes * Mathf.PI * 2f;
                    // Jagged rim: a pothole edge is broken, not turned, so the radius varies hard.
                    float jag = 0.62f + (float)random.NextDouble() * 0.72f;
                    rimOuter[s] = radius * jag;
                    rimInner[s] = rimOuter[s] * (0.82f + (float)random.NextDouble() * 0.2f);

                    float ox = cx + Mathf.Cos(angle) * rimOuter[s];
                    float oz = cz + Mathf.Sin(angle) * rimOuter[s];
                    ox = Mathf.Clamp(ox, -half * 0.96f, half * 0.96f);
                    oz = Mathf.Clamp(oz, -half * 0.96f, half * 0.96f);

                    float ix = cx + Mathf.Cos(angle) * rimInner[s];
                    float iz = cz + Mathf.Sin(angle) * rimInner[s];
                    ix = Mathf.Clamp(ix, -half * 0.96f, half * 0.96f);
                    iz = Mathf.Clamp(iz, -half * 0.96f, half * 0.96f);

                    // Outer rim vertex sits on the intact surface, inner one at the torn floor.
                    pos.Add(new Vector3(ox, sampler(ox, oz) + thickness + 0.003f, oz));
                    nrm.Add(Vector3.up);
                    uv.Add(new Vector2(ox, oz));

                    pos.Add(new Vector3(ix, sampler(ix, iz) + thickness - depth, iz));
                    nrm.Add(Vector3.up);
                    uv.Add(new Vector2(ix * 2.5f, iz * 2.5f));
                }

                // Wall between consecutive rim pairs, plus the floor fan the walls enclose.
                int floorCentre = pos.Count;
                float fx = cx, fz = cz;
                pos.Add(new Vector3(fx, sampler(fx, fz) + thickness - depth, fz));
                nrm.Add(Vector3.up);
                uv.Add(new Vector2(fx * 2.5f, fz * 2.5f));

                for (int s = 0; s < spokes; s++)
                {
                    int n = (s + 1) % spokes;
                    int o0 = rimStart + s * 2;      // outer, this spoke
                    int i0 = rimStart + s * 2 + 1;  // inner, this spoke
                    int o1 = rimStart + n * 2;      // outer, next spoke
                    int i1 = rimStart + n * 2 + 1;  // inner, next spoke

                    // broken rim wall
                    tri.Add(o0); tri.Add(i0); tri.Add(o1);
                    tri.Add(i0); tri.Add(i1); tri.Add(o1);
                    // floor fan
                    tri.Add(floorCentre); tri.Add(i1); tri.Add(i0);
                }
            }
        }

        /// <summary>Stable per-cell seed from a world position, so patches survive a rebuild.</summary>
        public static int CellSeed(Vector3 worldPosition)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(worldPosition.x * 4f);
                hash = hash * 31 + Mathf.RoundToInt(worldPosition.y * 4f);
                hash = hash * 31 + Mathf.RoundToInt(worldPosition.z * 4f);
                return hash;
            }
        }
    }
}
