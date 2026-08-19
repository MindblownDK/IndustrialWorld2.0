// Assets/Scripts/VoxelEngine/Generation/OilReservoirDecorator.cs
//
// Oil-rich spherical-body crude-oil geology. Every generated site is one readable feature:
// a surface seep/puddle, a narrow radial bore, and a real underground reservoir.
//
// Finite sites appear only on setup-authorized oil-rich bodies. A much rarer
// Pirate World-only subset receives PirateOilNode identity and is the only kind
// that an infinite Jack Pump can use. No flat-world path exists here.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Core;
using VoxelEngine.Cosmos;
using VoxelEngine.Materials;
using VoxelEngine.WaterSim;

namespace VoxelEngine.Generation
{
    public static class OilReservoirDecorator
    {
        // A 96 m Cartesian cell gives finite seeps enough breathing room without making
        // an oil-rich body feel saturated. The per-cell hashes include the body seed.
        private const int SiteCellSize = 96;
        private const int MaxSurfaceProbeSteps = 192;
        private const int ExteriorAirProbeSteps = 12;
        private const int PendingRetryBudgetPerChunk = 2;
        private const int MaxNewSitesPerChunk = 1;
        private const int PendingRetryDelayFrames = 12;
        private const int MaxInvalidPendingAttempts = 3;

        private enum SiteBuildResult
        {
            Created,
            WaitingForLoadedChunks,
            Invalid
        }

        private struct SiteCandidate
        {
            public Vector3Int anchor;
            public Vector3Int cell;
            public bool infinite;
        }

        private struct PendingReservoir
        {
            public SphereWorld world;
            public Vector3Int anchor;
            public Vector3Int cell;
            public bool infinite;
            public int invalidAttempts;
            public int nextRetryFrame;
        }

        // A world owns its own claims: two scene instances must never suppress one another
        // merely because they happen to stream the same body-local coordinates.
        private static readonly Dictionary<SphereWorld, HashSet<Vector3Int>> s_claimedSiteCells = new();
        private static readonly List<PendingReservoir> s_pending = new(32);
        private static readonly Dictionary<SphereWorld, int> s_loggedSiteCounts = new();

        /// <summary>
        /// Called by SphereWorld after a chunk is available. It intentionally accepts only
        /// spherical, setup-authorized oil-rich bodies; old flat generation cannot reach it.
        /// </summary>
        public static void Decorate(Chunk chunk, IVoxelWorld world)
        {
            if (chunk == null || world is not SphereWorld sphere || !chunk.isGenerated
                || sphere.body == null || sphere.body.settings == null
                || !sphere.body.settings.CanGenerateFiniteCrudeOilSeeps) return;

            RetryPending(sphere);
            // A chunk can straddle several 96 m cells. Process a small bounded number so a
            // finite seep in the first cell cannot hide a valid rare node in a neighbouring one.
            for (int processed = 0; processed < MaxNewSitesPerChunk; processed++)
            {
                if (!TryFindSiteCandidate(chunk, sphere, out SiteCandidate candidate)) break;
                CreateOrQueue(candidate, sphere);
            }
        }

        /// <summary>Retries deferred site construction while the spherical world streams.</summary>
        public static void Tick(SphereWorld world)
        {
            if (world == null || world.body == null || world.body.settings == null
                || !world.body.settings.CanGenerateFiniteCrudeOilSeeps) return;
            RetryPending(world);
        }

        /// <summary>Release static generation bookkeeping when a spherical world is destroyed.</summary>
        public static void ForgetWorld(SphereWorld world)
        {
            if (world == null) return;
            s_claimedSiteCells.Remove(world);
            s_loggedSiteCounts.Remove(world);
            for (int i = s_pending.Count - 1; i >= 0; i--)
                if (s_pending[i].world == world) s_pending.RemoveAt(i);
        }

        /// <summary>
        /// Finds a true exterior solid point rather than depending on a randomly hit raw-oil
        /// voxel. This makes sites deterministic, discoverable, and able to retrofit streamed
        /// legacy chunks that were saved before crude markers were enabled.
        /// </summary>
        private static bool TryFindSiteCandidate(Chunk chunk, SphereWorld world, out SiteCandidate candidate)
        {
            candidate = default;
            const int size = VoxelConstants.CHUNK_SIZE;
            int baseX = chunk.coord.x * size;
            int baseY = chunk.coord.y * size;
            int baseZ = chunk.coord.z * size;
            int seed = world.body.genParams.seed;
            float finiteChance = world.body.settings.ResolveCrudeOilSiteChance();
            bool mayCreateInfiniteNode = world.body.settings.CanGenerateInfiniteJackPumpNodes;
            float infiniteChance = mayCreateInfiniteNode
                ? world.body.settings.ResolveInfiniteOilNodeChance()
                : 0f;

            // Chunks are small (32³); scanning their interior once on generation is cheaper and
            // more reliable than waiting for a particular underground ore-pixel to be selected.
            // Geological cells are 96 m wide, while a streamed chunk is 32 m. A two-metre
            // candidate lattice is more than enough to find an exposed local surface but avoids
            // evaluating costly radial terrain columns for every one of 27k solid voxels.
            const int CandidateStride = 2;
            for (int z = 1; z < size - 1; z += CandidateStride)
            for (int y = 1; y < size - 1; y += CandidateStride)
            for (int x = 1; x < size - 1; x += CandidateStride)
            {
                Voxel voxel = chunk.GetVoxelLocal(x, y, z);
                if (!voxel.IsSolid) continue;

                Vector3Int anchor = new(baseX + x, baseY + y, baseZ + z);
                Vector3 up = GetUpDir(anchor);
                Vector3Int outer = anchor + Vector3Int.RoundToInt(up);
                bool outerLoaded = TryGetLoadedVoxel(world, outer, out Voxel outerVoxel);
                // Most candidates are deep interior and can be rejected with one cheap voxel
                // lookup before the exact sampled-surface test.
                if (outerLoaded && outerVoxel.IsSolid) continue;
                // Never let a cave wall or deep interior voxel nominate an oil site. The same
                // sampled radial surface used by terrain/scatter must own the candidate.
                if (!world.IsNearSampledTerrainSurface(anchor, 3f)) continue;
                // A surface chunk can finish one frame before the adjacent air chunk. Queue
                // only anchors close to the expected spherical surface in that case; otherwise
                // deep interior chunks would claim unrelated radial cells before they can see
                // the actual exterior boundary.
                if (!outerLoaded && !IsNearPotentialSurface(world, anchor)) continue;

                Vector3Int cell = ToSiteCell(anchor);
                if (IsClaimed(world, cell) || HasPending(world, cell)) continue;

                // An infinite result always produces a visible site, even when the ordinary
                // seep roll failed. Only the canonical Pirate World is allowed to make this
                // rare Jack Pump identity; all other oil-rich bodies remain finite.
                bool infinite = mayCreateInfiniteNode
                    && ShouldCreate(cell, seed, infiniteChance, 0xB5297A4Du);
                if (!infinite && !ShouldCreate(cell, seed, finiteChance, 0x68E31DA4u))
                    continue;

                candidate = new SiteCandidate { anchor = anchor, cell = cell, infinite = infinite };
                return true;
            }

            return false;
        }

        private static void CreateOrQueue(SiteCandidate candidate, SphereWorld world)
        {
            SiteBuildResult result = TryCreateReservoir(world, candidate.anchor, candidate.cell, candidate.infinite);
            if (result == SiteBuildResult.Created)
            {
                Claim(world, candidate.cell);
                return;
            }

            if (result == SiteBuildResult.WaitingForLoadedChunks)
            {
                QueuePending(candidate, world);
                return;
            }

            // A malformed/too-short radial probe is deterministic. Claim it so one bad cell
            // cannot repeatedly consume generator time; neighbouring valid cells remain eligible.
            Claim(world, candidate.cell);
        }

        private static Vector3Int ToSiteCell(Vector3Int voxel)
            => new(
                Mathf.FloorToInt(voxel.x / (float)SiteCellSize),
                Mathf.FloorToInt(voxel.y / (float)SiteCellSize),
                Mathf.FloorToInt(voxel.z / (float)SiteCellSize));

        private static bool ShouldCreate(Vector3Int cell, int seed, float chance, uint salt)
        {
            chance = Mathf.Clamp01(chance);
            if (chance <= 0f) return false;
            return Hash01(cell, seed, salt) <= chance;
        }

        private static float Hash01(Vector3Int cell, int seed, uint salt)
        {
            unchecked
            {
                uint h = (uint)cell.x * 73856093u;
                h ^= (uint)cell.y * 19349663u;
                h ^= (uint)cell.z * 83492791u;
                h ^= (uint)seed * 2654435761u;
                h ^= salt;
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h & 0x00FFFFFFu) / 16777215f;
            }
        }

        private static SiteBuildResult TryCreateReservoir(
            SphereWorld world, Vector3Int anchor, Vector3Int cell, bool infinite)
        {
            Vector3 up = GetUpDir(anchor);
            if (!TryFindExposedSurface(world, anchor, up, out Vector3Int surface, out bool waitingForChunks))
                return waitingForChunks ? SiteBuildResult.WaitingForLoadedChunks : SiteBuildResult.Invalid;

            unchecked
            {
                int hash = anchor.x * 73856093 ^ anchor.y * 19349663 ^ anchor.z * 83492791 ^ world.body.genParams.seed;
                var random = new System.Random(hash);

                // Finite crude seeps are intentionally compact. The very rare Jack Pump site is
                // visibly broader and deeper, making puddle → bore → reservoir readable in play.
                int puddleRadius = infinite ? 4 + random.Next(2) : 2 + random.Next(2);
                int reservoirRadius = infinite ? 7 + random.Next(3) : 4 + random.Next(2);
                int reservoirDepth = infinite ? 42 + random.Next(22) : 24 + random.Next(15);
                Vector3 reservoirCenter = (Vector3)surface - up * reservoirDepth;
                Vector3 reservoirTop = reservoirCenter + up * Mathf.Max(1f, reservoirRadius * 0.55f);

                var touched = new HashSet<Chunk>();
                bool allWritten = BuildSurfacePuddle(world, surface, up, puddleRadius, touched);
                // The bore + reservoir are written as SOLID oil-soaked rock (density +127) so
                // the terrain mesher renders them VISIBLE (liquid oil sealed inside rock was
                // invisible — only fluid-at-air surfaces render). The surface puddle stays a
                // real liquid for pumps.
                allWritten &= BuildRadialFunnel(world, surface, reservoirTop, up, Mathf.Max(2, puddleRadius - 1), 2, touched);
                allWritten &= BuildReservoir(world, Vector3Int.RoundToInt(reservoirCenter), reservoirRadius, touched);
                FlushTouchedChunks(world, touched);

                // If any write was skipped because a chunk below the surface wasn't generated
                // yet (fast streaming), re-run later — the writes are idempotent, so retrying
                // the whole build is safe and simply fills in the missing bore/reservoir.
                if (!allWritten)
                    return SiteBuildResult.WaitingForLoadedChunks;

                if (infinite)
                    PirateOilNode.Ensure(world, surface, Vector3Int.RoundToInt(reservoirCenter));

                LogCreatedSite(world, surface, cell, infinite);
            }

            return SiteBuildResult.Created;
        }

        private static Vector3 GetUpDir(Vector3Int voxel)
        {
            Vector3 radial = ((Vector3)voxel).normalized;
            return radial.sqrMagnitude > 0.0001f ? radial : Vector3.up;
        }

        private static bool IsNearPotentialSurface(SphereWorld world, Vector3Int voxel)
        {
            if (world == null || world.body == null) return false;
            float radius = ((Vector3)voxel).magnitude * VoxelConstants.VOXEL_SIZE;
            // Ocean floors are about 25 m below the sea shell; leave a little terrain margin
            // while excluding the much deeper streamed interior.
            return radius >= world.body.SeaRadius - 32f && radius <= world.body.SurfaceRadius + 32f;
        }

        /// <summary>
        /// Finds an actual exterior boundary. On an ocean it traverses from the sea floor through
        /// water to the water/air boundary, so a site never becomes a hidden dark patch below sea.
        /// </summary>
        private static bool TryFindExposedSurface(SphereWorld world, Vector3Int start, Vector3 up,
            out Vector3Int surface, out bool waitingForChunks)
        {
            surface = default;
            waitingForChunks = false;
            Vector3 origin = start;

            for (int step = 0; step < MaxSurfaceProbeSteps; step++)
            {
                Vector3Int currentPos = Vector3Int.RoundToInt(origin + up * step);
                Vector3Int nextPos = Vector3Int.RoundToInt(origin + up * (step + 1));
                if (!TryGetLoadedVoxel(world, currentPos, out Voxel current)
                    || !TryGetLoadedVoxel(world, nextPos, out Voxel next))
                {
                    waitingForChunks = true;
                    return false;
                }

                bool currentFluid = FluidMaterialUtility.IsFluid(current);
                bool nextFluid = FluidMaterialUtility.IsFluid(next);

                if (current.IsSolid && !next.IsSolid && !nextFluid)
                {
                    if (HasOpenExterior(world, nextPos, up, out bool exteriorWaiting))
                    {
                        surface = currentPos;
                        return true;
                    }
                    if (exteriorWaiting)
                    {
                        waitingForChunks = true;
                        return false;
                    }
                }

                if (currentFluid && !nextFluid && !next.IsSolid)
                {
                    if (HasOpenExterior(world, nextPos, up, out bool fluidExteriorWaiting))
                    {
                        surface = currentPos;
                        return true;
                    }
                    if (fluidExteriorWaiting)
                    {
                        waitingForChunks = true;
                        return false;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reject a cave wall or a short internal air pocket as a false "surface". The radial
        /// path must remain clear for a few cells beyond the air boundary before a seep can be
        /// placed, while missing streamed chunks remain a retryable condition.
        /// </summary>
        private static bool HasOpenExterior(SphereWorld world, Vector3 firstAir, Vector3 up, out bool waitingForChunks)
        {
            waitingForChunks = false;
            for (int step = 0; step < ExteriorAirProbeSteps; step++)
            {
                Vector3Int probe = Vector3Int.RoundToInt(firstAir + up * step);
                if (!TryGetLoadedVoxel(world, probe, out Voxel voxel))
                {
                    waitingForChunks = true;
                    return false;
                }
                if (voxel.IsSolid || FluidMaterialUtility.IsFluid(voxel)) return false;
            }
            return true;
        }

        private static bool BuildSurfacePuddle(SphereWorld world, Vector3Int surface, Vector3 up,
            int radius, HashSet<Chunk> touched)
        {
            bool allWritten = true;
            GetTangentBasis(up, out Vector3 tangentA, out Vector3 tangentB);
            for (int a = -radius; a <= radius; a++)
            for (int b = -radius; b <= radius; b++)
            {
                if (a * a + b * b > radius * radius) continue;
                Vector3 probe = surface + tangentA * a + tangentB * b;
                // Follow each point back to its own local radial terrain surface. The old
                // fixed-radius disk cut across slopes and made oil appear as random scattered
                // cells instead of a puddle that sits naturally on the land.
                if (!TryResolvePuddleSurface(world, probe, out Vector3Int localSurface)) continue;
                Vector3 localUp = GetUpDir(localSurface);
                // Preserve the terrain cell underneath the visible puddle. Oil lives in the
                // exterior air/fluid cell, so the player sees a pooled surface rather than a
                // carved hole with no collision floor.
                allWritten &= WriteSurfaceOil(world, localSurface, localUp, touched);

                // 9.7.0 — a seep is a SMALL LAKE, not a film: interior cells sink one
                // voxel (the top solid converts to liquid oil), giving the pool real
                // depth with an intact rim and floor.
                if (a * a + b * b <= (radius - 1) * (radius - 1))
                {
                    Vector3Int digPos = localSurface - Vector3Int.RoundToInt(localUp);
                    if (TryGetLoadedVoxel(world, digPos, out Voxel digVox) && digVox.IsSolid)
                    {
                        var oilVox = new Voxel(-5, (byte)VoxelEngine.Materials.MaterialId.CrudeOil, 255);
                        world.SetVoxelWorld(digPos, oilVox, remesh: false);
                        if (world.TryGetChunk(new Vector3Int(
                                Mathf.FloorToInt(digPos.x / (float)VoxelConstants.CHUNK_SIZE),
                                Mathf.FloorToInt(digPos.y / (float)VoxelConstants.CHUNK_SIZE),
                                Mathf.FloorToInt(digPos.z / (float)VoxelConstants.CHUNK_SIZE)), out var digChunk))
                            touched.Add(digChunk);
                    }
                }
            }
            return allWritten;
        }

        private static bool TryResolvePuddleSurface(SphereWorld world, Vector3 probe, out Vector3Int surface)
        {
            surface = default;
            Vector3 radialUp = probe.sqrMagnitude > 0.0001f ? probe.normalized : Vector3.up;
            // Search from just outside the expected seep plane inward. This finds the actual
            // local hillside/ocean surface for every puddle cell and never writes into air.
            for (int offset = 10; offset >= -24; offset--)
            {
                Vector3Int candidate = Vector3Int.RoundToInt(probe + radialUp * offset);
                if (!TryGetLoadedVoxel(world, candidate, out Voxel voxel)) return false;
                if (!voxel.IsSolid && !FluidMaterialUtility.IsFluid(voxel)) continue;
                if (TryFindExposedSurface(world, candidate, radialUp, out surface, out _)) return true;
            }
            return false;
        }

        /// <summary>
        /// Builds a tapered geological funnel from right below the surface puddle down to
        /// the top of the deep reservoir, connecting puddle -> funnel -> reservoir cleanly.
        /// Writes SOLID oil-soaked rock so the shaft is visible in the terrain.
        /// </summary>
        private static bool BuildRadialFunnel(SphereWorld world, Vector3Int surface, Vector3 reservoirTop,
            Vector3 up, int mouthRadius, int throatRadius, HashSet<Chunk> touched)
        {
            bool allWritten = true;
            Vector3 start = surface - up * 1f;
            Vector3 direction = reservoirTop - start;
            int steps = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude));
            int effectiveMouth = Mathf.Max(2, mouthRadius);
            int effectiveThroat = Mathf.Max(2, throatRadius);   // ≥2 so the shaft reads as a real bore

            for (int step = 1; step <= steps; step++)
            {
                float progress = step / (float)steps;
                Vector3 center = Vector3.Lerp(start, reservoirTop, progress);
                float currentRadius = Mathf.Lerp(effectiveMouth, effectiveThroat, progress);
                int rInt = Mathf.Max(1, Mathf.RoundToInt(currentRadius));
                int r2 = rInt * rInt;

                GetTangentBasis(up, out Vector3 tangentA, out Vector3 tangentB);
                for (int a = -rInt; a <= rInt; a++)
                for (int b = -rInt; b <= rInt; b++)
                {
                    if (a * a + b * b > r2) continue;
                    Vector3Int pos = Vector3Int.RoundToInt(center + tangentA * a + tangentB * b);
                    allWritten &= WriteSolidOil(world, pos, touched);
                }
            }
            return allWritten;
        }

        private static bool BuildReservoir(SphereWorld world, Vector3Int center, int radius, HashSet<Chunk> touched)
        {
            bool allWritten = true;
            int radiusSquared = radius * radius;
            for (int z = -radius; z <= radius; z++)
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y + z * z > radiusSquared) continue;
                allWritten &= WriteSolidOil(world, center + new Vector3Int(x, y, z), touched);
            }
            return allWritten;
        }

        private static void GetTangentBasis(Vector3 up, out Vector3 tangentA, out Vector3 tangentB)
        {
            Vector3 reference = Mathf.Abs(Vector3.Dot(up, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right;
            tangentA = Vector3.Cross(reference, up).normalized;
            tangentB = Vector3.Cross(up, tangentA).normalized;
        }

        private static bool IsVoxelChunkLoaded(SphereWorld world, Vector3Int voxel)
        {
            if (world == null) return false;
            const int size = VoxelConstants.CHUNK_SIZE;
            Vector3Int coord = new(
                Mathf.FloorToInt(voxel.x / (float)size),
                Mathf.FloorToInt(voxel.y / (float)size),
                Mathf.FloorToInt(voxel.z / (float)size));
            return world.TryGetChunk(coord, out Chunk chunk) && chunk != null && chunk.isGenerated;
        }

        private static bool TryGetLoadedVoxel(SphereWorld world, Vector3Int voxel, out Voxel value)
        {
            if (!IsVoxelChunkLoaded(world, voxel))
            {
                value = default;
                return false;
            }
            value = world.GetVoxelWorld(voxel);
            return true;
        }

        private static bool WriteSurfaceOil(SphereWorld world, Vector3Int surface, Vector3 up, HashSet<Chunk> touched)
        {
            if (world == null) return true;
            if (TryGetLoadedVoxel(world, surface, out Voxel current) && FluidMaterialUtility.IsFluid(current))
            {
                // At a genuine ocean surface, replace only the top fluid cell with crude.
                return WriteOil(world, surface, touched);
            }

            Vector3Int exterior = Vector3Int.RoundToInt((Vector3)surface + up);
            if (!TryGetLoadedVoxel(world, exterior, out Voxel above)) return false;
            if (above.IsSolid) return true;
            return WriteOil(world, exterior, touched);
        }

        /// <summary>Liquid crude for the visible surface puddle (rendered by the fluid mesh).</summary>
        private static bool WriteOil(SphereWorld world, Vector3Int voxel, HashSet<Chunk> touched)
        {
            if (!TryGetChunkForWrite(world, voxel, out Chunk chunk)) return false;
            world.SetVoxelWorld(voxel, new Voxel(-1, (byte)MaterialId.CrudeOil, 255), remesh: false);
            touched.Add(chunk);
            return true;
        }

        /// <summary>
        /// SOLID oil-soaked rock for the bore + reservoir — the terrain mesher renders it
        /// visibly (dark oil), so the shaft down to the reservoir is real and seeable.
        /// </summary>
        private static bool WriteSolidOil(SphereWorld world, Vector3Int voxel, HashSet<Chunk> touched)
        {
            if (!TryGetChunkForWrite(world, voxel, out Chunk chunk)) return false;
            // 9.7.1 — the bore/reservoir RECOLOUR existing rock, never create it:
            // writing density 127 into open air made funnel disks poke out of
            // hillsides as floating black slabs (the "tarp over the dunes"). The
            // original density is preserved, so the terrain silhouette is untouched —
            // the oil vein simply shows where it genuinely meets the surface and is
            // discovered by mining, exactly as intended.
            if (!TryGetLoadedVoxel(world, voxel, out Voxel existing)) return false;
            if (!existing.IsSolid) return true;   // air/liquid/cave — nothing to soak
            if (existing.material == (byte)MaterialId.CrudeOil) return true;
            world.SetVoxelWorld(voxel, new Voxel(existing.density, (byte)MaterialId.CrudeOil, 0), remesh: false);
            touched.Add(chunk);
            return true;
        }

        private static bool TryGetChunkForWrite(SphereWorld world, Vector3Int voxel, out Chunk chunk)
        {
            chunk = null;
            const int size = VoxelConstants.CHUNK_SIZE;
            Vector3Int coord = new(
                Mathf.FloorToInt(voxel.x / (float)size),
                Mathf.FloorToInt(voxel.y / (float)size),
                Mathf.FloorToInt(voxel.z / (float)size));
            if (!world.TryGetChunk(coord, out chunk) || chunk == null || !chunk.isGenerated) return false;
            return true;
        }

        private static void FlushTouchedChunks(SphereWorld world, HashSet<Chunk> touched)
        {
            foreach (Chunk chunk in touched)
            {
                if (chunk == null || !chunk.isGenerated) continue;
                world.ScheduleMeshJob(chunk);
                FluidManager.Instance?.MarkActive(chunk.coord);
                WaterMeshBuilder.Schedule(chunk);
            }
        }

        private static bool IsClaimed(SphereWorld world, Vector3Int cell)
            => s_claimedSiteCells.TryGetValue(world, out HashSet<Vector3Int> cells) && cells.Contains(cell);

        private static void Claim(SphereWorld world, Vector3Int cell)
        {
            if (!s_claimedSiteCells.TryGetValue(world, out HashSet<Vector3Int> cells))
            {
                cells = new HashSet<Vector3Int>();
                s_claimedSiteCells.Add(world, cells);
            }
            cells.Add(cell);
        }

        private static bool HasPending(SphereWorld world, Vector3Int cell)
        {
            for (int i = 0; i < s_pending.Count; i++)
                if (s_pending[i].world == world && s_pending[i].cell == cell) return true;
            return false;
        }

        private static void QueuePending(SiteCandidate candidate, SphereWorld world)
        {
            if (HasPending(world, candidate.cell)) return;
            s_pending.Add(new PendingReservoir
            {
                world = world,
                anchor = candidate.anchor,
                cell = candidate.cell,
                infinite = candidate.infinite,
                nextRetryFrame = Time.frameCount + PendingRetryDelayFrames
            });
        }

        private static void RetryPending(SphereWorld world)
        {
            int processed = 0;
            for (int i = s_pending.Count - 1; i >= 0 && processed < PendingRetryBudgetPerChunk; i--)
            {
                PendingReservoir pending = s_pending[i];
                if (pending.world != world) continue;
                // If streaming evicted the source chunk, drop this deferred entry. Re-entering
                // the area re-discovers the deterministic cell and queues a fresh safe probe.
                if (!IsVoxelChunkLoaded(world, pending.anchor))
                {
                    s_pending.RemoveAt(i);
                    continue;
                }
                if (pending.nextRetryFrame > Time.frameCount) continue;
                if (IsClaimed(world, pending.cell))
                {
                    s_pending.RemoveAt(i);
                    continue;
                }

                processed++;
                SiteBuildResult result = TryCreateReservoir(world, pending.anchor, pending.cell, pending.infinite);
                if (result == SiteBuildResult.Created)
                {
                    Claim(world, pending.cell);
                    s_pending.RemoveAt(i);
                    continue;
                }

                if (result == SiteBuildResult.WaitingForLoadedChunks)
                {
                    pending.nextRetryFrame = Time.frameCount + PendingRetryDelayFrames;
                    s_pending[i] = pending;
                    continue;
                }

                pending.invalidAttempts++;
                if (pending.invalidAttempts >= MaxInvalidPendingAttempts)
                {
                    Claim(world, pending.cell);
                    s_pending.RemoveAt(i);
                }
                else
                {
                    pending.nextRetryFrame = Time.frameCount + PendingRetryDelayFrames;
                    s_pending[i] = pending;
                }
            }
        }

        private static void LogCreatedSite(SphereWorld world, Vector3Int surface, Vector3Int cell, bool infinite)
        {
            s_loggedSiteCounts.TryGetValue(world, out int count);
            if (!infinite && count >= 6) return;
            s_loggedSiteCounts[world] = count + 1;
            string kind = infinite ? "RARE INFINITE Pirate Jack Pump" : "finite crude";
            Debug.Log($"[CrudeOil] Created {kind} site on '{world.body.DisplayName}' at local voxel {surface} (cell {cell}).");
        }
    }
}
