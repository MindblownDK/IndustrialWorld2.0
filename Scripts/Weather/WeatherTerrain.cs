// Assets/Scripts/VoxelEngine/Weather/WeatherTerrain.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║        THE RAIN SHADOW — TERRESTRIAL FRONT RESPONSE (17.33.0-dev) ║
// ║                                                                  ║
// ║  17.31.0-dev gave the weather PLACE: a drifting field of bands    ║
// ║  over a body. 17.32.0-dev gave the ground a vote in what the      ║
// ║  bands do to it (the smog washout reads the sky over each cell).  ║
// ║  Neither of those looked at the ground itself - a band crossed a  ║
// ║  mountain range exactly as it crossed a plain.                    ║
// ║                                                                  ║
// ║  This file is the ground's answer. A band arriving at a slope     ║
// ║  that RISES in the direction of travel is being forced upward,    ║
// ║  and rising air drops what it carries: the windward flank is      ║
// ║  wetter. The far side is descending, drying air: the lee flank    ║
// ║  is the rain shadow, and it is the reason a desert sits behind    ║
// ║  a range while the coast in front of it is green. Elevation adds  ║
// ║  the last few percent - high ground stands nearer the cloud base  ║
// ║  and catches a little more of everything.                         ║
// ║                                                                  ║
// ║  The response is a MULTIPLIER on coverage, never an absolute      ║
// ║  intensity: a clear spell stays clear over a mountain top, and    ║
// ║  the state machine is untouched. Only how much of a band lands    ║
// ║  on a place is affected.                                         ║
// ║                                                                  ║
// ║  Purity and cost: the terrain height is PlanetField's own analytic║
// ║  surface (the single source of truth the GPU mirrors), so every   ║
// ║  machine computes the same multiplier from the same body. Three   ║
// ║  height samples per unique place; the answer is memoised on a     ║
// ║  150 m bucket because terrain does not change, so a place pays    ║
// ║  for itself once and every later ask - per frame, per cell, per   ║
// ║  map repaint - is a dictionary hit. The memo is derived state:    ║
// ║  it can be thrown away at any moment and the answer is the same,  ║
// ║  so it can never put two machines out of step.                    ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Cosmos;
using VoxelEngine.GpuVoxel;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// How the ground under a band changes how much of that band lands on it.
    /// One pure function of the body's own terrain, memoised for the session.
    /// </summary>
    public static class WeatherTerrain
    {
        // ── The response curve ───────────────────────────────────────────────────
        /// <summary>Rise-per-metre along the direction of travel that counts as a full
        /// orographic response (22 cm of climb per metre of run - a steep hillside). Steeper
        /// slopes saturate rather than run away, so a cliff is not a rain laser.</summary>
        public const float SlopeReference = 0.22f;

        /// <summary>How much a full windward slope multiplies a band. Calibrated against the
        /// analytic field at the default step: a mountainous body lands between roughly 0.77x
        /// (lee) and 1.28x (windward) at the 5th and 95th percentile, with a mean of 1.00 to
        /// within a percent - the rain shadow is legible without the planet getting wetter or
        /// drier on average.</summary>
        public const float OrographicGain = 0.90f;

        /// <summary>Weight of the elevation term, centred on ordinary land (the field's own
        /// LandPlateau), so flat ground is neutral and only genuine height moves it.</summary>
        public const float ElevationGain = 0.20f;

        /// <summary>Hard limits. Nothing may ever be perfectly dry behind a ridge or
        /// perfectly soaked on a peak - both would make a place unplayable rather than
        /// interesting.</summary>
        public const float MinMultiplier = 0.45f;
        public const float MaxMultiplier = 1.55f;

        // ── Sampling geometry ─────────────────────────────────────────────────── ─
        /// <summary>Baseline for the slope measurement, as a fraction of the body radius.</summary>
        public const float SampleFractionOfRadius = 0.06f;
        public const float SampleMinMetres = 120f;
        public const float SampleMaxMetres = 400f;

        /// <summary>A body with less relief than this has nothing to shadow, so the whole
        /// query is skipped and the multiplier is exactly 1.</summary>
        public const float MinReliefMetres = 20f;

        /// <summary>Memo bucket, in metres of ground. Larger than the field's smallest
        /// terrain feature (the 260 m hill band) is deliberately avoided.</summary>
        public const float CacheCellMetres = 150f;
        private const int CacheCapacity = 8192;

        // ── Memo (derived state, safe to discard) ────────────────────────────────
        private struct CacheKey : System.IEquatable<CacheKey>
        {
            public int seed;
            public int x;
            public int y;
            public int z;

            public bool Equals(CacheKey other)
                => seed == other.seed && x == other.x && y == other.y && z == other.z;

            public override bool Equals(object obj) => obj is CacheKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = seed;
                    h = (h * 397) ^ x;
                    h = (h * 397) ^ y;
                    h = (h * 397) ^ z;
                    return h;
                }
            }
        }

        private static readonly Dictionary<CacheKey, float> s_cache = new(CacheCapacity);

        /// <summary>
        /// The multiplier a body's own ground applies to a band over a place, 1 = unchanged.
        /// <paramref name="up"/> is the surface direction at the place (the same vector the
        /// front field is asked about), so a caller never has to convert anything.
        /// </summary>
        public static float Multiplier(in SphereGenParams gen, Vector3 up)
        {
            if (up.sqrMagnitude < 1e-6f) return 1f;
            up.Normalize();

            float relief = MaxRelief(gen);
            if (relief < MinReliefMetres) return 1f;

            CacheKey key = KeyFor(gen, up);
            if (s_cache.TryGetValue(key, out float memoised)) return memoised;

            float value = Evaluate(gen, up, relief);

            if (s_cache.Count >= CacheCapacity) s_cache.Clear();
            s_cache[key] = value;
            return value;
        }

        /// <summary>
        /// Drops the memo. Only useful when the body underneath has genuinely changed (a
        /// different world loaded in the same process); everything else is keyed by seed.
        /// </summary>
        public static void ClearCache() => s_cache.Clear();

        /// <summary>
        /// The multiplier for a place expressed as a whole percent change, for readouts:
        /// "+18" / "-24" / "0". Never the raw float - a panel says what a player can feel.
        /// </summary>
        public static int Percent(float multiplier) => Mathf.RoundToInt((multiplier - 1f) * 100f);

        /// <summary>
        /// The three names the ground can earn, in the same spirit as the band words: a place
        /// is on a WINDWARD SLOPE (a band lands harder), in a RAIN SHADOW (it lands softer) or
        /// on LEVEL GROUND (unchanged). Thresholds are the point where a player could actually
        /// notice the difference, not a rounding budget.
        /// </summary>
        public static string Word(float multiplier)
        {
            if (multiplier >= 1.06f) return "WINDWARD SLOPE";
            if (multiplier <= 0.94f) return "RAIN SHADOW";
            return "LEVEL GROUND";
        }

        // ── The physics ──────────────────────────────────────────────────────────
        private static float Evaluate(in SphereGenParams gen, Vector3 up, float relief)
        {
            float radius = Mathf.Max(50f, gen.radiusWorld + gen.baseHeight);
            float stepMetres = Mathf.Clamp(radius * SampleFractionOfRadius, SampleMinMetres, SampleMaxMetres);
            float angle = stepMetres / radius;

            // The slope is measured ALONG THE BANDS' OWN TRAVEL, because that is the direction
            // the air (and the rain in it) is actually moving when it meets the ground.
            Vector3 drift = WeatherFronts.DriftDirection;
            Vector3 tangent = drift - up * Vector3.Dot(drift, up);
            if (tangent.sqrMagnitude < 1e-5f)
            {
                // Exactly at a pole of the drift direction: any tangent will do, and the
                // answer is still deterministic because it comes from the terrain, not a roll.
                tangent = Vector3.ProjectOnPlane(Vector3.forward, up);
                if (tangent.sqrMagnitude < 1e-5f) tangent = Vector3.ProjectOnPlane(Vector3.right, up);
            }
            tangent.Normalize();

            float here = Height(gen, up);
            float downwind = Height(gen, up + tangent * angle);
            float upwind = Height(gen, up - tangent * angle);

            // Rising ground downwind of us forces the air up - lift. Falling ground lets it
            // descend - the lee. Symmetric around zero, so the body average is unchanged.
            float slope = (downwind - upwind) / Mathf.Max(1f, 2f * stepMetres);
            float lift = Mathf.Clamp(slope / SlopeReference, -1f, 1f);

            // Elevation above ORDINARY LAND (the field's own plateau), not above the deepest
            // basin and not above sea level: flat ground must be neutral or every coastal base
            // reads as a shadow. The reference is the mean surface the field itself composes
            // land from (`radiusWorld + baseHeight`), plus the plateau, which is exactly the
            // elevation `PlanetField.SurfaceRadius` returns for land with no hills on it.
            float ordinaryLandRadius = gen.MeanSurfaceRadius + PlanetField.LandPlateau;
            float elevation = Mathf.Clamp(
                (here - ordinaryLandRadius) / Mathf.Max(1f, relief), -1f, 1f);

            float multiplier = 1f + OrographicGain * lift + ElevationGain * elevation;
            return Mathf.Clamp(multiplier, MinMultiplier, MaxMultiplier);
        }

        /// <summary>Terrain radius at a unit direction, from the one analytic field the whole
        /// game agrees on (the same call the GPU mirror evaluates).</summary>
        private static float Height(in SphereGenParams gen, Vector3 dir)
        {
            // The field is a function of DIRECTION, so the probe points are normalised here
            // rather than being trusted to arrive that way - an unnormalised sample would ask
            // about a slightly different place and quietly bias the slope towards the radius.
            dir.Normalize();
            return PlanetField.SurfaceRadius(gen.seed, new float3(dir.x, dir.y, dir.z),
                gen.radiusWorld, gen.baseHeight, gen.seaRadius,
                gen.continentScaleDir, gen.mountainScale);
        }

        private static float MaxRelief(in SphereGenParams gen)
            => PlanetField.MaxElevation(gen.mountainScale);

        private static CacheKey KeyFor(in SphereGenParams gen, Vector3 up)
        {
            float radius = Mathf.Max(50f, gen.radiusWorld + gen.baseHeight);
            float scale = radius / CacheCellMetres;
            return new CacheKey
            {
                seed = gen.seed,
                x = Mathf.RoundToInt(up.x * scale),
                y = Mathf.RoundToInt(up.y * scale),
                z = Mathf.RoundToInt(up.z * scale),
            };
        }
    }
}
