// Assets/Scripts/VoxelEngine/Building/RoadPaver.cs
//
// THE PAVE GESTURE — point to point, and the placement maths both the gesture and the ordinary
// block-by-block path share.
//
// Two jobs, one file, because they must never disagree:
//
//   1. STATIC PLACEMENT. Where a road cell goes, whether it may go there, and what it costs. The
//      ghost, the planner and a hand-placed road block all resolve through `TryComputePose` +
//      `EvaluateCell`, so a cell the ghost shows as valid is a cell the commit accepts — the same
//      "cannot drift apart" discipline the route book and the autopilot evaluator use.
//   2. THE PLAN. One click sets the start, the next lays the corridor between. Width is Ctrl+scroll
//      and counts cells of the selected surface, RMB lifts one cell and Ctrl+RMB lifts the whole
//      run. See THE PLAN below for why this replaced a held drag.
//
// LATTICE. A run must be continuous, and on a spherical world rounding world axes drifts between
// neighbours (the same drift `BuildSystem` warns about for machines). So a new cell anchors to an
// existing road when one is in reach: it inherits that road's frame and quantises its offset in
// THAT frame, which is what makes a planned corridor line up cell for cell around a planet — and
// what makes starting a plan ON an existing road extend it instead of laying a parallel strip a few
// centimetres off. With no neighbour to anchor to, the first cell quantises on the local tangent
// frame, and the whole corridor runs in that one frame so it cannot fan out over its length.
//
// HEIGHT is never inherited — it is re-probed from the ground under the resolved lateral position,
// ignoring road colliders. That is what lets a strip climb a terrace as a ramp while staying
// perfectly aligned, and what stops a cell laid on top of an existing road from stacking 8 cm high
// every time.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Cosmos;
using VoxelEngine.Environment;
using VoxelEngine.Items;

namespace VoxelEngine.Building
{
    public sealed class RoadPaver
    {
        // ════════════════════════════════════════════════════════════════
        //  STATIC PLACEMENT — shared by the ghost, the planner and the hand path
        // ════════════════════════════════════════════════════════════════

        /// <summary>True when a block item lays asphalt. Used by `BuildSystem` to route a held road
        /// block through the road snap instead of the generic machine snap.</summary>
        public static bool IsRoadBlock(BlockItem block)
        {
            if (block == null || block.placedPrefab == null) return false;
            return block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true) != null;
        }

        private static readonly List<AsphaltRoad> _anchorScratch = new List<AsphaltRoad>(8);
        /// <summary>Separate from `_anchorScratch` on purpose: `IsCellOccupied` and
        /// `TryComputePose` both read through that one, and a shared buffer would be clobbered
        /// the moment a caller nested them.</summary>
        private static readonly List<AsphaltRoad> _roadAtScratch = new List<AsphaltRoad>(8);

        /// <summary>
        /// Resolves where a road cell goes for a given aim. Returns false when there is nothing to
        /// lay on (sky, water with no bed, out of reach).
        /// </summary>
        public static bool TryComputePose(RaycastHit hit, BlockItem block, out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = default;
            if (block == null || block.placedPrefab == null) return false;

            var template = block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true);
            float cell = template != null ? Mathf.Max(0.25f, template.cellSize) : 1f;

            // ── Anchor: an existing road cell in reach, or the collider we actually hit ──
            AsphaltRoad anchor = hit.collider != null ? hit.collider.GetComponentInParent<AsphaltRoad>() : null;
            if (anchor == null)
            {
                RoadSurfaceUtility.QueryAt(hit.point, _anchorScratch);
                float bestSqr = cell * cell * 3.2f;
                for (int i = 0; i < _anchorScratch.Count; i++)
                {
                    var candidate = _anchorScratch[i];
                    if (candidate == null) continue;
                    float sqr = (candidate.transform.position - hit.point).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; anchor = candidate; }
                }
            }

            if (anchor != null)
            {
                Transform a = anchor.transform;
                Vector3 delta = hit.point - a.position;
                float lx = Mathf.Round(Vector3.Dot(delta, a.right) / cell) * cell;
                float lz = Mathf.Round(Vector3.Dot(delta, a.forward) / cell) * cell;
                Vector3 lateral = a.position + a.right * lx + a.forward * lz;
                rotation = a.rotation;
                if (!AsphaltRoad.ProbeGround(lateral, a.up, out float groundOffset)) return false;
                position = lateral + a.up * groundOffset;
                return true;
            }

            // ── No anchor: quantise on the local tangent frame ──
            rotation = GravityProvider.GetSurfaceRotation(hit.point);
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float cx = Mathf.Round(Vector3.Dot(hit.point, right) / cell) * cell;
            float cz = Mathf.Round(Vector3.Dot(hit.point, forward) / cell) * cell;
            Vector3 flat = right * cx + forward * cz + up * Vector3.Dot(hit.point, up);
            if (!AsphaltRoad.ProbeGround(flat, up, out float offset)) return false;
            position = flat + up * offset;
            return true;
        }

        /// <summary>Grade and volume verdict for a resolved cell, plus what it costs.</summary>
        public static AsphaltRoad.GradeBand EvaluateCell(BlockItem block, Vector3 position, Quaternion rotation,
                                                         out int materialCost)
            => JudgeCell(block, position, rotation, out materialCost, out _);

        /// <summary>
        /// The verdict every consumer shares, plus whether laying this cell shaves ground first.
        /// Ground that pokes up through the cell plane but stays inside the carve limit is NOT a
        /// refusal: the road trims it flush and prices what is left. Deeper than the limit still
        /// refuses, because paving shaves a bump — it does not dig a tunnel. This is what stops a
        /// one-voxel step in otherwise open ground from reading as "cannot pave inside a wall".
        /// </summary>
        public static AsphaltRoad.GradeBand JudgeCell(BlockItem block, Vector3 position, Quaternion rotation,
                                                      out int materialCost, out bool needsCarve)
        {
            needsCarve = false;
            materialCost = 1;
            var template = block != null && block.placedPrefab != null
                ? block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true)
                : null;

            float cell        = template != null ? template.cellSize            : 1f;
            float maxSmooth   = template != null ? template.maxGradeSmooth      : 0.22f;
            float maxRough    = template != null ? template.maxGradeRoughness   : 0.50f;

            var band = AsphaltRoad.EvaluateSite(position, rotation, cell, maxSmooth, maxRough, out _);
            if (band == AsphaltRoad.GradeBand.Buried || band == AsphaltRoad.GradeBand.TooRough)
            {
                float above = GroundAbovePlane(position, rotation, cell);
                if (above > CARVE_EPSILON && above <= CARVE_LIMIT)
                {
                    // Once the bump is shaved, everything above the plane IS the plane, so the slope
                    // left to judge is only how far the remaining ground falls below it.
                    float lowest = LowestGroundBelow(position, rotation, cell);
                    float slope = -lowest / Mathf.Max(0.25f, cell);
                    if (slope <= maxRough)
                    {
                        needsCarve = true;
                        band = slope > maxSmooth ? AsphaltRoad.GradeBand.Rough : AsphaltRoad.GradeBand.Smooth;
                    }
                }
            }

            if (band == AsphaltRoad.GradeBand.Rough) materialCost = 2;
            else if (band != AsphaltRoad.GradeBand.Smooth) materialCost = 0;
            return band;
        }

        // ════════════════════════════════════════════════════════════════
        //  CARVE — shaving a bump so the slab sits flush
        //
        //  A road drapes the terrain rather than flattening it, and that stays true: carving only
        //  trims ground that pokes UP THROUGH the cell plane, never digs below it, and never more
        //  than about a voxel of height. It is the difference between a slab that bulges over a
        //  stray step or refuses outright, and one that sits into the ground like it was laid there.
        // ════════════════════════════════════════════════════════════════

        /// <summary>Tallest ground the road will shave to sit flush, in metres. Roughly one voxel:
        /// enough that a stray step never refuses a line or bulges the slab, not enough that paving
        /// becomes free terraforming.</summary>
        public const float CARVE_LIMIT = 1.25f;
        private const float CARVE_EPSILON = 0.12f;

        /// <summary>Height above the cell plane of the tallest ground inside the footprint, 0 when
        /// nothing pokes through.</summary>
        private static float GroundAbovePlane(Vector3 position, Quaternion rotation, float cell)
        {
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float step = cell / 3f;
            float above = 0f;
            for (int gx = -1; gx <= 1; gx++)
            for (int gz = -1; gz <= 1; gz++)
            {
                Vector3 lateral = position + right * (gx * step) + forward * (gz * step);
                if (!AsphaltRoad.ProbeGround(lateral, up, out float h)) continue;
                if (h > above) above = h;
            }
            return above;
        }

        /// <summary>The deepest the remaining ground falls below the cell plane, as a negative
        /// number (0 when the footprint is level with the plane).</summary>
        private static float LowestGroundBelow(Vector3 position, Quaternion rotation, float cell)
        {
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float step = cell / 3f;
            float lowest = 0f;
            for (int gx = -1; gx <= 1; gx++)
            for (int gz = -1; gz <= 1; gz++)
            {
                Vector3 lateral = position + right * (gx * step) + forward * (gz * step);
                if (!AsphaltRoad.ProbeGround(lateral, up, out float h)) continue;
                float clamped = Mathf.Min(h, 0f);
                if (clamped < lowest) lowest = clamped;
            }
            return lowest;
        }

        /// <summary>Empties every solid voxel poking up through the cell plane, up to
        /// <see cref="CARVE_LIMIT"/>. Columns deeper than the limit are left alone — the verdict has
        /// already refused those.</summary>
        public static void CarveCell(Vector3 position, Quaternion rotation, float cell)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return;
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float step = cell / 3f;

            for (int gx = -1; gx <= 1; gx++)
            for (int gz = -1; gz <= 1; gz++)
            {
                Vector3 lateral = position + right * (gx * step) + forward * (gz * step);
                if (!AsphaltRoad.ProbeGround(lateral, up, out float h)) continue;
                if (h <= CARVE_EPSILON || h > CARVE_LIMIT) continue;

                Vector3Int bottom = world.WorldToVoxel(lateral + up * 0.05f);
                Vector3Int top    = world.WorldToVoxel(lateral + up * (h + 0.30f));
                int x0 = Mathf.Min(bottom.x, top.x), x1 = Mathf.Max(bottom.x, top.x);
                int y0 = Mathf.Min(bottom.y, top.y), y1 = Mathf.Max(bottom.y, top.y);
                int z0 = Mathf.Min(bottom.z, top.z), z1 = Mathf.Max(bottom.z, top.z);
                for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                {
                    var v = new Vector3Int(x, y, z);
                    if (world.GetVoxelWorld(v).IsSolid)
                        world.SetVoxelWorld(v, VoxelEngine.Core.Voxel.Empty, remesh: true);
                }
            }
        }

        // ════════════════════════════════════════════════════════════════
        //  GRADING — cutting and filling a station to one level at commit
        //
        //  The carve above trims what pokes through ONE cell's plane. Grading is the bigger
        //  gesture: when a whole station is too far out of level to take pavement at all, the
        //  paver cuts the high side and fills the low side to one level as part of laying the
        //  road — the player asked for a road, not for homework with a terrain tool. The terrain
        //  only moves at COMMIT, never during planning: the corridor is re-solved every frame
        //  while the ghost is up, and ground that shifted while the player was still drawing the
        //  line would be ground they never agreed to move.
        // ════════════════════════════════════════════════════════════════

        /// <summary>Resolves the level a refused station will be graded to, caching the answer for
        /// the rest of the solve. All the lanes of one station share one target, which is what
        /// keeps the graded rectangles butted together with no seam for the slabs to bridge.</summary>
        private bool TryGradeStation(RoadCorridorBuffers buf, Vector3 up, float cell, int k,
                                     out float target, out string refusal)
        {
            target = 0f;
            refusal = null;
            if (k < 0 || k >= _levelAt.Count) return false;

            // Cached from an earlier lane of the same station, or an earlier frame of the same
            // solve: NaN = never asked, negative infinity = asked and refused (with the reason),
            // anything else = the level the station grades to.
            if (!float.IsNaN(_levelAt[k]))
            {
                if (float.IsNegativeInfinity(_levelAt[k])) { refusal = _levelWhy[k]; return false; }
                target = _levelAt[k];
                return true;
            }

            float lo = float.MaxValue, hi = float.MinValue, sum = 0f;
            int samples = 0;
            bool blocked = false, missed = false;
            for (int w = 0; w < _width && !blocked && !missed; w++)
            {
                var frame = RoadCorridor.CellAt(buf, w, k);
                if (frame == null) continue;
                Vector3 fUp = frame.rotation * Vector3.up;
                Vector3 fRight = frame.rotation * Vector3.right;
                Vector3 fForward = frame.rotation * Vector3.forward;
                float step = cell / 3f;
                for (int gx = -1; gx <= 1 && !blocked && !missed; gx++)
                for (int gz = -1; gz <= 1 && !blocked && !missed; gz++)
                {
                    Vector3 lateral = frame.position + fRight * (gx * step) + fForward * (gz * step);
                    if (!GradeProbe(lateral, fUp, out float above, out bool hitBlock))
                    { missed = true; break; }
                    if (hitBlock) { blocked = true; break; }
                    float h = Vector3.Dot(lateral, up) + above;
                    if (h < lo) lo = h;
                    if (h > hi) hi = h;
                    sum += h;
                    samples++;
                }
            }

            string why = null;
            if (blocked)
                why = "Cannot grade through a building - lift it, or route the line around it";
            else if (missed || samples < 3)
                why = "No ground to grade onto under part of the line";
            else
            {
                float mean = sum / samples;
                if (hi - mean > LEVEL_CUT_MAX || mean - lo > LEVEL_FILL_MAX)
                    why = "The ground steps too far here - the paver grades up to "
                          + (int)LEVEL_CUT_MAX + " m of cut or fill";
                else
                {
                    _levelAt[k] = mean;
                    target = mean;
                    return true;
                }
            }
            _levelAt[k] = float.NegativeInfinity;
            _levelWhy[k] = why;
            refusal = why;
            return false;
        }

        private static readonly RaycastHit[] _gradeHits = new RaycastHit[8];

        /// <summary>Ground probe that also reports whether the ground it found is a placed block
        /// rather than terrain. Grading cuts and fills voxels, and a building is not voxels — a
        /// strip that "graded" through a wall would be a strip that paved over the player's
        /// factory. Road cells are excluded exactly as the ground probe excludes them.</summary>
        private static bool GradeProbe(Vector3 worldPoint, Vector3 up,
                                       out float heightAbovePoint, out bool hitPlacedBlock)
        {
            heightAbovePoint = 0f;
            hitPlacedBlock = false;
            int count = Physics.RaycastNonAlloc(worldPoint + up * 2.5f, -up, _gradeHits, 6.5f,
                                                ~0, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                var collider = _gradeHits[i].collider;
                if (collider == null) continue;
                if (collider.GetComponentInParent<AsphaltRoad>() != null) continue;
                float along = Vector3.Dot(worldPoint - _gradeHits[i].point, up);
                if (along < best) { best = along; nearest = i; }
            }
            if (nearest < 0) return false;
            heightAbovePoint = -best;
            hitPlacedBlock = _gradeHits[nearest].collider.GetComponentInParent<PlacedBlock>() != null;
            return true;
        }

        /// <summary>Whether ONE cell's footprint is within the grader's reach — the per-cell form of
        /// the station verdict, used by the live ghost so the leg only tints red where laying would
        /// actually refuse, and green where the paver is about to do the grading for the player.</summary>
        private static bool CellGradeable(Vector3 position, Quaternion rotation, Vector3 up, float cell)
        {
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float step = cell / 3f;
            float lo = float.MaxValue, hi = float.MinValue;
            int samples = 0;
            for (int gx = -1; gx <= 1; gx++)
            for (int gz = -1; gz <= 1; gz++)
            {
                Vector3 lateral = position + right * (gx * step) + forward * (gz * step);
                if (!GradeProbe(lateral, up, out float above, out bool hitBlock)) return false;
                if (hitBlock) return false;
                float h = Vector3.Dot(lateral, up) + above;
                if (h < lo) lo = h;
                if (h > hi) hi = h;
                samples++;
            }
            if (samples < 3) return false;
            return (hi - lo) * 0.5f <= LEVEL_CUT_MAX && (hi - lo) * 0.5f <= LEVEL_FILL_MAX;
        }

        /// <summary>Metres the water surface stands ABOVE <paramref name="point"/>, measured along
        /// <paramref name="up"/>. False when the column is dry. Water in this world comes in two
        /// dialects and both are read here: the voxels the fluid sim fills — lakes, ponds, and the
        /// ocean wherever it is per-voxel water — and, on the sphere world, the planet's ocean
        /// shell at the sea radius, which is not voxel water everywhere and is invisible to a pure
        /// voxel scan. A deck over that ocean sampled the voxels, found nothing, and followed the
        /// shore line into the sea; the shell is the second opinion.</summary>
        private static bool WaterSurfaceAbove(Vector3 point, Vector3 up, out float surfaceAbovePoint)
        {
            surfaceAbovePoint = 0f;
            bool sphere = VoxelEngine.Core.ActiveWorld.Current is VoxelEngine.Cosmos.SphereWorld;

            // 1) Voxel water. `GetSurfaceHeight` returns the surface's absolute world Y on a flat
            //    world and its height above the probe point along the local radial on a sphere;
            //    both are normalised here into "metres above the point, along up".
            float reading = VoxelEngine.Maritime.WaterProbeSystem.GetSurfaceHeight(point);
            if (reading > VoxelEngine.Maritime.WaterProbeSystem.NoWaterHeight * 0.5f)
            {
                surfaceAbovePoint = sphere ? reading : reading - Vector3.Dot(point, up);
                return true;
            }
            if (!sphere) return false;

            // 2) The ocean shell. Only where what is under the station lies BELOW the shell —
            //    dry ground above the sea is dry, whatever the arithmetic might prefer. With no
            //    bed within reach of a forty-metre probe the column is deep water rather than a
            //    dry chasm at the water's edge; that is the one assumption here, and the demand
            //    arithmetic caps how wrong it can be (nothing is lifted unless the station sits
            //    within a freeboard of the sea).
            float aboveSea = VoxelEngine.WaterSim.PlanetWaterUtility.SignedDistanceToSea(point);
            if (aboveSea < 0f) { surfaceAbovePoint = -aboveSea; return true; }
            bool bed = AsphaltRoad.ProbeGround(point, up, out float bedAbove, 40f);
            if (!bed || bedAbove < -aboveSea) { surfaceAbovePoint = -aboveSea; return true; }
            return false;
        }

        /// <summary>Cuts and fills the ground under one plan cell to the level the cell will sit
        /// at, at commit, before the slab is placed. Every lane of the station was given the same
        /// target, so the graded rectangles meet with no seam. Cut and fill are each capped at the
        /// grader's reach; the verdict has already refused anything further out than that.</summary>
        public static void LevelGroundTo(Vector3 position, Quaternion rotation, float cell,
                                         float targetAlongUp)
        {
            var world = VoxelEngine.Core.ActiveWorld.Current;
            if (world == null) return;
            Vector3 up = rotation * Vector3.up;
            Vector3 right = rotation * Vector3.right;
            Vector3 forward = rotation * Vector3.forward;
            float step = cell / 3f;
            float baseH = Vector3.Dot(position, up);

            for (int gx = -1; gx <= 1; gx++)
            for (int gz = -1; gz <= 1; gz++)
            {
                Vector3 lateral = position + right * (gx * step) + forward * (gz * step);
                if (!AsphaltRoad.ProbeGround(lateral, up, out float off)) continue;
                float d = targetAlongUp - (baseH + off);   // >0 fill, <0 cut

                if (d > CARVE_EPSILON)
                {
                    // Fill: pour solid voxels from the current surface up to the target, in the
                    // ground's own material so the graded shoulder does not read as a scar.
                    float top = Mathf.Min(d, LEVEL_FILL_MAX);
                    Vector3Int a = world.WorldToVoxel(lateral + up * (off + 0.05f));
                    Vector3Int b = world.WorldToVoxel(lateral + up * (off + top - 0.05f));
                    var material = GroundMaterialAt(world, lateral + up * (off - 0.6f));
                    for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                    for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                    for (int z = Mathf.Min(a.z, b.z); z <= Mathf.Max(a.z, b.z); z++)
                    {
                        var v = new Vector3Int(x, y, z);
                        if (!world.GetVoxelWorld(v).IsSolid)
                            world.SetVoxelWorld(v, material, remesh: true);
                    }
                }
                else if (d < -CARVE_EPSILON)
                {
                    // Cut: empty what stands above the target — the same margins as the carve,
                    // a little above the bump so the last voxel of it goes too.
                    float cut = Mathf.Min(-d, LEVEL_CUT_MAX);
                    Vector3Int a = world.WorldToVoxel(lateral + up * (off - cut + 0.05f));
                    Vector3Int b = world.WorldToVoxel(lateral + up * (off + 0.30f));
                    for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
                    for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
                    for (int z = Mathf.Min(a.z, b.z); z <= Mathf.Max(a.z, b.z); z++)
                    {
                        var v = new Vector3Int(x, y, z);
                        if (world.GetVoxelWorld(v).IsSolid)
                            world.SetVoxelWorld(v, VoxelEngine.Core.Voxel.Empty, remesh: true);
                    }
                }
            }
        }

        /// <summary>The voxel to pour a fill from: the terrain's own material where the sample
        /// finds terrain, plain solid where it finds nothing (the same default the world itself
        /// builds ground from).</summary>
        private static VoxelEngine.Core.Voxel GroundMaterialAt(VoxelEngine.Core.IVoxelWorld world,
                                                               Vector3 at)
        {
            var v = world.GetVoxelWorld(world.WorldToVoxel(at));
            return v.IsSolid ? v : VoxelEngine.Core.Voxel.Solid;
        }

        /// <summary>True when a road cell already occupies this slot, so paving again would be a
        /// no-op the player should be told about rather than charged for.</summary>
        public static bool IsCellOccupied(Vector3 position, float cellSize)
        {
            RoadSurfaceUtility.QueryAt(position, _anchorScratch);
            float limit = cellSize * 0.45f;
            for (int i = 0; i < _anchorScratch.Count; i++)
            {
                var road = _anchorScratch[i];
                if (road == null) continue;
                if ((road.transform.position - position).sqrMagnitude <= limit * limit) return true;
            }
            return false;
        }

        /// <summary>
        /// Commits one road cell. Instantiates the authored prefab, wires the `PlacedBlock` exactly
        /// as `BuildSystem.TryPlace` does, and tells the strip to reshape itself around the new cell.
        /// </summary>
        public static AsphaltRoad PlaceCell(BlockItem block, Vector3 position, Quaternion rotation)
        {
            if (block == null || block.placedPrefab == null) return null;

            // Shave before placing, so the slab lands on ground it already owns. Doing it here, in
            // the one choke point both the planner and hand placement go through, is what keeps the
            // ghost, the verdict and the world from disagreeing about whether a bump exists.
            var carveTemplate = block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true);
            float carveCell = carveTemplate != null ? carveTemplate.cellSize : 1f;
            JudgeCell(block, position, rotation, out _, out bool needsCarve);
            if (needsCarve) CarveCell(position, rotation, carveCell);

            var go = Object.Instantiate(block.placedPrefab, position, rotation);
            go.name = block.displayName;

            if (go.GetComponentInChildren<Collider>() == null) go.AddComponent<BoxCollider>();

            var placed = go.GetComponent<PlacedBlock>();
            if (placed == null) placed = go.AddComponent<PlacedBlock>();
            placed.Item   = block;
            placed.Hp     = block.blockHealth;
            placed.onGrid = false;

            if (block.placedMaterial != null || block.texture != null)
            {
                var texturizer = go.AddComponent<BlockTexturizer>();
                texturizer.overrideMaterial = block.placedMaterial;
                texturizer.overrideTexture  = block.texture;
            }

            var road = go.GetComponentInChildren<AsphaltRoad>(true);
            road?.RefreshAfterPlacement();
            return road;
        }

        // ════════════════════════════════════════════════════════
        //  THE PLAN — point to point, owned by PlayerInteractionTool
        //
        //  This replaces the old hold-to-drag gesture. A drag could only ever lay one cell wide,
        //  only where the mouse happened to sweep, and it spent material before the player could
        //  see what the whole strip would cost. A road is a DESIGN, not a scribble: click a start,
        //  click an end, and the planner fills the corridor between them at the chosen width,
        //  dropping every cell onto the ground. The ghost shows the entire committed shape before
        //  a single unit is spent, red when the ground refuses part of the line or the player
        //  cannot afford it. Starting a plan on an existing road extends that road in its own
        //  frame, which is what makes connect-and-extend fall out of the gesture for free.
        // ════════════════════════════════════════════════════════

        public const int MIN_WIDTH = 1;
        /// <summary>Widest carriageway the paver will lay, in cells. Ten cells is 40 m on the 4 m
        /// slab, which is a motorway rather than a road; the corner solver enforces the real
        /// limit, since a carriageway this wide simply cannot turn inside a short leg.</summary>
        public const int MAX_WIDTH = 10;

        private bool _planning;
        /// <summary>The clicked points of the open plan. A road is a polyline: the corridor follows
        /// every waypoint, so bends are placed, not approximated.</summary>
        private readonly List<Vector3> _wpPos = new List<Vector3>(8);
        private readonly List<Quaternion> _wpRot = new List<Quaternion>(8);
        private readonly HashSet<long> _planSeen = new HashSet<long>(256);
        /// <summary>Coarse pocket keys already filled this solve: one junction pocket can be
        /// discovered from two different cells, and only one cell may go into it. Cleared by
        /// `FillJunctionPockets` at the start of every solve.</summary>
        private readonly HashSet<long> _pocketSeen = new HashSet<long>(64);
        private readonly List<Vector3> _planInR  = new List<Vector3>(128);
        private readonly List<Vector3> _planOutR = new List<Vector3>(128);
        private readonly List<float> _planInM  = new List<float>(128);
        private readonly List<float> _planOutM = new List<float>(128);

        // ── Corridor solve ──────────────────────────────────────────────
        // The planner no longer lays legs and stitches them; it solves the whole clicked polyline
        // as one carriageway (`RoadCorridor`) and emits the cells that solve produced. Two solves
        // happen per frame while a plan is open: the COMMITTED polyline, which is what the interact
        // key will actually lay, and the LIVE polyline out to the cursor, which is what the ghost
        // draws. They are kept apart because appending a live leg turns the last waypoint into a
        // corner, and a corner changes the geometry of the cells before it — a ghost built from the
        // live solve would promise a fillet the commit does not lay.
        private readonly RoadCorridorBuffers _commitCorridor = new RoadCorridorBuffers();
        private readonly RoadCorridorBuffers _liveCorridor = new RoadCorridorBuffers();
        private readonly List<Vector3> _quadSW = new List<Vector3>(256);
        private readonly List<Vector3> _quadSE = new List<Vector3>(256);
        private readonly List<Vector3> _quadNE = new List<Vector3>(256);
        private readonly List<Vector3> _quadNW = new List<Vector3>(256);
        private readonly List<bool> _planExplicit = new List<bool>(256);
        private readonly List<Vector3> _corridorPoints = new List<Vector3>(16);

        /// <summary>Corner radius in cells. Clamped at lay time to the minimum the chosen width can
        /// carry without collapsing its inside lane, so this is a request, not a promise.</summary>
        private int _cornerRadiusCells = 2;
        private int _width = 1;

        private readonly List<Vector3> _planPos = new List<Vector3>(128);
        private readonly List<Quaternion> _planRot = new List<Quaternion>(128);
        /// <summary>false where the cell lands on pavement that already exists — it is not placed
        /// again and not charged again, but its run is resurfaced instead.</summary>
        private readonly List<bool> _planFresh = new List<bool>(128);
        private readonly List<int> _planCost = new List<int>(128);
        private readonly List<RoadRun> _planTouchedRuns = new List<RoadRun>(8);
        private string _refusal;
        /// <summary>Verdict of the LIVE preview leg only. Tints the ghost; never reaches the
        /// HUD, because the preview describes ground the player has not committed to.</summary>
        private string _liveRefusal;
        private int _totalCost;

        // ── Water crossings ──
        // The paver does not make the player lay bridge cells by hand. A corridor that reaches
        // water gets its crossing inserted automatically where it crosses, charged at its own
        // price, because asking a player to eyeball a deck level is asking them to do the
        // surveyor's job. These two are set by the caller from the tool each frame, because
        // `UpdatePlan` is handed the road block and not the tool.
        /// <summary>Block laid for deck cells, or null when this paver cannot cross water.</summary>
        public BlockItem BridgeBlock { get; set; }
        /// <summary>Bridge material charged per deck cell.</summary>
        public int BridgeCostPerCell { get; set; } = 6;
        /// <summary>Whether crossings this plan lays are drawbridges rather than fixed. Set from the
        /// surface wheel: the same asphalt road, but the structure over water can open. Off by
        /// default, because a deck that swings shut under a lorry is not a thing to opt into by
        /// accident.</summary>
        public bool CrossingsOpen { get; set; }

        /// <summary>Metres a FIXED deck must stand above the water it crosses. Below this a boat
        /// that cannot fit under is a boat the road has blocked, so the deck is lifted until the
        /// ordinary small craft of the world pass beneath it. Together with the pier spacing this
        /// is what makes a crossing read as a bridge rather than as a causeway laid on the surface.</summary>
        public const float DECK_FREEBOARD_FIXED = 3.6f;

        /// <summary>Metres a DRAWBRIDGE deck must stand above the water. Lower than the fixed
        /// figure on purpose: a drawbridge opens for whatever cannot fit under it, so its closed
        /// clearance only has to clear the water itself, not the traffic on it.</summary>
        public const float DECK_FREEBOARD_DRAWBRIDGE = 2.4f;

        /// <summary>Steepest grade the deck profile may climb or descend per cell while rising from
        /// the shore to its clearance over the channel. The shore end of a crossing is level with
        /// the road that leads onto it; this is the slope that takes it from there to standing over
        /// the water, and it stays inside what the road's own grade rules call rough-but-paveable.</summary>
        public const float DECK_MAX_GRADE = 0.15f;

        /// <summary>Deepest cut the grader will make below a station's level, in metres. Cut and
        /// fill together are what the paver will do to a strip that is too far out of level to
        /// pave; beyond that the ground is a cliff, and a road that sliced it flat would not be a
        /// road, it would be a quarry.</summary>
        public const float LEVEL_CUT_MAX = 3f;

        /// <summary>Highest fill the grader will pour above a station's level, in metres.</summary>
        public const float LEVEL_FILL_MAX = 3f;

        private int _bridgeCost;
        private int _gapCount;
        private readonly List<bool>  _planBridge   = new List<bool>(128);
        private readonly List<float> _groundAt     = new List<float>(64);
        private readonly List<bool>  _isGap        = new List<bool>(64);
        private readonly List<int>   _gapOfStation = new List<int>(64);
        private readonly List<float> _deckAt       = new List<float>(64);
        private readonly List<float> _gapExcess    = new List<float>(64);
        private readonly List<Vector3> _stationFrame = new List<Vector3>(64);
        private readonly List<float> _levelAt      = new List<float>(64);
        private readonly List<string> _levelWhy    = new List<string>(64);
        private readonly List<bool>  _planLevel    = new List<bool>(128);
        private readonly List<BridgeSpan> _planSpans = new List<BridgeSpan>(4);

        /// <summary>Bridge material per deck cell. Deliberately well above the asphalt price: a
        /// crossing has to hold itself up over nothing, and that should cost more than pavement.</summary>
        private int BridgeCellCost => Mathf.Max(1, BridgeCostPerCell);

        public bool IsPlanning => _planning;
        public int Width => _width;
        public int PlannedCells => _planPos.Count;
        public string Refusal => _refusal;
        public int TotalCost => _totalCost;
        /// <summary>True when the line crosses pavement that already exists. A plan that lays
        /// nothing new is still work — it resurfaces the runs it crossed — so it must not read
        /// as a refusal in the ghost.</summary>
        public bool HasTouchedRuns => _planTouchedRuns.Count > 0;

        private int _commitCount;
        private int _commitCost;
        private int _commitRunCount;
        private string _commitRefusal;

        /// <summary>What laying the CLICKED polyline right now would spend. The live leg to the aim
        /// is excluded: it is preview, not commitment.</summary>
        public int CommitCost => _commitCost;
        /// <summary>Deck cells in the committed plan. The caller needs this to check the bridge
        /// material separately, because the crossing bills a different pot than the road.</summary>
        public int CommitBridgeCells { get; private set; }
        public string CommitRefusal => _commitRefusal;
        public bool HasCommitWork => _commitCost > 0 || _commitRunCount > 0;

        public void SetWidth(int width) => _width = Mathf.Clamp(width, MIN_WIDTH, MAX_WIDTH);

        /// <summary>First click. Snaps to an existing road when one is in reach, so the plan starts
        /// in that road's frame instead of on a slightly different heading from the aim.</summary>
        public bool BeginPlan(RaycastHit hit, bool hasHit, BlockItem block, out string refusal)
        {
            refusal = null;
            _planning = false;
            _wpPos.Clear(); _wpRot.Clear();
            if (!hasHit || block == null || block.placedPrefab == null)
            {
                refusal = "Nothing to pave on";
                return false;
            }
            if (!TryComputePose(hit, block, out var p, out var r))
            {
                refusal = "Nothing to pave on";
                return false;
            }
            _wpPos.Add(p); _wpRot.Add(r);
            _planning = true;
            return true;
        }

        /// <summary>Adds a corner to the open plan. A road is a polyline, not a single span: every
        /// click after the first is a waypoint, and the corridor follows the waypoints instead of
        /// ignoring everything between the two ends.</summary>
        public bool AddWaypoint(RaycastHit hit, bool hasHit, BlockItem block, out string refusal)
        {
            refusal = null;
            if (!_planning || !hasHit || block == null || block.placedPrefab == null)
            {
                refusal = "Nothing to pave on";
                return false;
            }
            if (!TryComputePose(hit, block, out var p, out var r))
            {
                refusal = "Nothing to pave on";
                return false;
            }
            _wpPos.Add(p); _wpRot.Add(r);
            return true;
        }

        public int WaypointCount => _wpPos.Count;

        /// <summary>Recomputes the whole corridor: every committed waypoint pair, then a live segment
        /// out to the current aim so the ghost always shows exactly what laying now would build.
        /// </summary>
        public void UpdatePlan(RaycastHit hit, bool hasHit, BlockItem block, bool affordable)
        {
            _planPos.Clear(); _planRot.Clear(); _planFresh.Clear(); _planCost.Clear();
            _planInR.Clear(); _planOutR.Clear(); _planInM.Clear(); _planOutM.Clear();
            _quadSW.Clear(); _quadSE.Clear(); _quadNE.Clear(); _quadNW.Clear();
            _planTouchedRuns.Clear(); _planSeen.Clear();
            _planBridge.Clear(); _groundAt.Clear(); _isGap.Clear();
            _gapOfStation.Clear(); _deckAt.Clear(); _planSpans.Clear(); _planLevel.Clear();
            _stationFrame.Clear(); _levelAt.Clear(); _levelWhy.Clear();
            _refusal = null; _liveRefusal = null; _totalCost = 0; _bridgeCost = 0; _gapCount = 0;
            _commitCount = 0; _commitCost = 0; _commitRunCount = 0; _commitRefusal = null;
            if (!_planning || block == null || _wpPos.Count == 0) { HideGhost(); return; }

            float cell = CellOf(block);
            // One tangent frame for the whole corridor: a corridor is short against a planet, and
            // `PlaceCell` re-drops every cell onto the real ground afterwards regardless.
            Vector3 up = _wpRot[0] * Vector3.up;
            Vector3 aimPos = default;   // assigned only when haveAim; the && below short-circuits,
                                      // so it has to be declared to be definitely assigned
            bool haveAim = hasHit && TryComputePose(hit, block, out aimPos, out _);

            // ── 1) The COMMITTED solve: what the interact key will actually lay. ──
            // With one waypoint there is no clicked polyline yet, so the cursor IS the endpoint —
            // otherwise the ghost would show a route to the cursor and the click would lay a single
            // stub cell, which is exactly "I can only ever pave one row". With two or more waypoints
            // the clicked route is the contract and the cursor is only a preview.
            CollectWaypoints(_corridorPoints, _wpPos.Count == 1);
            if (_wpPos.Count == 1)
            {
                if (haveAim) _corridorPoints[1] = aimPos;
                else _corridorPoints[1] = _corridorPoints[0] + (_wpRot[0] * Vector3.forward) * cell;
            }
            EmitCorridor(_commitCorridor, _corridorPoints, up, block, cell, true);
            FillJunctionPockets(block, cell);

            // Snapshot what that polyline commits. The live leg below is a PREVIEW: it leads the
            // cursor, and if the aim happens to be at a wall or the sky it tints the ghost red —
            // but it must never stop the player laying the route they actually clicked, and it must
            // never reach the HUD. Printing the preview's verdict every frame is what made the paver
            // shout "cannot pave inside a wall" while standing on flat open ground.
            _commitCount = _planPos.Count;
            _commitCost = _totalCost;
            // Snapshotted here, before the live preview leg adds its own deck cells to the running
            // total: what the interact key will lay is the clicked route, not the route plus the
            // leg the cursor happens to be pointing at.
            CommitBridgeCells = _bridgeCost / BridgeCellCost;
            _commitRefusal = _refusal;
            _commitRunCount = _planTouchedRuns.Count;

            // ── 2) The LIVE solve: the same route continued to the cursor, for the ghost only. ──
            // Solved separately rather than appended, because continuing the route turns the last
            // clicked waypoint into a corner, and a corner re-fillets the cells before it.
            if (_wpPos.Count >= 2 && haveAim)
            {
                CollectWaypoints(_corridorPoints, true);
                _corridorPoints[_corridorPoints.Count - 1] = aimPos;
                RoadCorridor.Build(_liveCorridor, _corridorPoints, up, _width, cell,
                                   _cornerRadiusCells * cell);
                JudgeCorridor(_liveCorridor, block);
            }
            else
            {
                // Nothing to preview beyond the committed route: show that, so the ghost and the
                // click can never disagree.
                _liveCorridor.DiscardCells();
            }

            bool good = _refusal == null && _liveRefusal == null && affordable
                        && (_commitCost > 0 || _commitRunCount > 0);
            ShowGhost(good);
        }

        /// <summary>Second click. Charges once, places every fresh cell and resurfaces the runs the
        /// line crossed. Refuses the WHOLE line, not just the bad cell, when the ground will not
        /// take it: a road with a gap in it is worse than no road.</summary>
        public bool CommitPlan(BlockItem block, RoadPaverTool tool, Inventory inventory,
                               ItemDefinition material, int pricePerCell,
                               out int laid, out string refusal)
        {
            laid = 0;
            refusal = _commitRefusal;
            if (!_planning) return false;
            if (_commitRefusal != null) { CancelPlan(); return false; }
            if (material == null) { CancelPlan(); refusal = "No paving material configured"; return false; }
            if (_commitCost <= 0 && _commitRunCount == 0)
            { CancelPlan(); refusal = "That line is already paved"; return false; }
            if (inventory.CountOf(material) < _commitCost)
            { CancelPlan(); refusal = "Needs " + _commitCost + " " + material.displayName; return false; }

            int repairUnits = 0;
            for (int i = 0; i < _commitRunCount; i++)
            {
                var run = _planTouchedRuns[i];
                if (run == null || run.Wear01 <= 0f) continue;
                repairUnits += Mathf.CeilToInt(run.Wear01 * run.PavedArea * tool.repairMaterialPerSquareMetre);
            }
            if (repairUnits > 0 && inventory.CountOf(material) < _totalCost + repairUnits)
            {
                CancelPlan();
                refusal = "Needs " + (_totalCost + repairUnits) + " " + material.displayName;
                return false;
            }

            // ── The crossing bills itself ──
            // A deck cell is a different structure from a pavement cell and is built from a
            // different material, so it is charged separately rather than folded into the asphalt
            // total. Charging it from the same pot would let a player cross a river for the price
            // of the asphalt that happens to be on top of it.
            int bridgeCells = 0;
            for (int i = 0; i < _commitCount; i++)
                if (_planFresh[i] && i < _planBridge.Count && _planBridge[i]) bridgeCells++;

            var bridgeMaterial = tool != null ? tool.bridgeMaterial : null;
            int bridgeUnits = bridgeCells > 0 && BridgeBlock != null
                ? bridgeCells * Mathf.Max(1, tool.bridgeMaterialPerCell) : 0;
            if (bridgeUnits > 0)
            {
                if (bridgeMaterial == null)
                { CancelPlan(); refusal = "No bridge material configured"; return false; }
                if (inventory.CountOf(bridgeMaterial) < bridgeUnits)
                {
                    CancelPlan();
                    refusal = "Needs " + bridgeUnits + " " + bridgeMaterial.displayName + " for the crossing";
                    return false;
                }
            }

            // The substructure bills itself as well: the piers that stand in the riverbed and the
            // girders that run under the deck are masonry, and masonry is stone. Two pots on one
            // crossing is deliberate — the deck is iron plate and the supports are stone, and
            // folding them together would let a stone-rich player cross water for nothing but
            // stone, or an iron-rich one for nothing but iron.
            var pierMaterial = tool != null ? tool.pierMaterial : null;
            int pierUnits = bridgeCells > 0 && pierMaterial != null
                ? bridgeCells * Mathf.Max(0, tool.pierMaterialPerCell) : 0;
            if (pierUnits > 0 && inventory.CountOf(pierMaterial) < pierUnits)
            {
                CancelPlan();
                refusal = "Needs " + pierUnits + " " + pierMaterial.displayName
                        + " for the bridge supports";
                return false;
            }

            inventory.container.Remove(material, _commitCost + repairUnits);
            if (bridgeUnits > 0) inventory.container.Remove(bridgeMaterial, bridgeUnits);
            if (pierUnits > 0) inventory.container.Remove(pierMaterial, pierUnits);

            _planSpans.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // After three crossings that came back "still in the water", the next report has to
            // arrive as numbers, not as an adjective. One line per gap span, straight from the
            // lists the solver left behind (they live until the plan is cancelled): which
            // stations, where the deck actually stands, how far it climbed over its own ends,
            // and what the water says on a fresh sample at the station it climbed highest for.
            // Compiled out of release builds; in the editor it is the difference between another
            // blind fix and a diagnosis.
            if (_isGap.Count > 0 && _planRot.Count > 0)
            {
                Vector3 diagUp = _planRot[0] * Vector3.up;
                for (int g = 0; g < _isGap.Count; g++)
                {
                    if (!_isGap[g]) continue;
                    int lastGap = g;
                    while (lastGap + 1 < _isGap.Count && _isGap[lastGap + 1]) lastGap++;
                    float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
                    int peakStation = g;
                    for (int s = g; s <= lastGap && s < _deckAt.Count; s++)
                    {
                        if (_deckAt[s] < lo) lo = _deckAt[s];
                        if (_deckAt[s] > hi) { hi = _deckAt[s]; peakStation = s; }
                    }
                    float rise = hi - Mathf.Max(_deckAt[g], _deckAt[lastGap]);
                    string water = "none";
                    if (peakStation < _stationFrame.Count &&
                        WaterSurfaceAbove(_stationFrame[peakStation], diagUp, out float diagSurf))
                        water = "+" + diagSurf.ToString("F2") + "m above station " + peakStation;
                    Debug.Log("[RoadPaver] gap " + g + "-" + lastGap + " (" + (lastGap - g + 1)
                              + " cells) deck " + lo.ToString("F2") + ".." + hi.ToString("F2")
                              + " rise " + rise.ToString("F2") + " water " + water);
                    g = lastGap;
                }
            }
#endif

            // ── Grade the ground BEFORE any slab lands on it ──
            // Leveling used to run inside the placement loop, one cell ahead of its own slab —
            // which meant any neighbour placed earlier draped a mesh over ground the grader had
            // not touched yet, and the seam between a graded station and the road before it stood
            // as a step. All the cut and fill happens first now, so every cell's drape — graded
            // or not — samples the finished terrain, and the transition onto a graded station is
            // a ramp inside the neighbouring slab instead of a ledge between two.
            for (int i = 0; i < _commitCount; i++)
            {
                if (!_planFresh[i]) continue;
                bool deckEntry = i < _planBridge.Count && _planBridge[i] && BridgeBlock != null;
                if (!deckEntry && i < _planLevel.Count && _planLevel[i])
                    LevelGroundTo(_planPos[i], _planRot[i], CellOf(block),
                                  Vector3.Dot(_planPos[i], _planRot[i] * Vector3.up));
            }

            for (int i = 0; i < _commitCount; i++)
            {
                if (!_planFresh[i]) continue;
                bool isDeck = i < _planBridge.Count && _planBridge[i] && BridgeBlock != null;
                var road = PlaceCell(isDeck ? BridgeBlock : block, _planPos[i], _planRot[i]);
                if (road != null)
                {
                    if (_planExplicit[i])
                    {
                        // The corridor solver already cut this cell to the carriageway's shared
                        // cross-sections, so its footprint is the finished quad. The curve frame is
                        // cleared rather than left stale: both describe the outline and the explicit
                        // one wins, but a stale frame would resurface the moment the quad is cleared.
                        road.hasExplicitFootprint = true;
                        road.quadSW = _quadSW[i]; road.quadSE = _quadSE[i];
                        road.quadNE = _quadNE[i]; road.quadNW = _quadNW[i];
                        road.curveInRight = Vector3.zero; road.curveOutRight = Vector3.zero;
                        road.curveInMitre = 1f; road.curveOutMitre = 1f;
                    }
                    else
                    {
                        road.hasExplicitFootprint = false;
                        road.curveInRight  = Quaternion.Inverse(_planRot[i]) * _planInR[i];
                        road.curveOutRight = Quaternion.Inverse(_planRot[i]) * _planOutR[i];
                        road.curveInMitre  = _planInM[i];
                        road.curveOutMitre = _planOutM[i];
                    }
                    road.RefreshAfterPlacement();

                    // A junction box stays SQUARE, like the reference intersections: three or more
                    // connected edges means this cell is a crossing, and a crossing mitred to one
                    // of its arms would pinch the others. Only for cells that carry a curve frame —
                    // a corridor cell's quad is already the right shape for the crossing it is in.
                    if (!road.hasExplicitFootprint)
                    {
                        var e = road.Edges;
                        int conn = ((e & RoadEdgeMask.North) != 0 ? 1 : 0) + ((e & RoadEdgeMask.East) != 0 ? 1 : 0)
                                 + ((e & RoadEdgeMask.South) != 0 ? 1 : 0) + ((e & RoadEdgeMask.West) != 0 ? 1 : 0);
                        if (conn >= 3)
                        {
                            road.curveInRight = Vector3.zero; road.curveOutRight = Vector3.zero;
                            road.curveInMitre = 1f; road.curveOutMitre = 1f;
                            road.RefreshAfterPlacement();
                        }
                    }
                }
                laid++;
            }
            for (int i = 0; i < _commitRunCount; i++)
                if (_planTouchedRuns[i] != null && _planTouchedRuns[i].Wear01 > 0f)
                    _planTouchedRuns[i].Repair();

            // ── Assemble the crossings ──
            // Spans are built after the deck is on the ground rather than during placement, because
            // a span's classification and pier spacing depend on the finished crossing: a cell laid
            // first does not yet know whether it is one pier of a viaduct or the whole of a culvert.
            if (bridgeCells > 0)
            {
                float spanReach = CellOf(BridgeBlock) * 1.75f;
                var neighbours = new List<AsphaltRoad>(8);
                var decks = new List<AsphaltRoad>(bridgeCells);
                for (int i = 0; i < _commitCount; i++)
                {
                    if (!_planFresh[i] || i >= _planBridge.Count || !_planBridge[i]) continue;
                    var deck = RoadAt(_planPos[i], CellOf(BridgeBlock));
                    if (deck != null && deck.surfaceKind == RoadSurfaceKind.Bridge
                        && !decks.Contains(deck)) decks.Add(deck);
                }
                for (int d = 0; d < decks.Count; d++)
                {
                    neighbours.Clear();
                    for (int o = 0; o < decks.Count; o++)
                    {
                        if (o == d) continue;
                        if ((decks[o].transform.position - decks[d].transform.position).magnitude <= spanReach)
                            neighbours.Add(decks[o]);
                    }
                    var span = BridgeSpan.JoinOrCreate(decks[d], neighbours);
                    if (!_planSpans.Contains(span)) _planSpans.Add(span);
                }
                // The leaves are surfaced from the deck block's own material rather than a second
                // authored one: a drawbridge leaf is the deck, and a leaf that did not match the
                // cells it stands in for would read as a different structure while it swung.
                Material deckMat = null;
                if (BridgeBlock != null && BridgeBlock.placedPrefab != null)
                {
                    var deckRenderer = BridgeBlock.placedPrefab.GetComponentInChildren<MeshRenderer>(true);
                    if (deckRenderer != null) deckMat = deckRenderer.sharedMaterial;
                }
                for (int sp = 0; sp < _planSpans.Count; sp++)
                    _planSpans[sp].Rebuild(allowDrawbridge: CrossingsOpen, deckMaterial: deckMat);
            }

            int spent = _commitCost + repairUnits;
            CancelPlan();
            VoxelEngine.UI.BuildFeedbackHud.Show(block.displayName + " placed",
                laid + " cell(s) · " + spent + " " + material.displayName
                + (repairUnits > 0 ? " · includes resurfacing" : "")
                + (bridgeCells > 0
                    ? " · " + bridgeCells + " deck cell(s), " + bridgeUnits + " " + bridgeMaterial.displayName
                      + (pierUnits > 0 ? " + " + pierUnits + " " + pierMaterial.displayName + " supports" : "")
                    : ""),
                block.icon, new Color(0.55f, 0.80f, 0.95f));
            return laid > 0;
        }

        public void CancelPlan()
        {
            _planning = false;
            _wpPos.Clear(); _wpRot.Clear();
            _planPos.Clear(); _planRot.Clear(); _planFresh.Clear(); _planCost.Clear();
            _planInR.Clear(); _planOutR.Clear(); _planInM.Clear(); _planOutM.Clear();
            _quadSW.Clear(); _quadSE.Clear(); _quadNE.Clear(); _quadNW.Clear(); _planExplicit.Clear();
            _planTouchedRuns.Clear(); _planSeen.Clear();
            _planBridge.Clear(); _groundAt.Clear(); _isGap.Clear();
            _gapOfStation.Clear(); _deckAt.Clear(); _planSpans.Clear(); _planLevel.Clear();
            _stationFrame.Clear(); _levelAt.Clear(); _levelWhy.Clear();
            _refusal = null; _liveRefusal = null; _totalCost = 0; _bridgeCost = 0; _gapCount = 0;
            _commitCount = 0; _commitCost = 0; _commitRunCount = 0; _commitRefusal = null;
            HideGhost();
        }

        /// <summary>One straight leg of the polyline: forward from the leg's start along its own
        /// frame, fanned out sideways by the chosen width. Width means "cells of whatever I am
        /// paving", so 3 wide on road is 12 m of carriageway and 3 wide on pathway is 3 m of cobbles.
        /// Cells already in the plan from an earlier leg are skipped, which is what lets two legs
        /// share their corner cell instead of paying for it twice.</summary>
        /// <summary>Fills <paramref name="into"/> with the clicked waypoints, plus a trailing copy
        /// of the last one when <paramref name="withLiveSlot"/> is set so the caller can overwrite
        /// it with the cursor position without allocating.</summary>
        private void CollectWaypoints(List<Vector3> into, bool withLiveSlot)
        {
            into.Clear();
            for (int i = 0; i < _wpPos.Count; i++) into.Add(_wpPos[i]);
            if (withLiveSlot && into.Count > 0) into.Add(into[into.Count - 1]);
        }

        /// <summary>
        /// Solves the corridor and emits one plan cell per cell the solve produced. This replaces
        /// the per-leg `BuildSegment` plus the `FillCorners` and `FillWedges` passes: because every
        /// lane shares the carriageway's cross-sections, there are no seams left to chamfer or fill.
        /// </summary>
        private void EmitCorridor(RoadCorridorBuffers buf, List<Vector3> points, Vector3 up,
                                  BlockItem block, float cell, bool emit)
        {
            RoadCorridor.Build(buf, points, up, _width, cell, _cornerRadiusCells * cell);

            if (buf.cornerTooTight)
            {
                SetRefusal("Corner too tight for a road " + _width + " wide - widen the turn, "
                           + "or Ctrl+scroll narrower");
                return;
            }
            if (!emit) { JudgeCorridor(buf, block); return; }

            int n = buf.cellsPerLane;
            if (n <= 0) return;

            // ── Pass 1: where is the ground, and where is it not? ──
            // A corridor that reaches water used to be refused whole, which is the correct answer
            // for a road and the wrong answer for a crossing. So the ground is measured first, the
            // stations that have none are collected into gaps, and each gap gets a deck level from
            // the ground at its two ends — a bridge deck is level with its approaches, not with the
            // riverbed, which is what makes the road run straight over instead of diving in.
            _groundAt.Clear(); _isGap.Clear();
            _gapOfStation.Clear(); _deckAt.Clear();
            _stationFrame.Clear(); _levelAt.Clear(); _levelWhy.Clear();
            // Restarted here and not left to `UpdatePlan`: `EmitCorridor` runs twice per frame, once
            // for the committed route and once for the live preview leg, and each solve numbers its
            // own gaps. Letting the counter carry over left the deck-level loop walking gap numbers
            // the current solve never assigned.
            _gapCount = 0;
            // Filled with Add rather than sized up front: `List<T>` has no Resize, and these four
            // are reused scratch that were just cleared, so growing them one station at a time is
            // the same work without a second API to invent.
            for (int k = 0; k < n; k++)
            {
                _groundAt.Add(float.NaN); _isGap.Add(false);
                _gapOfStation.Add(-1);    _deckAt.Add(0f);
                _stationFrame.Add(default); _levelAt.Add(float.NaN); _levelWhy.Add(null);
            }
            // One representative frame position per station: the gap solver needs somewhere to
            // sample the water surface, and a reference height to turn frame-relative offsets into
            // absolute levels. The first lane that exists at the station is as good a spot as any —
            // every lane at a station spans the same two cross-sections, so they all agree on
            // height to within a rounding hair.
            for (int k = 0; k < n; k++)
            {
                for (int w = 0; w < _width; w++)
                {
                    var stationFrame = RoadCorridor.CellAt(buf, w, k);
                    if (stationFrame == null) continue;
                    _stationFrame[k] = stationFrame.position;
                    break;
                }
            }

            bool canBridge = BridgeBlock != null;
            for (int w = 0; w < _width; w++)
            {
                for (int k = 0; k < n; k++)
                {
                    var frame = RoadCorridor.CellAt(buf, w, k);
                    if (frame == null) continue;
                    bool hit = AsphaltRoad.ProbeGround(frame.position, up, out float off);
                    if (hit && float.IsNaN(_groundAt[k])) _groundAt[k] = off;
                    var band = hit
                        ? JudgeCell(block, frame.position + up * off, frame.rotation, out _, out _)
                        : AsphaltRoad.GradeBand.NoGround;
                    // The bed under shallow water is dry voxels with a smooth slope, which the
                    // site evaluation correctly calls paveable land — and the crossing then
                    // drapes ordinary road along the lakebed, underwater. That is how a river
                    // crossing could end up "a bridge under the water" without a single deck
                    // cell in it. Ground that lies below the water surface is water, whatever
                    // the bed voxels say: the ocean shell especially is not per-voxel water,
                    // and the shallow shelf it covers reads as dry ground to a voxel test.
                    if (hit && (band == AsphaltRoad.GradeBand.Smooth || band == AsphaltRoad.GradeBand.Rough)
                        && WaterSurfaceAbove(frame.position, up, out float surfaceAbove)
                        && surfaceAbove > off)
                        band = AsphaltRoad.GradeBand.Underwater;
                    bool gap = band == AsphaltRoad.GradeBand.Underwater
                            || band == AsphaltRoad.GradeBand.NoGround;
                    if (gap) _isGap[k] = true;
                    else if (band == AsphaltRoad.GradeBand.TooRough || band == AsphaltRoad.GradeBand.Buried)
                    {
                        // The station's graded level is resolved HERE, in the measuring pass, and
                        // not left to pass 2 — because the gap solver reads `_groundAt` to anchor
                        // the deck's approach line, and a graded station flanking the water must
                        // anchor the deck to the level its own road will actually sit at, not to
                        // the raw ground the grader is about to move. Cached per station, so the
                        // other lanes of the same station and pass 2's verdict all read the same
                        // answer without re-probing.
                        if (TryGradeStation(buf, up, cell, k, out float gradedLevel, out _))
                            _groundAt[k] = gradedLevel - Vector3.Dot(_stationFrame[k], up);
                    }
                }
            }

            // Number the gaps and give each one a deck level. The starting point is still the
            // ground at the gap's two ends — a bridge deck leaves the bank level with the road that
            // leads onto it, not with the riverbed. But "level with the approaches" alone is what
            // laid decks in the water: where the shore sits at the waterline, so does the deck. So
            // the level over each gap is the HIGHER of the approach line and the waterline plus a
            // freeboard, and the result is slope-limited from both shores: the crossing leaves the
            // bank at road level, climbs no faster than a steep road, and stands over the channel.
            int currentGap = -1;
            for (int k = 0; k < n; k++)
            {
                if (!_isGap[k]) { currentGap = -1; continue; }
                if (currentGap < 0) currentGap = _gapCount++;
                _gapOfStation[k] = currentGap;
            }
            float freeboard = CrossingsOpen ? DECK_FREEBOARD_DRAWBRIDGE : DECK_FREEBOARD_FIXED;
            float maxStep = cell * DECK_MAX_GRADE;
            for (int g = 0; g < _gapCount; g++)
            {
                int first = -1, last = -1;
                for (int k = 0; k < n; k++) if (_gapOfStation[k] == g) { if (first < 0) first = k; last = k; }
                float before = float.NaN, after = float.NaN;
                int beforeStation = -1, afterStation = -1;
                for (int k = first - 1; k >= 0; k--)
                    if (!_isGap[k] && !float.IsNaN(_groundAt[k])) { before = _groundAt[k]; beforeStation = k; break; }
                for (int k = last + 1; k < n; k++)
                    if (!_isGap[k] && !float.IsNaN(_groundAt[k])) { after = _groundAt[k]; afterStation = k; break; }

                // Absolute heights along `up`. The offsets the probes return are frame-relative,
                // and the two ends of a gap belong to two different frames: lerping offsets as if
                // they shared an origin is only right when both clicked banks happen to be level.
                // Measured against one axis it is right whatever the approaches do.
                float beforeH = beforeStation >= 0
                    ? Vector3.Dot(_stationFrame[beforeStation], up) + before : float.NaN;
                float afterH = afterStation >= 0
                    ? Vector3.Dot(_stationFrame[afterStation], up) + after : float.NaN;

                int len = last - first + 1;
                _gapExcess.Clear();
                for (int k = first; k <= last; k++)
                {
                    float t = len == 1 ? 0.5f : (k - first) / (float)(len - 1);
                    // The approach line: flat when only one side of the gap has ground, which is
                    // the corridor starting or ending in the water rather than crossing it. The
                    // deck ALWAYS meets this line — it is what makes the crossing flush with the
                    // roads on both banks, and where the banks themselves are steep, the line is
                    // the shore road's own grade, no steeper than what is already paved there.
                    float line = !float.IsNaN(beforeH) && !float.IsNaN(afterH)
                        ? Mathf.Lerp(beforeH, afterH, t)
                        : (!float.IsNaN(beforeH) ? beforeH : afterH);

                    // The waterline demand at this station, as an EXCESS over the approach line.
                    // The deck must stand at least waterline + freeboard over the water, but the
                    // rise to that is ramped, and it is the EXCESS that is ramped rather than the
                    // whole profile: a dry gully or a high-bank crossing has no demand at all, and
                    // its deck must stay exactly on the line, the way it always did.
                    //
                    // The surface itself comes from `WaterSurfaceAbove`, which speaks both of the
                    // world's dialects of water: the voxels the fluid sim fills (lakes, and the
                    // ocean where it is voxel water), and — on the sphere — the planet's ocean
                    // SHELL, which is not per-voxel water everywhere and is invisible to a pure
                    // voxel scan. A crossing over that ocean found no water at all through the
                    // voxel probe, the demand read as zero, and the deck followed the shore line
                    // straight into the sea.
                    float excess = 0f;
                    if (WaterSurfaceAbove(_stationFrame[k], up, out float surfaceAbove))
                    {
                        float waterLevel = Vector3.Dot(_stationFrame[k], up) + surfaceAbove;
                        excess = Mathf.Max(waterLevel + freeboard - line, 0f);
                    }

                    _deckAt[k] = line;
                    _gapExcess.Add(excess);
                }

                // Slope-limit the excess from both shores. The forward pass is the highest rise
                // the near bank permits, the backward pass the far bank's, and the deck carries
                // the lower of the two — the one both banks permit. A gap too short to climb to
                // its freeboard becomes a tent: the crossing stands as high as its banks can ramp
                // it, which over a short hop is the crossing that fits.
                float fwd = float.IsNaN(beforeH) ? float.PositiveInfinity : 0f;
                for (int i = 0; i < len; i++)
                {
                    fwd = Mathf.Min(_gapExcess[i], fwd + maxStep);
                    _gapExcess[i] = fwd;
                }
                float bwd = float.IsNaN(afterH) ? float.PositiveInfinity : 0f;
                for (int i = len - 1; i >= 0; i--)
                {
                    bwd = Mathf.Min(_gapExcess[i], bwd + maxStep);
                    if (bwd < _gapExcess[i]) _gapExcess[i] = bwd;
                }
                for (int k = first; k <= last; k++)
                    _deckAt[k] += _gapExcess[k - first];
            }
            if (_gapCount > 0 && !canBridge)
            {
                SetRefusal("The line crosses water and this paver has no bridge material");
                return;
            }

            // ── Pass 2: lay it ──
            for (int w = 0; w < _width; w++)
            {
                for (int k = 0; k < n; k++)
                {
                    var frame = RoadCorridor.CellAt(buf, w, k);
                    if (frame == null) continue;
                    Vector3 pos = frame.position;
                    Quaternion rot = frame.rotation;
                    bool onBridge = _gapOfStation[k] >= 0;

                    if (!onBridge)
                    {
                        if (!AsphaltRoad.ProbeGround(pos, up, out float off))
                        {
                            SetRefusal("No ground under part of the road");
                            AddCell(pos, rot, true, 0, Vector3.zero, Vector3.zero, 1f, 1f,
                                    frame.sw, frame.se, frame.ne, frame.nw, true);
                            continue;
                        }
                        pos += up * off;

                        if (IsCellOccupied(pos, cell))
                        {
                            var existing = RoadAt(pos, cell);
                            if (existing != null && existing.Run != null && !_planTouchedRuns.Contains(existing.Run))
                                _planTouchedRuns.Add(existing.Run);
                            AddCell(pos, rot, false, 0, Vector3.zero, Vector3.zero, 1f, 1f,
                                    frame.sw, frame.se, frame.ne, frame.nw, true);
                            continue;
                        }

                        var band = JudgeCell(block, pos, rot, out int cost, out _);
                        if (band != AsphaltRoad.GradeBand.Smooth && band != AsphaltRoad.GradeBand.Rough)
                        {
                            // Ground too far out of level — or buried in it — is where the paver
                            // used to send the player away to flatten the strip by hand. It grades
                            // instead: the whole station is cut and filled to one level at commit,
                            // and the cell keeps the rough-ground price, because grading rough
                            // ground is exactly what that price was always for. What the grader
                            // cannot reach still refuses — a cliff taller than its cut, or a
                            // building standing in the footprint.
                            // Declared and defaulted OUTSIDE the short-circuit: `&&` never calls
                            // `TryGradeStation` when the band is neither too rough nor buried, and
                            // an out parameter the call never ran is an unassigned local to the
                            // compiler, whatever the logic promises about only reading it later.
                            float gradedTo = 0f;
                            string gradeWhy = null;
                            bool grade = (band == AsphaltRoad.GradeBand.TooRough ||
                                          band == AsphaltRoad.GradeBand.Buried) &&
                                         TryGradeStation(buf, up, cell, k, out gradedTo, out gradeWhy);
                            if (!grade)
                            {
                                SetRefusal(gradeWhy ?? AsphaltRoad.DescribeSite(band));
                                AddCell(pos, rot, true, 0, Vector3.zero, Vector3.zero, 1f, 1f,
                                        frame.sw, frame.se, frame.ne, frame.nw, true);
                                continue;
                            }
                            pos += up * (gradedTo - Vector3.Dot(pos, up));
                            if (IsCellOccupied(pos, cell))
                            {
                                var gradedExisting = RoadAt(pos, cell);
                                if (gradedExisting != null && gradedExisting.Run != null &&
                                    !_planTouchedRuns.Contains(gradedExisting.Run))
                                    _planTouchedRuns.Add(gradedExisting.Run);
                                AddCell(pos, rot, false, 0, Vector3.zero, Vector3.zero, 1f, 1f,
                                        frame.sw, frame.se, frame.ne, frame.nw, true);
                                continue;
                            }
                            _totalCost += 2;
                            AddCell(pos, rot, true, 2, Vector3.zero, Vector3.zero, 1f, 1f,
                                    frame.sw, frame.se, frame.ne, frame.nw, true);
                            if (_planLevel.Count > 0) _planLevel[_planLevel.Count - 1] = true;
                            continue;
                        }
                        _totalCost += cost;
                        AddCell(pos, rot, true, cost, Vector3.zero, Vector3.zero, 1f, 1f,
                                frame.sw, frame.se, frame.ne, frame.nw, true);
                        continue;
                    }

                    // A deck cell: dropped onto the span's level, not onto the ground, and laid from
                    // the bridge block so it carries the bridge material and the bridge surface kind.
                    // `_deckAt` holds an ABSOLUTE level along `up` — the approach line plus the
                    // ramped waterline excess, measured against the world, not against this frame —
                    // so the cell MOVES to that level rather than adding it as an offset. Adding it
                    // landed the deck at its own height ON TOP of the frame's height: charged,
                    // planned, and floating double-high over the channel where nobody looking at the
                    // water would find it.
                    pos += up * (_deckAt[k] - Vector3.Dot(pos, up));
                    if (IsCellOccupied(pos, cell))
                    {
                        var existingDeck = RoadAt(pos, cell);
                        if (existingDeck != null && existingDeck.Run != null && !_planTouchedRuns.Contains(existingDeck.Run))
                            _planTouchedRuns.Add(existingDeck.Run);
                        AddCell(pos, rot, false, 0, Vector3.zero, Vector3.zero, 1f, 1f,
                                frame.sw, frame.se, frame.ne, frame.nw, true);
                        continue;
                    }
                    _bridgeCost += BridgeCellCost;
                    AddCell(pos, rot, true, BridgeCellCost, Vector3.zero, Vector3.zero, 1f, 1f,
                            frame.sw, frame.se, frame.ne, frame.nw, true);
                    if (_planBridge.Count > 0) _planBridge[_planBridge.Count - 1] = true;
                }
            }
        }

        /// <summary>Walks a solved corridor for refusals only, without emitting cells. This is how
        /// the live leg tints the ghost red over ground that will not take pavement while leaving
        /// the committed plan untouched.</summary>
        private void JudgeCorridor(RoadCorridorBuffers buf, BlockItem block)
        {
            if (buf == null) return;
            Vector3 up = buf.stations.Count > 0
                ? Vector3.Cross(buf.stations[0].right, buf.stations[0].tangent).normalized
                : Vector3.up;
            for (int i = 0; i < buf.cells.Count; i++)
            {
                var frame = buf.cells[i];
                if (!AsphaltRoad.ProbeGround(frame.position, up, out float off))
                { SetLiveRefusal("No ground under part of the road"); continue; }
                var band = JudgeCell(block, frame.position + up * off, frame.rotation, out _, out _);
                if (band != AsphaltRoad.GradeBand.Smooth && band != AsphaltRoad.GradeBand.Rough)
                {
                    // The live ghost shows what the paver will do about rough ground: grade it and
                    // lay the road. Only ground beyond the grader's reach tints the leg red.
                    if (band == AsphaltRoad.GradeBand.TooRough || band == AsphaltRoad.GradeBand.Buried)
                        if (CellGradeable(frame.position, frame.rotation, up, CellOf(block)))
                            continue;
                    SetLiveRefusal(AsphaltRoad.DescribeSite(band));
                }
            }
        }

        /// <summary>Asked-for corner radius, in cells. The solver clamps it to the minimum the
        /// current width can carry, so a tight request on a wide road yields the tightest corner
        /// that still leaves the inside lane a real lane.</summary>
        public void SetCornerRadius(int cells) => _cornerRadiusCells = Mathf.Clamp(cells, 0, 8);
        public int CornerRadiusCells => _cornerRadiusCells;

        /// <summary>The radius actually used for the current width and cell size, in metres — what
        /// the HUD should report, since the request and the result differ on a wide road.</summary>
        public float EffectiveCornerRadius(float cell)
            => RoadCorridor.MinimumRadius(_width, cell) > _cornerRadiusCells * cell
                ? RoadCorridor.MinimumRadius(_width, cell) : _cornerRadiusCells * cell;

        /// <summary>
        /// Fills the diagonal pocket where the NEW corridor meets pavement that is ALREADY IN THE
        /// WORLD. Within one corridor there is nothing to fill: every lane shares the carriageway's
        /// cross-sections, so the cells meet exactly, and 9.43.0 deleted the old wedge pass for that
        /// reason. But a corridor crossing or butting onto a strip laid earlier is a different case —
        /// the two were solved independently, on different lattices, and the diagonal slot between
        /// them belongs to neither. Left alone it is a triangular hole in the middle of a junction,
        /// which is exactly what "connecting two roads does not work" looks like from the saddle.
        ///
        /// Restricted to pockets where at least one flanking cell is an existing world road, so it
        /// can never add a cell the corridor solver deliberately did not lay.
        /// </summary>
        private void FillJunctionPockets(BlockItem block, float cell)
        {
            int snapshot = _planPos.Count;
            if (snapshot == 0) return;
            _pocketSeen.Clear();

            for (int i = 0; i < snapshot; i++)
            {
                Vector3 pos = _planPos[i];
                Quaternion rot = _planRot[i];
                Vector3 r = rot * Vector3.right;
                Vector3 f = rot * Vector3.forward;
                Vector3 up = rot * Vector3.up;

                for (int sr = -1; sr <= 1; sr += 2)
                for (int sf = -1; sf <= 1; sf += 2)
                {
                    Vector3 diag = pos + (r * sr + f * sf) * cell;
                    if (IsCellOccupied(diag, cell)) continue;
                    long pocket = KeyOf(diag / Mathf.Max(0.25f, cell * 0.5f));
                    if (!_pocketSeen.Add(pocket)) continue;

                    // The two cells flanking the diagonal across the corner. Both paved means the
                    // slot is the pocket of a junction rather than a gap in open ground, and at
                    // least one of them must be pavement already in the world — otherwise this is
                    // the corridor's own geometry and the solver has already decided it.
                    bool sideA = _planSeen.Contains(KeyOf(pos + r * sr * cell));
                    bool sideB = _planSeen.Contains(KeyOf(pos + f * sf * cell));
                    bool worldA = IsCellOccupied(pos + r * sr * cell, cell);
                    bool worldB = IsCellOccupied(pos + f * sf * cell, cell);
                    if (!(sideA || worldA) || !(sideB || worldB)) continue;
                    if (!worldA && !worldB) continue;

                    if (!AsphaltRoad.ProbeGround(diag, up, out float off)) continue;
                    Vector3 dpos = diag + up * off;
                    var band = JudgeCell(block, dpos, rot, out int cost, out _);
                    if (band != AsphaltRoad.GradeBand.Smooth && band != AsphaltRoad.GradeBand.Rough) continue;
                    _totalCost += cost;
                    AddCell(dpos, rot, true, cost, Vector3.zero, Vector3.zero, 1f, 1f);
                }
            }
        }

        private void AddCell(Vector3 pos, Quaternion rot, bool fresh, int cost,
                             Vector3 inR, Vector3 outR, float inM, float outM,
                             Vector3 qSW = default, Vector3 qSE = default,
                             Vector3 qNE = default, Vector3 qNW = default,
                             bool explicitQuad = false)
        {
            // Two legs of a polyline share their corner cell; the seen-set is what stops the second
            // leg from laying (and charging for) a cell the first leg already put in the plan.
            long key = KeyOf(pos);
            if (!_planSeen.Add(key)) return;
            // The per-entry flag lists are appended HERE, only for a cell that actually enters the
            // plan, and never before the seen-set check above: appending first left a phantom
            // flag behind on every shared corner, shifting every later bridge and level flag one
            // cell off the plan entry it belonged to. A multi-leg line that crossed water after a
            // corner billed the wrong cells as deck, and a graded one would have moved the ground
            // under the wrong cells.
            _planPos.Add(pos); _planRot.Add(rot); _planFresh.Add(fresh); _planCost.Add(cost);
            _planInR.Add(inR); _planOutR.Add(outR); _planInM.Add(inM); _planOutM.Add(outM);
            _quadSW.Add(qSW); _quadSE.Add(qSE); _quadNE.Add(qNE); _quadNW.Add(qNW);
            _planExplicit.Add(explicitQuad);
            _planBridge.Add(false);
            _planLevel.Add(false);
        }

        private static long KeyOf(Vector3 pos)
            => (long)Mathf.Round(pos.x * 8) * 1000003L
             + (long)Mathf.Round(pos.y * 8) * 331L
             + (long)Mathf.Round(pos.z * 8);

        private void SetRefusal(string reason)
        {
            if (_refusal == null) _refusal = reason;
        }

        private void SetLiveRefusal(string reason)
        {
            if (_liveRefusal == null) _liveRefusal = reason;
        }

        private float CellOf(BlockItem block)
        {
            var tpl = block != null && block.placedPrefab != null
                ? block.placedPrefab.GetComponentInChildren<AsphaltRoad>(true) : null;
            return tpl != null ? Mathf.Max(0.1f, tpl.cellSize) : 4f;
        }

        /// <summary>Ctrl+remove: lift a whole placed section. The run IS the section, so this walks
        /// the run the aimed cell belongs to and lifts every cell in it.</summary>
        public bool RemoveRun(RaycastHit hit, RoadPaverTool tool, Inventory inventory, out int removed)
        {
            removed = 0;
            var road = RoadUnder(hit);
            if (road == null || road.Run == null) return false;

            var blocks = new List<AsphaltRoad>(road.Run.Blocks);
            bool refunded = road.Run.Wear01 <= tool.refundWearLimit
                            && tool.pavingMaterial != null && tool.refundPerCell > 0;

            for (int i = 0; i < blocks.Count; i++)
            {
                var cell = blocks[i];
                if (cell == null) continue;
                cell.DetachFromRun();
                UnityEngine.Object.Destroy(cell.gameObject);
                removed++;
            }

            if (removed > 0)
            {
                if (refunded) inventory.Add(tool.pavingMaterial, tool.refundPerCell * removed);
                VoxelEngine.UI.BuildFeedbackHud.Show("Road section removed",
                    removed + " cell(s)" + (refunded
                        ? " · +" + tool.refundPerCell * removed + " " + tool.pavingMaterial.displayName
                        : " · worn out, nothing recovered"),
                    null, refunded ? new Color(0.55f, 0.80f, 0.95f) : new Color(0.85f, 0.70f, 0.45f));
            }
            return removed > 0;
        }

        /// <summary>The road cell occupying this slot, or null. Reads the same spatial hash every
        /// other lookup in this file uses rather than scanning the scene: this runs once per already
        /// paved cell of a corridor, so a full `FindObjectsByType` here was O(cells x roads) on
        /// every frame a plan was open over existing pavement. It was also the last caller of the
        /// obsolete `FindObjectsSortMode` overload.</summary>
        private AsphaltRoad RoadAt(Vector3 pos, float cell)
        {
            VoxelEngine.Environment.RoadSurfaceUtility.QueryAt(pos, _roadAtScratch);
            float half = cell * 0.5f;
            for (int i = 0; i < _roadAtScratch.Count; i++)
            {
                var other = _roadAtScratch[i];
                if (other == null) continue;
                var t = other.transform;
                if (Vector3.Dot(t.forward, t.up) > 0.2f) continue;
                Vector3 lp = t.worldToLocalMatrix.MultiplyPoint3x4(pos);
                if (lp.x > -half && lp.x < half && lp.z > -half && lp.z < half &&
                    Mathf.Abs(lp.y) < Mathf.Max(0.5f, half))
                    return other;
            }
            return null;
        }

        private AsphaltRoad RoadUnder(RaycastHit hit)
        {
            var road = hit.collider != null ? hit.collider.GetComponentInParent<AsphaltRoad>() : null;
            if (road == null && hit.point != Vector3.zero) road = RoadAt(hit.point, 4f);
            return road;
        }

        /// <summary>Lifts one cell back. Refunds material only while the run is still in good
        /// condition; worn-out pavement is rubble, not stock.</summary>
        public bool TryScrape(RaycastHit hit, RoadPaverTool tool, Inventory inventory)
        {
            if (hit.collider == null || tool == null || inventory == null) return false;
            var road = hit.collider.GetComponentInParent<AsphaltRoad>();
            if (road == null)
            {
                // The registry returns everything sharing a hash cell, which is wider than a road
                // cell, so aim at the nearest one to the hit rather than the first in the list.
                RoadSurfaceUtility.QueryAt(hit.point, _anchorScratch);
                float bestSqr = float.MaxValue;
                for (int i = 0; i < _anchorScratch.Count; i++)
                {
                    var candidate = _anchorScratch[i];
                    if (candidate == null) continue;
                    float sqr = (candidate.transform.position - hit.point).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; road = candidate; }
                }
                if (road != null && bestSqr > 1.5f * 1.5f) road = null;
            }
            if (road == null) return false;

            float wear = road.Run?.Wear01 ?? 0f;
            bool refunded = wear <= tool.refundWearLimit && tool.pavingMaterial != null && tool.refundPerCell > 0;

            // DetachFromRun leaves the registry, releases the cell and re-splits whatever runs the
            // lifted cell was holding together, so the destroy below cannot strand a neighbour.
            road.DetachFromRun();
            Object.Destroy(road.gameObject);

            if (refunded) inventory.Add(tool.pavingMaterial, tool.refundPerCell);

            VoxelEngine.UI.BuildFeedbackHud.Show("Road lifted",
                refunded ? $"+{tool.refundPerCell} {tool.pavingMaterial.displayName}"
                         : "Worn out - no material recovered",
                null, refunded ? new Color(0.55f, 0.80f, 0.95f) : new Color(0.85f, 0.70f, 0.45f));
            return true;
        }

        // ════════════════════════════════════════════════════════
        //  GHOST — the whole planned corridor, drawn before a single unit is spent
        // ════════════════════════════════════════════════════════

        private static readonly Color GhostGood = new Color(0.30f, 0.85f, 0.55f, 0.35f);
        private static readonly Color GhostBad  = new Color(0.95f, 0.32f, 0.28f, 0.45f);

        private GameObject _ghost;
        private MeshFilter _ghostFilter;
        private MeshRenderer _ghostRenderer;
        private Material _ghostMat;
        private Mesh _ghostMesh;
        private readonly List<Vector3> _gPos = new List<Vector3>(512);
        private readonly List<Vector3> _gNrm = new List<Vector3>(512);
        private readonly List<Vector2> _gUv = new List<Vector2>(512);
        private readonly List<int> _gTri = new List<int>(768);

        private void EnsureGhost()
        {
            if (_ghost != null) return;
            _ghost = new GameObject("RoadGhost") { hideFlags = HideFlags.HideAndDontSave };
            _ghostFilter = _ghost.AddComponent<MeshFilter>();
            _ghostRenderer = _ghost.AddComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _ghostMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                // Never cull: the ghost must survive being looked at from under a ramp too.
                if (_ghostMat.HasProperty("_Cull")) _ghostMat.SetFloat("_Cull", 0f);
                _ghostMat.color = GhostGood;
                _ghostRenderer.material = _ghostMat;
            }
            _ghostMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = "RoadGhost" };
            _ghostFilter.sharedMesh = _ghostMesh;
            _ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ghostRenderer.receiveShadows = false;
            _ghost.SetActive(false);
        }

        private void ShowGhost(bool good)
        {
            EnsureGhost();
            if (_ghost == null) return;
            _gPos.Clear(); _gNrm.Clear(); _gUv.Clear(); _gTri.Clear();

            // The ghost is drawn from the LIVE corridor's own quads rather than from squares of the
            // committed plan, so what the player sees is the carriageway the cursor is describing —
            // fillets, lane fan and all — sitting proud of the ground the cells will be dropped on.
            var cells = _liveCorridor != null && _liveCorridor.cells.Count > 0
                ? _liveCorridor.cells : _commitCorridor.cells;
            if (cells.Count == 0) { HideGhost(); return; }

            for (int i = 0; i < cells.Count; i++)
            {
                var frame = cells[i];
                if (!AsphaltRoad.ProbeGround(frame.position, frame.rotation * Vector3.up, out float off))
                    off = 0f;
                Vector3 lift = frame.rotation * Vector3.up * (off + 0.09f);
                Vector3 n = frame.rotation * Vector3.up;
                int b = _gPos.Count;
                _gPos.Add(frame.position + frame.rotation * frame.sw + lift);
                _gPos.Add(frame.position + frame.rotation * frame.se + lift);
                _gPos.Add(frame.position + frame.rotation * frame.ne + lift);
                _gPos.Add(frame.position + frame.rotation * frame.nw + lift);
                for (int v = 0; v < 4; v++) _gNrm.Add(n);
                _gUv.Add(new Vector2(0f, 0f)); _gUv.Add(new Vector2(1f, 0f));
                _gUv.Add(new Vector2(1f, 1f)); _gUv.Add(new Vector2(0f, 1f));
                // Wound face-UP. The first draft's winding faced down and URP culls back faces, so
                // the ghost was invisible from exactly the angle the player looks at it from.
                _gTri.Add(b); _gTri.Add(b + 2); _gTri.Add(b + 1);
                _gTri.Add(b); _gTri.Add(b + 3); _gTri.Add(b + 2);
            }

            _ghostMesh.Clear();
            _ghostMesh.SetVertices(_gPos);
            _ghostMesh.SetNormals(_gNrm);
            _ghostMesh.SetUVs(0, _gUv);
            _ghostMesh.SetTriangles(_gTri, 0);
            _ghostMesh.RecalculateBounds();

            if (_ghostMat != null) _ghostMat.color = good ? GhostGood : GhostBad;
            _ghost.SetActive(true);
        }

        private void HideGhost()
        {
            if (_ghost != null) _ghost.SetActive(false);
        }
    }
}
