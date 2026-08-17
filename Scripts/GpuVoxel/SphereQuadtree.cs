// Assets/Scripts/VoxelEngine/GpuVoxel/SphereQuadtree.cs
//
// The SPHERIFIED QUADTREE — six cube faces, each divided into a quadtree of
// curved shell nodes (9.0.0). As the viewer approaches the surface a node
// splits into four children at double resolution; receding merges them back.
//
// The desired-leaf computation runs as a Burst job on worker threads
// (Unity Job System) — the main thread only schedules it and harvests the
// result list, so streaming decisions never block the game loop.
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GpuVoxel
{
    /// <summary>Unique address of a quadtree node: cube face, depth, tile coords.</summary>
    public struct QuadNodeId : IEquatable<QuadNodeId>
    {
        public int face;    // 0..5
        public int depth;   // 0 = whole face
        public int x;       // 0 .. 2^depth − 1
        public int y;

        public QuadNodeId(int face, int depth, int x, int y)
        { this.face = face; this.depth = depth; this.x = x; this.y = y; }

        public QuadNodeId Parent => new QuadNodeId(face, math.max(0, depth - 1), x >> 1, y >> 1);
        public QuadNodeId Child(int cx, int cy) => new QuadNodeId(face, depth + 1, x * 2 + cx, y * 2 + cy);

        public bool Equals(QuadNodeId o) => face == o.face && depth == o.depth && x == o.x && y == o.y;
        public override bool Equals(object o) => o is QuadNodeId id && Equals(id);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = face;
                h = h * 397 ^ depth;
                h = h * 397 ^ x;
                h = h * 397 ^ y;
                return h;
            }
        }
        public override string ToString() => $"F{face} D{depth} ({x},{y})";
    }

    /// <summary>
    /// Everything the streaming pipeline needs to build one node: uv window,
    /// radial band hugging the terrain, and precomputed culling data.
    /// Blittable — produced inside the Burst descent job.
    /// </summary>
    public struct QuadNodeDesc
    {
        public QuadNodeId id;
        public float2 uvMin;        // face-space uv of footprint corner (c = 0)
        public float2 uvSize;       // face-space uv extent of the 64-cell footprint
        public float rLo;           // radius (m) at radial corner c = 0
        public float rHi;           // radius (m) at radial corner c = 64
        public float minSurface;    // sampled min surface radius over the tile
        public float maxSurface;    // sampled max surface radius over the tile
        public float3 centerDir;    // unit direction of the tile centre
        public float arc;           // footprint edge length (m) on the surface
        public float distance;      // viewer distance (m) at build time — priority key

        public float CellArc => arc / GpuVoxelConstants.NODE_CELLS;
        public float Dr => (rHi - rLo) / GpuVoxelConstants.NODE_CELLS;
        /// <summary>Mesh anchor (body-local) — vertices are emitted relative to it.</summary>
        public float3 Anchor => centerDir * (rLo + rHi) * 0.5f;
    }

    /// <summary>
    /// Burst job: descends the six face quadtrees around the viewer and emits
    /// the desired leaf set with terrain-hugging radial bands. ~1–2 k field
    /// samples per pass; runs entirely off the main thread.
    /// </summary>
    [BurstCompile(FloatPrecision.Standard, FloatMode.Fast, CompileSynchronously = false)]
    public struct BuildDesiredLeavesJob : IJob
    {
        public int   seed;
        public float radiusWorld;
        public float baseHeight;
        public float seaRadius;
        public float continentScale;
        public float mountainScale;

        public float3 viewerLocal;   // body-local viewer position
        public int    maxDepth;      // finest allowed depth
        public float  splitFactor;   // split while viewerDist < splitFactor × arc
        public int    maxLeaves;     // hard safety cap

        public NativeList<QuadNodeDesc> results;

        private struct StackEntry { public QuadNodeId id; }

        public void Execute()
        {
            results.Clear();
            var stack = new NativeList<StackEntry>(512, Allocator.Temp);
            for (int f = 0; f < 6; f++)
                stack.Add(new StackEntry { id = new QuadNodeId(f, 0, 0, 0) });

            while (stack.Length > 0 && results.Length < maxLeaves)
            {
                var e = stack[stack.Length - 1];
                stack.RemoveAt(stack.Length - 1);

                QuadNodeDesc desc = BuildDesc(e.id);

                bool split = e.id.depth < maxDepth && desc.distance < splitFactor * desc.arc;
                if (split)
                {
                    stack.Add(new StackEntry { id = e.id.Child(0, 0) });
                    stack.Add(new StackEntry { id = e.id.Child(1, 0) });
                    stack.Add(new StackEntry { id = e.id.Child(0, 1) });
                    stack.Add(new StackEntry { id = e.id.Child(1, 1) });
                }
                else
                {
                    results.Add(desc);
                }
            }
            stack.Dispose();
        }

        private float Surface(in float3 dir) =>
            PlanetField.SurfaceRadius(seed, dir, radiusWorld, baseHeight, seaRadius,
                                      continentScale, mountainScale);

        private QuadNodeDesc BuildDesc(in QuadNodeId id)
        {
            int n = 1 << id.depth;
            float tileUv = 2f / n;
            float2 uvMin = new float2(-1f + id.x * tileUv, -1f + id.y * tileUv);
            float2 uvSize = new float2(tileUv, tileUv);

            // 9-point surface probe: corners, edge midpoints, centre.
            float minS = float.MaxValue, maxS = float.MinValue;
            float3 centerDir = default;
            for (int j = 0; j <= 2; j++)
            for (int i = 0; i <= 2; i++)
            {
                float u = uvMin.x + uvSize.x * (i * 0.5f);
                float v = uvMin.y + uvSize.y * (j * 0.5f);
                float3 d = CubeSphere.FaceDirection(id.face, u, v);
                if (i == 1 && j == 1) centerDir = d;
                float s = Surface(d);
                minS = math.min(minS, s);
                maxS = math.max(maxS, s);
            }

            float arc = (math.PI * 0.5f) * radiusWorld / n;   // footprint edge (m)
            float cellArc = arc / GpuVoxelConstants.NODE_CELLS;

            // Radial band: sampled min/max padded against peaks between probes.
            float pad = 8f + 0.5f * (maxS - minS) + 2f * cellArc;
            pad = math.min(pad, 500f);

            // ── SHARED RADIAL LATTICE (9.2.0 gap fix) ─────────────────────────
            // Neighbouring nodes at the same depth previously used free-floating
            // radial bands, so their ghost-cell corner radii did not align — the
            // watertight stitching broke and cracks/gaps opened along node borders
            // wherever relief differed. The band is now quantised onto a per-depth
            // lattice: dr is a power-of-two multiple of the cell arc (identical for
            // every same-depth node) and rLo snaps to a multiple of dr, so shared
            // boundary corners sample the field at IDENTICAL positions on both
            // sides — bit-identical vertices, no cracks. dr doubles only over
            // extreme relief (rare; skirts cover those transitions).
            float span = (maxS - minS) + 2f * pad;
            float dr = cellArc;
            while (span > 63.5f * dr) dr *= 2f;
            float rLo = math.floor((minS - pad) / dr) * dr;
            float coreFloor = radiusWorld * 0.4f;
            if (rLo < coreFloor) rLo = math.floor(coreFloor / dr) * dr;
            float rHi = rLo + GpuVoxelConstants.NODE_CELLS * dr;

            // Viewer distance to the tile's surface shell (footprint-compensated).
            float viewerR = math.length(viewerLocal);
            float surfC = math.clamp(viewerR, rLo, rHi);
            float dist = math.length(viewerLocal - centerDir * surfC) - 0.75f * arc;
            dist = math.max(0f, dist);

            return new QuadNodeDesc
            {
                id = id,
                uvMin = uvMin,
                uvSize = uvSize,
                rLo = rLo,
                rHi = rHi,
                minSurface = minS,
                maxSurface = maxS,
                centerDir = centerDir,
                arc = arc,
                distance = dist
            };
        }
    }
}
