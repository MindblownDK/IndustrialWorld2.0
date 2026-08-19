// Assets/Scripts/VoxelEngine/Cosmos/SphereDensity.cs
//
// The radial density field for a spherical body.
//
// Everything is sampled in DIRECTION space (a point on the unit sphere) plus a RADIAL
// distance, so the same generator is correct at any planet radius. Continents, climate
// (temperature/humidity), biome blending and ore veins are all 3D on the sphere — they wrap
// seamlessly across the six cube faces with no seams (snoise is continuous on the sphere
// because we feed it the true normalised direction).
//
// Phase 3: LATITUDE-BASED CLIMATE (equator hot, poles cold → Earth-like biome distribution +
// polar ice caps), SLOPE-BASED ROCK (steep faces = stone, not grass-on-cliffs), and SNOW LINE
// (high altitude + cold = snow). These three make the planet read as Earth.
using Unity.Burst;
using Unity.Collections;
using Unity.Mathematics;
using VoxelEngine.Biomes;
using VoxelEngine.Core;
using VoxelEngine.Generation;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    [BurstCompile]
    public static class SphereDensity
    {
        // Climate noise scales in DIRECTION space (so they're radius-independent).
        private const float TempScale   = 1.7f;
        private const float HumidScale  = 2.1f;
        private const float TempOffset  = 47.3f;
        private const float HumidOffset = 91.7f;

        // Latitude blend: how strongly the equator/pole gradient dominates over noise.
        // 0 = pure noise (random climate), 1 = pure latitude (perfect bands). 0.65 = mostly
        // latitude with regional noise variation — looks like Earth.
        private const float LatitudeBlend = 0.55f;

        private static float SeedOffset(int seed, int channel)
        {
            uint h = (uint)(seed ^ (channel * 0x9E3779B9));
            h = (h ^ (h >> 16)) * 0x85EBCA6Bu;
            h = (h ^ (h >> 13)) * 0xC2B2AE35u;
            h = h ^ (h >> 16);
            return ((h & 0xFFFF) / 65535f - 0.5f) * 1000f;
        }

        /// <summary>
        /// Sample the planet's climate field at a surface direction.
        /// Returns temperature &amp; humidity in 0..1.
        ///
        /// Phase 3: temperature now blends a LATITUDE factor (equator = hot, poles = cold) with
        /// regional noise, so biomes form Earth-like climate zones: tropical near the equator,
        /// temperate mid-latitudes, tundra/ice near the poles. This is what makes the planet
        /// read as Earth rather than a random noise ball.
        /// </summary>
        public static float2 SampleClimate(int seed, in float3 dir)
        {
            float3 p = dir * 1f;
            float tNoise = noise.snoise(p * TempScale   + (SeedOffset(seed, 1) + TempOffset))  * 0.5f + 0.5f;
            float hNoise = noise.snoise(p * HumidScale  + (SeedOffset(seed, 2) + HumidOffset)) * 0.5f + 0.5f;

            // Latitude: |dir.y| = 0 at equator (hot), 1 at poles (cold).
            float lat = math.abs(dir.y);                          // 0 = equator, 1 = pole
            float tLat = math.saturate(1f - lat * 1.25f);         // equator ~1, poles ~0

            // Humidity latitude pattern (like Earth):
            //   equator (lat 0)   = wet (tropical rainforest)
            //   ~30° (lat 0.5)    = dry (desert belts)
            //   ~50° (lat 0.75)   = moderate-wet (temperate forest)
            //   poles (lat 1)     = dry (tundra)
            float hLat = math.cos(lat * 3.0f) * 0.3f + 0.55f;     // oscillating: wet-dry-wet-dry
            hLat = math.saturate(hLat);

            // Blend latitude climate with regional noise.
            float t = math.lerp(tNoise, tLat, LatitudeBlend);
            float h = math.lerp(hNoise, hLat, LatitudeBlend * 0.7f);

            return new float2(t, h);
        }

        /// <summary>
        /// A planet's "land mask" — 0 = deep ocean basin, 1 = continental interior. Drives the
        /// continent/ocean split that makes the planet read as Earth (large landmasses, big
        /// oceans, with continental shelves between).
        /// </summary>
        public static float LandMask(int seed, in float3 dir, float continentScaleDir)
        {
            // Apply frequency to the unit direction before the seed translation. This keeps
            // continental scale stable on authored-size planets instead of collapsing into one
            // nearly flat land/ocean band as radius grows.
            float scale = math.max(0.25f, continentScaleDir);
            float3 p = dir * scale + SeedOffset(seed, 3);
            float coarse = noise.snoise(p)             * 0.5f + 0.5f;
            float shape  = noise.snoise(p * 2.3f + 13f) * 0.5f + 0.5f;
            float land   = coarse * 0.72f + shape * 0.28f;
            return math.saturate(math.smoothstep(0.34f, 0.62f, land));
        }

        /// <summary>
        /// Estimate the local terrain slope (0 = flat, 1 = vertical cliff) by comparing the
        /// surface radius at `dir` vs a small angular offset. Used to apply ROCK on steep faces
        /// (no grass growing on cliffs) — a key realism detail.
        /// </summary>
        public static float EstimateSlope(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in float3 dir)
        {
            // Sample the surface height at dir and at two perpendicular offsets.
            EvaluateColumn(prm, biomes, dir, out float h0, out _);

            // Build two perpendicular tangent directions on the sphere.
            float3 refVec = math.abs(dir.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0);
            float3 tangent1 = math.normalizesafe(math.cross(dir, refVec), new float3(1, 0, 0));
            float3 tangent2 = math.normalizesafe(math.cross(dir, tangent1), new float3(0, 0, 1));

            // Sample a stable physical span instead of a fixed large angular offset. Full-size
            // planets otherwise interpret 0.02 radians as a huge distance and report artificial
            // cliff slopes everywhere.
            float angularStep = math.clamp(16f / math.max(1f, prm.radiusWorld), 0.0005f, 0.006f);
            float3 d1 = math.normalizesafe(dir + tangent1 * angularStep, dir);
            float3 d2 = math.normalizesafe(dir + tangent2 * angularStep, dir);

            EvaluateColumn(prm, biomes, d1, out float h1, out _);
            EvaluateColumn(prm, biomes, d2, out float h2, out _);

            // Height difference → slope estimate. Large diff = steep.
            float dh = (math.abs(h1 - h0) + math.abs(h2 - h0)) * 0.5f;
            return math.saturate(dh / 8f);  // 8m height change over ~1° ≈ steep
        }

        /// <summary>
        /// Score a biome against the local climate (euclidean distance in climate space,
        /// biased by priority). Mirrors the flat-world BiomePicker so authored biomes port over.
        /// </summary>
        public static float Score(in BiomeData b, float2 climate)
        {
            float tCenter = (b.tempRange.x + b.tempRange.y) * 0.5f;
            float tHalf   = math.max(0.001f, (b.tempRange.y - b.tempRange.x) * 0.5f);
            float tDist   = (climate.x - tCenter) / tHalf;

            float hCenter = (b.humidRange.x + b.humidRange.y) * 0.5f;
            float hHalf   = math.max(0.001f, (b.humidRange.y - b.humidRange.x) * 0.5f);
            float hDist   = (climate.y - hCenter) / hHalf;

            float d = math.sqrt(tDist * tDist + hDist * hDist);
            return (1f - d) + b.priority * 0.05f;
        }

        /// <summary>
        /// Evaluate the surface radius (metres from core) and dominant biome index for a
        /// direction. 9.0.0: the terrain SHAPE comes from <see cref="VoxelEngine.GpuVoxel.PlanetField"/> —
        /// the exact field the GPU quadtree surface evaluates — so the gameplay bubble,
        /// scatter, waterfalls, sky proxies and ocean cut-outs all agree with the rendered
        /// planet at every LOD. Biomes still drive materials, scatter and climate.
        /// </summary>
        public static void EvaluateColumn(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in float3 dir,
            out float surfaceRadius,
            out int  biomeIndex)
        {
            float2 climate = SampleClimate(prm.seed, dir);

            // Dominant biome (materials / scatter / climate flavour only).
            float bestScore = -1e9f;
            biomeIndex = 0;
            for (int i = 0; i < biomes.Length; i++)
            {
                float s = Score(biomes[i], climate);
                if (s > bestScore) { bestScore = s; biomeIndex = i; }
            }

            // Terrain shape — the unified 9.0.0 planetary field (GPU/CPU lockstep).
            surfaceRadius = VoxelEngine.GpuVoxel.PlanetField.SurfaceRadius(
                prm.seed, dir, prm.radiusWorld, prm.baseHeight, prm.seaRadius,
                prm.continentScaleDir, prm.mountainScale);
        }

        // ────────────────────────────────────────────────────────────────
        // CHUNK-COLUMN CACHING (7.20.0, gradient-corrected 8.0.2)
        // ────────────────────────────────────────────────────────────────
        // Evaluating the FULL column (climate + landmask + biome softmax + tectonic
        // ridges + slope probe ≈ 10–30 snoise calls) once per VOXEL was the dominant
        // generation cost. The column is now evaluated once per CHUNK (≈5 column
        // samples) plus a first-order gradient correction per voxel, so chunk
        // generation stays ~10–30× faster while the surface follows the true
        // spherical terrain across the whole chunk — LOD chunks (64 m–16 km) are
        // NOT flat slabs. The per-voxel work that remains (density, caves, surface
        // material bands, ores, oil, water) is unchanged.

        /// <summary>
        /// One precomputed surface column shared by every voxel of a chunk, PLUS a
        /// first-order gradient correction so the surface stays correct ACROSS the
        /// whole chunk (8.0.2). A single centre-only column made LOD chunks render as
        /// flat slabs at the chunk-centre height — stacked layers with gaps on the
        /// planet. The gradient restores the true spherical/terrain-following surface:
        ///
        ///     surfaceRadius(dir) ≈ surfaceRadius(centerDir)
        ///                        + dot(surfaceGrad, dir − centerDir)
        ///
        /// Burst-blittable POD — safe to pass into jobs.
        /// </summary>
        public struct ChunkColumn
        {
            /// <summary>Terrain surface radius (m from core) at the chunk's direction.</summary>
            public float surfaceRadius;
            /// <summary>Dominant biome index into the biome array.</summary>
            public int   biomeIndex;
            /// <summary>Climate (temperature, humidity) at the chunk's direction.</summary>
            public float2 climate;
            /// <summary>1 = the chunk's local slope is a genuine cliff (rock surface).</summary>
            public byte  slopeRock;
            /// <summary>Unit direction the column was evaluated at (chunk centre).</summary>
            public float3 centerDir;
            /// <summary>∂surfaceRadius/∂dir (m per unit direction) — linear correction.</summary>
            public float3 surfaceGrad;
            /// <summary>Continent mask at the chunk centre (0 = ocean, 1 = land) —
            /// gates generated sea water (9.7.4: no more hidden water lenses under
            /// beaches and inland dips that merely poke below the sea shell).</summary>
            public float landMask;
        }

        /// <summary>
        /// Evaluate the full surface column ONCE for a whole chunk (direction = the
        /// chunk's radial centre), including a 7-sample gradient so the shared column
        /// stays accurate across the chunk's whole footprint. Slope probing is done
        /// once here too — the old code re-sampled two offset columns per voxel,
        /// which dominated generation cost.
        /// </summary>
        public static ChunkColumn EvaluateChunkColumn(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in float3 dir)
        {
            ChunkColumn col;
            EvaluateColumn(prm, biomes, dir, out float surfaceRadius, out int biomeI);
            col.surfaceRadius = surfaceRadius;
            col.biomeIndex    = biomeI;
            col.climate       = SampleClimate(prm.seed, dir);
            col.slopeRock      = 0;
            col.centerDir      = dir;
            col.surfaceGrad    = float3.zero;
            col.landMask       = VoxelEngine.GpuVoxel.PlanetField.LandMask01(
                prm.seed, dir, prm.continentScaleDir);

            // Tangent basis on the sphere at the chunk centre.
            float3 refVec = math.abs(dir.y) < 0.9f ? new float3(0, 1, 0) : new float3(1, 0, 0);
            float3 t1 = math.normalizesafe(math.cross(dir, refVec), new float3(1, 0, 0));
            float3 t2 = math.normalizesafe(math.cross(dir, t1), new float3(0, 0, 1));

            // Slope probe — identical maths to the original per-voxel cliff test
            // (physical ~12 m span), run once per chunk.
            float slopeStep = math.clamp(12f / math.max(1f, prm.radiusWorld), 0.0005f, 0.005f);
            EvaluateColumn(prm, biomes, math.normalizesafe(dir + t1 * slopeStep, dir), out float s1, out _);
            EvaluateColumn(prm, biomes, math.normalizesafe(dir + t2 * slopeStep, dir), out float s2, out _);
            float heightDiff = math.max(math.abs(s1 - surfaceRadius), math.abs(s2 - surfaceRadius));
            col.slopeRock = heightDiff > 10f ? (byte)1 : (byte)0;

            // Gradient probe — central differences over a wider (~36 m) baseline so
            // the linear correction captures real terrain slopes across the chunk.
            float gradStep = math.clamp(36f / math.max(1f, prm.radiusWorld), 0.0015f, 0.012f);
            EvaluateColumn(prm, biomes, math.normalizesafe(dir + t1 * gradStep, dir), out float g1p, out _);
            EvaluateColumn(prm, biomes, math.normalizesafe(dir - t1 * gradStep, dir), out float g1m, out _);
            EvaluateColumn(prm, biomes, math.normalizesafe(dir + t2 * gradStep, dir), out float g2p, out _);
            EvaluateColumn(prm, biomes, math.normalizesafe(dir - t2 * gradStep, dir), out float g2m, out _);
            float d1 = (g1p - g1m) / (2f * gradStep);
            float d2 = (g2p - g2m) / (2f * gradStep);
            col.surfaceGrad = t1 * d1 + t2 * d2;
            return col;
        }

        /// <summary>
        /// Full per-voxel evaluation (no oil map — the gameplay world path).
        /// Returns the voxel (density byte + material + water level) for a body-relative
        /// cartesian position.
        /// </summary>
        public static Voxel EvaluateVoxel(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in NativeArray<OreLayer> ores,
            in float3 worldPos)
            => EvaluateVoxel(prm, biomes, ores, worldPos, default);

        /// <summary>
        /// Full per-voxel evaluation with an optional oil-site map (LOD levels).
        /// Returns the voxel (density byte + material + water level) for a body-relative
        /// cartesian position. Equivalent to computing the column at the voxel's own
        /// direction and delegating to <see cref="EvaluateVoxelCached"/> — kept for
        /// preview/authoring callers; chunk generation uses the cached path.
        /// </summary>
        public static Voxel EvaluateVoxel(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in NativeArray<OreLayer> ores,
            in float3 worldPos,
            in NativeParallelHashMap<int, OilSiteData> oilSites)
        {
            if (prm.isAsteroidBelt == 1)
                return EvaluateAsteroidVoxel(prm, worldPos);

            float3 dir = math.normalizesafe(worldPos, new float3(1f, 0f, 0f));
            return EvaluateVoxelCached(prm, biomes, ores, worldPos,
                EvaluateChunkColumn(prm, biomes, dir), oilSites);
        }

        /// <summary>
        /// Per-voxel evaluation against a precomputed <see cref="ChunkColumn"/> — the
        /// fast path used by <see cref="SphereChunkGenJob"/> (one column per chunk).
        /// </summary>
        public static Voxel EvaluateVoxelCached(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in NativeArray<OreLayer> ores,
            in float3 worldPos,
            in ChunkColumn column,
            in NativeParallelHashMap<int, OilSiteData> oilSites)
        {
            if (prm.isAsteroidBelt == 1)
                return EvaluateAsteroidVoxel(prm, worldPos);

            float radius = math.length(worldPos);
            float3 dir   = math.normalizesafe(worldPos, new float3(1f, 0f, 0f));

            // Legacy first-order surface correction (8.0.2). NOTE: the gameplay world no
            // longer uses this path — the linear extrapolation breaks on the 9.x ridged
            // field (whole chunks generated hollow or filled = the "gaps on generation"
            // and "hole through the planet" reports). SphereChunkGenJob now supplies an
            // exact per-voxel surface radius via its surface LATTICE and calls
            // EvaluateVoxelWithSurface below. This wrapper remains for probe callers.
            float3 dDir = dir - column.centerDir;
            float surfaceRadius = column.surfaceRadius
                + column.surfaceGrad.x * dDir.x
                + column.surfaceGrad.y * dDir.y
                + column.surfaceGrad.z * dDir.z;

            return EvaluateVoxelWithSurface(prm, biomes, ores, worldPos, surfaceRadius, column, oilSites);
        }

        /// <summary>
        /// Per-voxel evaluation against an EXACT (or lattice-interpolated) surface
        /// radius (9.5.0) — the artifact-free path used by SphereChunkGenJob. Biome,
        /// climate and slope flavour still come from the shared chunk column.
        /// </summary>
        public static Voxel EvaluateVoxelWithSurface(
            in SphereGenParams prm,
            in NativeArray<BiomeData> biomes,
            in NativeArray<OreLayer> ores,
            in float3 worldPos,
            float surfaceRadius,
            in ChunkColumn column,
            in NativeParallelHashMap<int, OilSiteData> oilSites)
        {
            if (prm.isAsteroidBelt == 1)
                return EvaluateAsteroidVoxel(prm, worldPos);

            float radius = math.length(worldPos);
            float3 dir   = math.normalizesafe(worldPos, new float3(1f, 0f, 0f));
            var biome = biomes[column.biomeIndex];

            float density = surfaceRadius - radius;

            float coreRadius = prm.radiusWorld * 0.55f;
            if (radius <= coreRadius)
                return new Voxel(127, (byte)MaterialId.Stone, 0);

            int depth = (int)math.floor(surfaceRadius - radius);

            // Caves come from the SAME carve function the GPU surface evaluates
            // (PlanetField.CaveCarve) — sealed beneath a protective crust and never
            // under the sea, so the bubble's caves match the rendered planet exactly.
            int protectedSurfaceCrust = math.max(8, biome.surfaceDepth + biome.subsurfaceDepth + 2);
            if (radius > coreRadius + 6f)
            {
                density -= VoxelEngine.GpuVoxel.PlanetField.CaveCarve(
                    worldPos, surfaceRadius, radius, prm.seaRadius, protectedSurfaceCrust);
            }

            if (density > 0f)
            {
                byte material = (byte)MaterialId.Stone;

                // ── Surface material selection (Phase 3: slope + snow + beach) ──
                float altitudeAboveSea = surfaceRadius - prm.seaRadius; // metres above sea level
                float2 climate = column.climate;                    // cached per chunk (smooth fields)
                bool atSurface = depth < biome.surfaceDepth;

                // Beach band: ONLY right at the waterline (±1m, top 2 voxels). The old band was
                // ±2.5m × 4 deep which covered the entire surface when terrain sat near sea level.
                if (biome.allowBeach == 1 &&
                    radius >= prm.seaRadius - 1f && radius <= prm.seaRadius + 1f && depth < 2)
                {
                    material = (byte)MaterialId.Sand;
                }
                else if (depth < biome.surfaceDepth)
                {
                    material = biome.surfaceMat;
                }
                else if (depth < biome.surfaceDepth + biome.subsurfaceDepth)
                {
                    material = biome.subsurfaceMat;
                }

                // ── SNOW LINE: high altitude + cold climate = snow/ice surface ──
                // Realistic snow caps on mountains and polar regions.
                if (atSurface && altitudeAboveSea > 35f && climate.x < 0.25f)
                {
                    material = (byte)MaterialId.Ice;
                }
                // Polar ice: very cold regions near poles get ice even at low altitude.
                else if (atSurface && climate.x < 0.1f)
                {
                    material = (byte)MaterialId.Ice;
                }

                // ── SLOPE-BASED ROCK: steep terrain = stone, no grass on cliffs ──
                // The slope probe itself is evaluated ONCE per chunk (ChunkColumn);
                // here we only apply it inside the same depth/altitude band the old
                // per-voxel test used, so gentle hills keep their grass.
                if (column.slopeRock == 1 &&
                    depth < biome.surfaceDepth + biome.subsurfaceDepth && altitudeAboveSea > 5f)
                {
                    material = (byte)MaterialId.Stone;
                }

                // ── Ore veins ──
                if (depth >= biome.surfaceDepth + 1)
                {
                    for (int i = 0; i < ores.Length; i++)
                    {
                        var ore = ores[i];
                        if (depth < ore.minDepth || depth > ore.maxDepth) continue;
                        float rich = VeinNoise.DepositRichness(
                            worldPos, 1f / ore.scale, ore.threshold, 1f, (uint)(prm.seed + i * 31));
                        if (rich > 0.5f)
                        {
                            material = (byte)ore.material;
                            break;
                        }
                    }
                }

                // ── Oil sites (LOD levels) ──
                // The gameplay world authors oil with OilReservoirDecorator; the LOD
                // levels reproduce the same puddle → bore → reservoir story from the
                // precomputed site map so oil fields are visible from orbit and from
                // the air. Only probed near the surface (map lookup is cheap).
                if (prm.hasOilSeeps == 1 && oilSites.IsCreated && oilSites.Count() > 0 && depth < 90f)
                {
                    byte oilMaterial = OilSiteSampler.Sample(oilSites, worldPos, depth, dir);
                    if (oilMaterial != 0) material = oilMaterial;
                }

                // Scale physical distance (metres) by 32 into signed-byte density units so
                // SurfaceNetsJob's zero-crossing interpolation (t = da / (da - db)) operates
                // with sub-voxel precision. Unscaled density clamped to ±1 caused every surface
                // edge to interpolate at t=0.5, creating stepped contour rings on gentle slopes.
                int scaledDensity = (int)math.round(density * 32f);
                sbyte densityByte = (sbyte)math.clamp(scaledDensity, 1, 127);
                return new Voxel(densityByte, material, 0);
            }
            else
            {
                // Only true ocean basins receive generated water. A cave excavated below the
                // mathematical sea shell on otherwise dry land must remain air: players should
                // encounter water only in oceans, intentional lakes, or placed/pumped liquid.
                bool genuineOcean = column.landMask < 0.45f || surfaceRadius < prm.seaRadius - 6f;
                if (genuineOcean && surfaceRadius < prm.seaRadius - 1f && radius <= prm.seaRadius)
                {
                    // Crude oil is authored separately as one coherent surface seep, tapered
                    // funnel, and deep reservoir — never as random submerged noise patches.
                    return new Voxel(-5, (byte)MaterialId.WaterLiquid, 255);
                }
                int scaledDensity = (int)math.round(density * 32f);
                sbyte densityByte = (sbyte)math.clamp(scaledDensity, -127, -1);
                return new Voxel(densityByte, (byte)MaterialId.Air, 0);
            }
        }

        /// <summary>
        /// Roadmap Era 4 Asteroid Belt: zero-gravity scattered procedural voxel asteroids
        /// spawning rarely in 3D space everywhere around the player (no surface/shell).
        /// Fully per-voxel — belt rocks have no coherent column to cache.
        /// </summary>
        private static Voxel EvaluateAsteroidVoxel(
            in SphereGenParams prm,
            in float3 worldPos)
        {
            float3 p = worldPos * 0.038f;
            float n1 = noise.snoise(p + SeedOffset(prm.seed, 1));
            float n2 = noise.snoise(p * 2.3f + SeedOffset(prm.seed, 2)) * 0.45f;
            float n3 = noise.snoise(p * 5.1f + SeedOffset(prm.seed, 3)) * 0.20f;
            float rockNoise = (n1 + n2 + n3);

            float densityAst = (rockNoise - 0.44f) * 40f;

            if (densityAst > 0f)
            {
                byte material = (byte)MaterialId.Stone;
                float oreChoice = noise.snoise(worldPos * 0.11f + SeedOffset(prm.seed, 4));
                if (oreChoice > 0.52f) material = (byte)MaterialId.Platinum;
                else if (oreChoice > 0.30f) material = (byte)MaterialId.Cobalt;
                else if (oreChoice > 0.10f) material = (byte)MaterialId.Gold;
                else if (oreChoice > -0.15f) material = (byte)MaterialId.Iron;
                else if (oreChoice > -0.40f) material = (byte)MaterialId.Silicon;
                else if (oreChoice > -0.65f) material = (byte)MaterialId.Ice;

                int scaledD = (int)math.round(densityAst * 32f);
                return new Voxel((sbyte)math.clamp(scaledD, 1, 127), material, 0);
            }
            else
            {
                int scaledD = (int)math.round(densityAst * 32f);
                return new Voxel((sbyte)math.clamp(scaledD, -127, -1), (byte)MaterialId.Air, 0);
            }
        }
    }
}
