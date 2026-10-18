// Assets/Scripts/VoxelEngine/Weather/WeatherFronts.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║      PER-REGION WEATHER — THE FRONT FIELD (17.31.0-dev)          ║
// ║                                                                  ║
// ║  Before this file, weather was one number for a whole world: the  ║
// ║  state machine rolled Clear → Overcast → Rain → HeavyRain and     ║
// ║  every square metre of the planet received the same intensity at  ║
// ║  the same moment. The map said so out loud ("Weather sampled at   ║
// ║  the host's frame"), and the honest consequence was that a base   ║
// ║  3 km from another base could never be under different skies.     ║
// ║                                                                  ║
// ║  What this file adds is PLACE: the state machine still decides    ║
// ║  what the weather SYSTEM over the body is doing (a clear spell,   ║
// ║  a rain band, a storm front), and this field decides WHERE that   ║
// ║  system is actually depositing. A body in a rain phase is not     ║
// ║  uniformly wet: it carries bands of rain with clear gaps between  ║
// ║  them, they drift, and the player can walk out of one.            ║
// ║                                                                  ║
// ║  The field is a pure, deterministic function of                   ║
// ║  (body seed, surface direction, front clock), so it needs no      ║
// ║  per-region simulation, no bookkeeping, no save data and no       ║
// ║  wire traffic beyond the clock itself — every machine, host or    ║
// ║  guest, computes exactly the same sky over the same square metre  ║
// ║  if it knows the clock. Any system can ask about ANY position     ║
// ║  (a solar panel asks about the sky above itself), which a grid    ║
// ║  of simulated regions could not answer without keeping the whole  ║
// ║  planet awake.                                                   ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Weather
{
    /// <summary>
    /// The spatial half of the weather: one drifting field of rain bands over a body,
    /// evaluated as a pure function of a surface direction and the front clock.
    /// No state, no allocation, no per-region bookkeeping.
    /// </summary>
    public static class WeatherFronts
    {
        // ── Band geometry ────────────────────────────────────────────────────────
        /// <summary>A band's physical size scales with the body: a quarter of its radius,
        /// so an 8 km world carries ~2 km bands and a small moon carries small ones.</summary>
        public const float BandFractionOfRadius = 0.25f;
        public const float MinBandMetres = 500f;
        public const float MaxBandMetres = 2400f;

        // ── Drift ────────────────────────────────────────────────────────────────
        /// <summary>How fast the whole pattern travels over the surface. Measured against the
        /// simulation this drifts a fixed base in and out of a band about nine times an hour -
        /// a band cycle of roughly six minutes - so the rain is something that arrives, visits
        /// and leaves, rather than a coin flip per frame.</summary>
        public const float DriftMetresPerSecond = 6f;

        /// <summary>Fixed travel direction in the noise domain (normalised on use). One
        /// direction for every body keeps the fronts readable and predictable; the body
        /// seed still decides where the bands actually sit.</summary>
        public static readonly Vector3 DriftDirection = new Vector3(0.86f, 0.19f, -0.47f).normalized;

        // ── Field shape ──────────────────────────────────────────────────────────
        /// <summary>Two octaves: one broad band structure, one finer texture on top.</summary>
        public const float BaseOctaveWeight = 0.65f;
        public const float DetailOctaveWeight = 0.35f;
        public const float DetailFrequency = 2.7f;

        /// <summary>Half-width of the band edge in field units - measured against the
        /// simulation, roughly 280 m of ground on an 8 km world. Inside (bias - width) a place is
        /// fully under the system; outside (bias + width) it is fully clear of it; between the two
        /// the sky fades, so a band edge is something a player WALKS THROUGH over about half a
        /// minute at a run instead of a line to cross.</summary>
        public const float BandEdgeHalfWidth = 0.07f;

        // ── Coverage bias ────────────────────────────────────────────────────────
        /// <summary>Fraction of the field that counts as "under the system", before the
        /// body's own gloominess adjusts it. A gloomier profile (higher overcastBias) gets
        /// wider bands, so a permanently overcast world is not secretly dry.</summary>
        public const float MinCoverageBias = 0.40f;
        public const float MaxCoverageBias = 0.62f;

        /// <summary>Band size for a body of this surface radius, in metres.</summary>
        public static float BandMetresFor(float surfaceRadius)
            => Mathf.Clamp(Mathf.Max(50f, surfaceRadius) * BandFractionOfRadius,
                           MinBandMetres, MaxBandMetres);

        /// <summary>Threshold the field is compared against, from the body's own overcast bias.</summary>
        public static float CoverageBiasFor(float overcastBias01)
            => Mathf.Lerp(MinCoverageBias, MaxCoverageBias, Mathf.Clamp01(overcastBias01));

        // ── Words ────────────────────────────────────────────────────────────────
        /// <summary>
        /// 17.32.0-dev - the three places a player can be relative to a band, named once so the
        /// map, the machine card and any later surface cannot say three different things about
        /// the same square metre. A place is UNDER a band when the system is essentially fully
        /// over it, AT THE EDGE while the fade is still blending, and CLEAR when the nearest
        /// band is a walk away.
        /// </summary>
        public static string CoverageWord(float coverage01)
        {
            if (coverage01 >= 0.85f) return "UNDER THE BAND";
            if (coverage01 >= 0.20f) return "AT THE BAND EDGE";
            return "CLEAR OF THE BAND";
        }

        /// <summary>
        /// 17.34.0-dev - a multiplier expressed as the whole percent change a player reads,
        /// e.g. 1.18 -> +18, 0.81 -> -19, 1.00 -> 0. Lives here with the other shared weather
        /// vocabulary so the terrain response, the climate response and every surface that
        /// prints them round the same way.
        /// </summary>
        public static int PercentChange(float multiplier) => Mathf.RoundToInt((multiplier - 1f) * 100f);

        /// <summary>
        /// How much of the weather system is over a place, 0..1. 0 means this square metre is
        /// in one of the gaps between bands and gets nothing; 1 means it is fully inside one.
        /// Callers multiply their precipitation by this, so a clear spell stays clear
        /// everywhere (there is no system to be under) while a rain phase becomes patches.
        /// </summary>
        /// <param name="up">Surface direction at the place being asked about (world up is
        /// what points away from the body, so it doubles as "which piece of the surface").</param>
        /// <param name="surfaceRadius">The body's mean surface radius in metres.</param>
        /// <param name="bandMetres">Band size from <see cref="BandMetresFor"/>.</param>
        /// <param name="seed">The body's own seed, so two worlds never share a sky.</param>
        /// <param name="clockSeconds">Front clock. Every caller that shares a clock shares a sky.</param>
        /// <param name="bias01">Threshold from <see cref="CoverageBiasFor"/>.</param>
        public static float Coverage01(Vector3 up, float surfaceRadius, float bandMetres,
            int seed, float clockSeconds, float bias01)
        {
            if (up.sqrMagnitude < 1e-6f) return 0f;
            up.Normalize();

            float band = Mathf.Max(100f, bandMetres);
            float domainScale = 1f / band;

            // The pattern itself travels; a place's own coordinates never move.
            Vector3 drift = DriftDirection * (DriftMetresPerSecond * clockSeconds * domainScale);
            Vector3 p = up * (Mathf.Max(50f, surfaceRadius) * domainScale) + drift;

            float sample = Value3(p, seed) * BaseOctaveWeight
                + Value3(p * DetailFrequency + new Vector3(17.3f, -9.1f, 5.7f), seed ^ 0x5f3a)
                  * DetailOctaveWeight;

            float bias = Mathf.Clamp01(bias01);
            return Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(bias - BandEdgeHalfWidth, bias + BandEdgeHalfWidth, sample));
        }

        /// <summary>
        /// Seeded 3D value noise with a quintic fade, trilinear between lattice corners.
        /// Written here rather than taken from Unity so every host and guest produces the
        /// same band from the same inputs, on any platform and any noise version.
        /// </summary>
        private static float Value3(Vector3 p, int seed)
        {
            int x0 = Mathf.FloorToInt(p.x);
            int y0 = Mathf.FloorToInt(p.y);
            int z0 = Mathf.FloorToInt(p.z);
            float fx = Fade(p.x - x0);
            float fy = Fade(p.y - y0);
            float fz = Fade(p.z - z0);
            int x1 = x0 + 1, y1 = y0 + 1, z1 = z0 + 1;

            float c000 = Hash01(x0, y0, z0, seed);
            float c100 = Hash01(x1, y0, z0, seed);
            float c010 = Hash01(x0, y1, z0, seed);
            float c110 = Hash01(x1, y1, z0, seed);
            float c001 = Hash01(x0, y0, z1, seed);
            float c101 = Hash01(x1, y0, z1, seed);
            float c011 = Hash01(x0, y1, z1, seed);
            float c111 = Hash01(x1, y1, z1, seed);

            float x00 = Mathf.Lerp(c000, c100, fx);
            float x10 = Mathf.Lerp(c010, c110, fx);
            float x01 = Mathf.Lerp(c001, c101, fx);
            float x11 = Mathf.Lerp(c011, c111, fx);
            float y0Lerp = Mathf.Lerp(x00, x10, fy);
            float y1Lerp = Mathf.Lerp(x01, x11, fy);
            return Mathf.Lerp(y0Lerp, y1Lerp, fz);
        }

        /// <summary>Perlin's quintic fade — continuous first and second derivatives, so the
        /// band edges have no visible crease as a player walks through them.</summary>
        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        /// <summary>Deterministic 0..1 hash of a lattice corner and a seed.</summary>
        private static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 1274126177 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 2246822519u;
                h ^= h >> 16;
                h *= 3266489917u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
