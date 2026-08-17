// Assets/Scripts/VoxelEngine/Cosmos/CosmicRegistry.cs
//
// Runtime model of the active solar system: the star, all planets, their moons and
// sub-moons, and the asteroid fields — propagated with REAL Keplerian orbital
// mechanics in double precision.
//
//   • Every body carries classical orbital elements (a, e, i, Ω, ω, M0) around its
//     parent (planets around the sun, moons around planets, sub-moons around moons).
//   • Positions and velocities are computed from the elements every frame via
//     OrbitMath (Kepler's equation + vis-viva), so orbits are genuinely elliptical,
//     periods follow T = 2π√(a³/μ), and orbital velocities are physically correct.
//   • All positions are stored in KILOMETRES as double3 (cosmic space). The scene
//     bridge is SpaceOrigin, which places the bodies in scene units relative to a
//     floating origin. `positionKm` (Vector3) remains as a legacy convenience view.
//
// The registry also answers the physics queries of the real-space simulation:
// N-body gravity acceleration, the dominant (frame) body at any point, and the
// co-moving frame velocity — consumed by GravityProvider and SpaceOrigin.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Materials;
using Random = Unity.Mathematics.Random;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Layout + dynamics of the whole solar system. [DefaultExecutionOrder(-60)]
    /// so the layout exists before SpaceOrigin (-1000 is earlier — registry guards
    /// on IsReady) and before any body consumer.
    /// </summary>
    [DefaultExecutionOrder(-60)]
    public class CosmicRegistry : MonoBehaviour
    {
        public static CosmicRegistry Instance { get; private set; }

        [Header("System")]
        [Tooltip("Solar system to instantiate. If null, the registry stays idle.")]
        public SolarSystemTemplate systemTemplate;

        [Tooltip("World seed. If left 0, taken from WorldSession (randomised once at world creation).")]
        public int worldSeed = 0;

        [Header("Orbital Dynamics")]
        [Tooltip("Multiplier on simulation time for ALL orbital motion (1 = nominal Keplerian speeds). " +
                 "Raise to make planets visibly progress along their orbits during a session.")]
        public double orbitalTimeScale = 1d;

        [Header("Cosmic → Scene bridge")]
        [Tooltip("Scene units per km. Kept for compatibility; SpaceOrigin owns the real bridge.")]
        public float WorldUnitsPerKm = 1f;

        // ── Runtime graph ─────────────────────────────────────────
        public SunInstance Sun { get; private set; }
        public IReadOnlyList<BodyInstance> Bodies => _bodies;
        public IReadOnlyList<AsteroidInstance> Asteroids => _asteroids;
        private readonly List<BodyInstance> _bodies = new List<BodyInstance>();
        private readonly List<AsteroidInstance> _asteroids = new List<AsteroidInstance>();
        public bool IsReady { get; private set; }

        /// <summary>Simulation clock (seconds since world creation). Drives every orbit.</summary>
        public double SimulationSeconds { get; private set; }

        /// <summary>
        /// Scene CelestialBody components, keyed by their sim instance. Populated by the
        /// body factory (CosmosBootstrap) and consumed by SpaceOrigin for scene placement.
        /// </summary>
        public readonly Dictionary<BodyInstance, CelestialBody> SceneBodies = new Dictionary<BodyInstance, CelestialBody>();

        private const double DefaultSunMuKm3S2 = 180d;

        // Minimum gap between sibling moon rings (km) so they can never intersect.
        private const double MinMoonGapKm = 40d;

        // Minimum gap between consecutive planet orbits (km). Enforced at runtime so a
        // system never reads as planets sitting right on top of each other.
        /// <summary>
        /// Runtime floor for the gap between neighbouring planet orbits. 8,000 km keeps the
        /// system vast (Space-Engineers feel): from the surface of an 8 km planet the next
        /// world is a small distant disc, deep space between planets is genuinely empty,
        /// and the real-voxel LOD window (8,000 km) covers the whole crossing.
        /// </summary>
        private const double MinPlanetGapKm = 8000d;

        // ── Lifecycle ─────────────────────────────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            if (worldSeed == 0)
            {
                var session = VoxelEngine.Menu.WorldSession.Instance;
                if (session != null) worldSeed = session.seed;
            }
            if (worldSeed == 0) worldSeed = 1337;

            if (systemTemplate != null)
                GenerateLayout(systemTemplate, worldSeed);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ── Layout generation (deterministic from seed) ───────────
        /// <summary>
        /// Build the whole system graph with real orbital elements. Planets get strictly
        /// increasing semi-major axes (each step within the authored separation band) so
        /// rings can never collide; moons likewise around their planet. Eccentricities
        /// are small and seeded, inclinations are gentle — real orbits, no intersections.
        /// </summary>
        public void GenerateLayout(SolarSystemTemplate template, int seed)
        {
            _bodies.Clear();
            _asteroids.Clear();
            SceneBodies.Clear();
            if (template == null) { IsReady = false; return; }

            systemTemplate = template;
            worldSeed = seed != 0 ? seed : 1337;
            SimulationSeconds = 0d;

            var rng = new Random((uint)(worldSeed > 0 ? worldSeed : 1));

            double sunMu = template.sun != null && template.sun.gravitationalParameterKm3S2 > 1d
                ? template.sun.gravitationalParameterKm3S2
                : DefaultSunMuKm3S2;

            Sun = new SunInstance
            {
                settings    = template.sun,
                positionKmD = double3.zero,
                gravitationalParamKm3S2 = sunMu,
            };

            // ── Planets ──────────────────────────────────────────
            double planetRadius = ND(ref rng, 400d, 1200d); // innermost ring
            PlanetTemplate[] templates = template.planets ?? System.Array.Empty<PlanetTemplate>();

            for (int i = 0; i < templates.Length; i++)
            {
                var pt = templates[i];
                if (pt == null || pt.body == null) continue;

                if (pt.distanceFromSun > 0f)
                    planetRadius = pt.distanceFromSun;
                else if (i == 0)
                    planetRadius = ND(ref rng, pt.orbitalDistanceKm.x, pt.orbitalDistanceKm.y);
                else
                {
                    // Runtime spacing floor: authored templates can allow planets 500 km
                    // apart, which reads as planets hovering right next to each other and
                    // their gravity wells overlapping. Enforce a sensible minimum gap so
                    // the system feels vast (Space-Engineers scale).
                    double sepLo = Mathd.Max(MinPlanetGapKm, template.minPlanetSeparationKm);
                    double sepHi = Mathd.Max(sepLo + 100d, template.maxPlanetSeparationKm);
                    planetRadius += ND(ref rng, sepLo, sepHi);
                }

                var elements = BuildPlanetElements(pt, planetRadius, ref rng, i, templates.Length);
                var planet = new BodyInstance
                {
                    isPlanet       = true,
                    settings       = pt.body,
                    planetTemplate = pt,
                    sunOrigin      = Sun,
                    orbit          = elements,
                    gravitationalParamKm3S2 = ComputeBodyMuKm3S2(pt.body),
                };
                // The star's μ drives the planet's orbit.
                planet.orbit.gravitationalParamKm3S2 = sunMu;
                planet.UpdateFromOrbit(0d);
                ValidateAndRepairOrbit(planet);
                _bodies.Add(planet);

                // ── Moons of this planet (non-intersecting radii) ──
                if (pt.moons == null || pt.moons.Length == 0) continue;

                double moonRadius = Mathd.Max(pt.body.radiusKm * 2.5, MinMoonGapKm);
                int moonCount = pt.moons.Length;
                for (int m = 0; m < moonCount; m++)
                {
                    var mt = pt.moons[m];
                    if (mt == null || mt.body == null) continue;

                    double lo = mt.orbitRadiusKm.x, hi = mt.orbitRadiusKm.y;
                    moonRadius += Mathd.Max(MinMoonGapKm, ND(ref rng, lo, hi)); // strictly increasing

                    double phase = mt.orbitPhaseDegrees > 0f
                        ? mt.orbitPhaseDegrees * Mathd.Deg2Rad
                        : (moonCount > 1 ? (m / (double)(moonCount - 1)) : 0d) * Mathd.TwoPi
                          + ND(ref rng, -0.5d, 0.5d);

                    var moonElements = BuildMoonElements(mt, moonRadius, phase, ref rng);
                    var moon = new BodyInstance
                    {
                        isPlanet       = false,
                        settings       = mt.body,
                        moonTemplate   = mt,
                        parentBody     = planet,
                        sunOrigin      = Sun,
                        orbit          = moonElements,
                        gravitationalParamKm3S2 = ComputeBodyMuKm3S2(mt.body),
                    };
                    // The parent planet's μ drives the moon's orbit.
                    moon.orbit.gravitationalParamKm3S2 = planet.gravitationalParamKm3S2;
                    moon.UpdateFromOrbit(0d);
                    ValidateAndRepairOrbit(moon);
                    _bodies.Add(moon);

                    // ── Orbiting moons around the moon (sub-moons) ──
                    int subMoonCount = (m == 0 || rng.NextFloat() > 0.45f) ? 1 : 0;
                    for (int sm = 0; sm < subMoonCount; sm++)
                    {
                        double smRadius = Mathd.Max(12d, ND(ref rng, 15d, 32d));
                        var subElements = new OrbitElements
                        {
                            semiMajorAxisKm      = smRadius,
                            eccentricity         = ND(ref rng, 0d, 0.05d),
                            inclinationRad       = ND(ref rng, -0.35d, 0.35d),
                            raanRad              = ND(ref rng, 0d, Mathd.TwoPi),
                            argPeriapsisRad      = ND(ref rng, 0d, Mathd.TwoPi),
                            meanAnomaly0         = ND(ref rng, 0d, Mathd.TwoPi),
                            gravitationalParamKm3S2 = moon.gravitationalParamKm3S2 * 0.1d,
                            timeScale            = Mathd.Max(0.25d, mt.orbitSpeed * 3.5d),
                        };
                        var subMoon = new BodyInstance
                        {
                            isPlanet       = false,
                            settings       = mt.body,
                            moonTemplate   = mt,
                            parentBody     = moon,
                            sunOrigin      = Sun,
                            orbit          = subElements,
                            gravitationalParamKm3S2 = ComputeBodyMuKm3S2(mt.body) * 0.1d,
                        };
                        subMoon.UpdateFromOrbit(0d);
                        ValidateAndRepairOrbit(subMoon);
                        _bodies.Add(subMoon);
                    }
                }
            }

            // ── Asteroids (visual belt + resource catalogue) ─────
            bool hasAuthoredField = false;
            if (template.asteroidFields != null)
            {
                foreach (var field in template.asteroidFields)
                {
                    if (field == null || field.settings == null) continue;
                    hasAuthoredField = true;
                    SpawnAsteroids(field.settings, ref rng);
                }
            }
            if (!hasAuthoredField || _asteroids.Count == 0) SpawnAsteroids(CreateFallbackAsteroidSettings(), ref rng);

            IsReady = true;
        }

        private static OrbitElements BuildPlanetElements(PlanetTemplate pt, double a, ref Random rng,
            int index, int count)
        {
            double eAuthored = pt.orbitEccentricity;
            // Keep periapsis away from the star: e ≤ 1 − 250/a.
            double eMax = Mathd.Clamp(1d - 250d / a, 0.02d, 0.6d);
            double e = eAuthored > 0.0001d
                ? Mathd.Min(eAuthored, eMax)
                : ND(ref rng, 0.015d, Mathd.Min(0.10d, eMax));
            double phase = pt.orbitPhaseDegrees > 0f
                ? pt.orbitPhaseDegrees * Mathd.Deg2Rad
                : (count > 1 ? (index / (double)(count - 1)) : 0d) * Mathd.TwoPi
                  + ND(ref rng, -0.4d, 0.4d);

            return new OrbitElements
            {
                semiMajorAxisKm      = a,
                eccentricity         = e,
                inclinationRad       = ND(ref rng, -0.09d, 0.09d),
                raanRad              = ND(ref rng, 0d, Mathd.TwoPi),
                argPeriapsisRad      = ND(ref rng, 0d, Mathd.TwoPi),
                meanAnomaly0         = phase,
                gravitationalParamKm3S2 = 180d, // placeholder; the real sun μ is assigned by the caller
                timeScale            = Mathd.Max(0.1d, pt.orbitSpeed > 0f ? pt.orbitSpeed : 1d),
            };
        }

        private static OrbitElements BuildMoonElements(MoonTemplate mt, double a, double phase, ref Random rng)
        {
            double eMax = Mathd.Clamp(1d - 6d / a, 0.005d, 0.3d);
            double e = mt.orbitEccentricity > 0.0001d
                ? Mathd.Min(mt.orbitEccentricity, eMax)
                : ND(ref rng, 0.002d, Mathd.Min(0.04d, eMax));
            return new OrbitElements
            {
                semiMajorAxisKm      = a,
                eccentricity         = e,
                inclinationRad       = ND(ref rng, -0.22d, 0.22d),
                raanRad              = ND(ref rng, 0d, Mathd.TwoPi),
                argPeriapsisRad      = ND(ref rng, 0d, Mathd.TwoPi),
                meanAnomaly0         = phase,
                gravitationalParamKm3S2 = 1d, // placeholder; the parent planet's μ is assigned by the caller
                timeScale            = Mathd.Max(0.1d, mt.orbitSpeed > 0f ? mt.orbitSpeed : 1d),
            };
        }

        /// <summary>μ (km³/s²) of a body, derived from its authored surface gravity and radius: μ = g·r².</summary>
        public static double ComputeBodyMuKm3S2(BodySettings settings)
        {
            if (settings == null) return 0d;
            bool isBelt = settings.bodyName != null &&
                          (settings.bodyName.IndexOf("Asteroid", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           settings.bodyName.IndexOf("Belt", System.StringComparison.OrdinalIgnoreCase) >= 0);
            if (isBelt) return 0d;
            double g = 9.81d * Mathd.Clamp(settings.gravity, 0d, 5d);
            double rM = settings.radiusKm * 1000d;
            return g * rM * rM / 1e9d; // m³/s² → km³/s²
        }

        private static AsteroidFieldSettings CreateFallbackAsteroidSettings()
        {
            return new AsteroidFieldSettings
            {
                density = 1f,
                resourceCount = 3,
                possibleResources = new[]
                {
                    MaterialId.Iron, MaterialId.Nickel, MaterialId.Silicon,
                    MaterialId.Platinum, MaterialId.Ice
                },
                sizeRangeKm = new Vector2(0.03f, 0.35f),
                shellRadiusKm = new Vector2(8000f, 12000f)
            };
        }

        private void SpawnAsteroids(AsteroidFieldSettings s, ref Random rng)
        {
            if (s == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(60f * s.density), 0, 400);

            var chosen = new List<MaterialId>();
            if (s.possibleResources != null && s.resourceCount > 0)
            {
                var pool = new List<MaterialId>(s.possibleResources);
                for (int i = 0; i < s.resourceCount && pool.Count > 0; i++)
                {
                    int idx = rng.NextInt(0, pool.Count);
                    chosen.Add(pool[idx]);
                    pool.RemoveAt(idx);
                }
            }

            for (int i = 0; i < count; i++)
            {
                float r = rng.NextFloat(s.shellRadiusKm.x, s.shellRadiusKm.y);
                double3 dir = RandomUnitAxisD(ref rng);
                _asteroids.Add(new AsteroidInstance
                {
                    positionKm   = (Vector3)(float3)(dir * r),
                    sizeKm       = rng.NextFloat(s.sizeRangeKm.x, s.sizeRangeKm.y),
                    material     = chosen.Count > 0 ? chosen[rng.NextInt(0, chosen.Count)] : MaterialId.Stone,
                });
            }
        }

        // ── Per-frame orbit advance (real Keplerian propagation) ──
        private void Update()
        {
            if (!IsReady) return;
            SimulationSeconds += Time.deltaTime * (orbitalTimeScale > 0d ? orbitalTimeScale : 1d);
            double t = SimulationSeconds;

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b == null || !b.orbit.IsValid) continue;
                b.UpdateFromOrbit(t);
            }
        }

        // ── Public queries ─────────────────────────────────────────
        /// <summary>
        /// N-body gravitational acceleration (m/s²) at a cosmic position (km): the sum of
        /// the star and every body's inverse-square pull. Below a body's surface the pull
        /// is clamped to its surface value (the interior is handled by the streamed world).
        /// </summary>
        public double3 GetGravityMetersS2(double3 posKm)
        {
            double3 g = double3.zero;
            if (!IsReady || Sun == null) return g;

            // Star.
            double3 toSun = Sun.positionKmD - posKm;
            double dSun2 = math.lengthsq(toSun);
            double dSun = math.sqrt(dSun2);
            if (dSun > 0.5d)
            {
                double a = Sun.gravitationalParamKm3S2 * 1000d / dSun2; // μ·1000/d² with d in km → m/s²
                g += toSun / dSun * a;
            }

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b == null || b.gravitationalParamKm3S2 <= 0d) continue;
                double3 toB = b.positionKmD - posKm;
                double d2 = math.lengthsq(toB);
                if (d2 < 1e-12) continue;

                // Distance to the core; below the surface the pull falls off LINEARLY
                // toward zero at the core (physically correct for a solid sphere). This
                // is critical: a player who ever clips into the terrain can no longer be
                // accelerated through the core to escape velocity — the old surface-clamp
                // made any terrain clip a one-way launch through the planet.
                double rSurfaceKm = Mathd.Max(0.05d, b.settings != null ? b.settings.radiusKm : 1d);
                double dActual = math.sqrt(d2);
                double dClamped = dActual < rSurfaceKm ? rSurfaceKm : dActual;

                double a = b.gravitationalParamKm3S2 * 1000d / (dClamped * dClamped);
                if (dActual < rSurfaceKm)
                    a *= dActual / rSurfaceKm;   // linear interior falloff (g·d/R)
                g += toB / dClamped * a;
            }
            return g;
        }

        /// <summary>
        /// N-body gravity in the SCENE frame: the cosmic pull at a position MINUS the
        /// pull the frame body itself experiences (its orbital acceleration around the
        /// star). Without this subtraction the player on a planet's surface feels the
        /// sun's full pull as a constant sideways force — a free-falling reference
        /// frame must cancel the frame body's own acceleration, leaving only the local
        /// body's pull plus a negligible tidal term.
        /// </summary>
        public double3 GetFrameRelativeGravityMetersS2(double3 posKm, CelestialBody frameBody)
        {
            double3 g = GetGravityMetersS2(posKm);
            if (frameBody == null) return g;
            foreach (var kv in SceneBodies)
            {
                if (kv.Value != frameBody) continue;
                double3 center = CosmicPositionOf(kv.Key);
                g -= GetGravityMetersS2(center);
                break;
            }
            return g;
        }

        /// <summary>
        /// The body whose gravity dominates at a cosmic position — or null when the star
        /// dominates (deep space). This is the scene reference frame selection rule.
        /// </summary>
        public BodyInstance GetDominantBody(double3 posKm, out double accelMps2)
        {
            accelMps2 = 0d;
            if (!IsReady || Sun == null) return null;

            double bestG = 0d;
            BodyInstance best = null;

            double3 toSun = Sun.positionKmD - posKm;
            double dSun2 = math.lengthsq(toSun);
            if (dSun2 > 1d)
            {
                bestG = Sun.gravitationalParamKm3S2 * 1000d / dSun2;
            }

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b == null || b.gravitationalParamKm3S2 <= 0d) continue;
                double3 toB = b.positionKmD - posKm;
                double d2 = math.lengthsq(toB);
                if (d2 < 1e-12) continue;
                double d = math.sqrt(d2);
                double rSurfaceKm = Mathd.Max(0.05d, b.settings != null ? b.settings.radiusKm : 1d);
                if (d < rSurfaceKm) d = rSurfaceKm;
                double g = b.gravitationalParamKm3S2 * 1000d / (d * d);
                if (g > bestG) { bestG = g; best = b; }
            }

            accelMps2 = bestG;
            return best;
        }

        /// <summary>
        /// Velocity of the co-moving scene frame (km/s) at a cosmic position: a
        /// gravity-weighted blend of nearby bodies' orbital velocities (the star itself
        /// is the inertial anchor). Near a planet the frame moves with the planet;
        /// in deep space the frame is the star frame (≈ 0).
        /// </summary>
        public double3 GetFrameVelocityKmS(double3 posKm)
        {
            if (!IsReady) return double3.zero;

            double3 acc = double3.zero;
            double wSum = 0d;

            // Star contributes its (zero) velocity with its own weight so deep space
            // settles on the inertial star frame.
            double3 toSun = Sun != null ? Sun.positionKmD - posKm : double3.zero;
            double dSun2 = math.lengthsq(toSun);
            double wSun = Sun != null ? Sun.gravitationalParamKm3S2 / math.max(dSun2, 100d) : 0d;
            wSum += wSun;

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                if (b == null || b.gravitationalParamKm3S2 <= 0d) continue;
                double3 toB = b.positionKmD - posKm;
                double d2 = math.lengthsq(toB);
                if (d2 < 1e-9) continue;
                double w = b.gravitationalParamKm3S2 / math.max(d2, 4d);
                acc += w * b.velocityKmS;
                wSum += w;
            }

            return wSum > 1e-12 ? acc / wSum : double3.zero;
        }

        /// <summary>Orbital velocity (km/s) of a body in the cosmic inertial frame.</summary>
        public double3 VelocityOf(BodyInstance body)
        {
            if (body == null) return double3.zero;
            double3 v = body.velocityKmS;
            if (body.parentBody != null) v += VelocityOf(body.parentBody);
            return v;
        }

        /// <summary>Cosmic position (km) of a body in the inertial frame (parent chain summed).</summary>
        /// <summary>
        /// NaN defence (9.4.0): if a freshly-built orbit propagates to a non-finite
        /// position, replace it with a safe circular orbit and report the original
        /// elements ONCE — a single corrupt template must never break the system.
        /// </summary>
        private static void ValidateAndRepairOrbit(BodyInstance b)
        {
            if (b == null) return;
            double3 pos = b.positionKmD;
            bool bad = double.IsNaN(pos.x) || double.IsNaN(pos.y) || double.IsNaN(pos.z) ||
                       double.IsInfinity(pos.x) || double.IsInfinity(pos.y) || double.IsInfinity(pos.z);
            if (!bad) return;

            var o = b.orbit;
            Debug.LogError($"[CosmicRegistry] '{b.DisplayName}' orbit propagated to NaN — repaired to a safe " +
                           $"circular orbit. Original: a={o.semiMajorAxisKm:0.###} km, e={o.eccentricity:0.###}, " +
                           $"i={o.inclinationRad:0.###}, μ={o.gravitationalParamKm3S2:0.###}, M0={o.meanAnomaly0:0.###}, " +
                           $"timeScale={o.timeScale:0.###}. Fix the authored template values.");

            double a = (double.IsNaN(o.semiMajorAxisKm) || o.semiMajorAxisKm < 1d) ? 30000d : o.semiMajorAxisKm;
            double mu = (double.IsNaN(o.gravitationalParamKm3S2) || o.gravitationalParamKm3S2 < 0.000001d)
                        ? 1000d : o.gravitationalParamKm3S2;
            b.orbit = new OrbitElements
            {
                semiMajorAxisKm = a,
                eccentricity = 0d,
                inclinationRad = 0d,
                raanRad = 0d,
                argPeriapsisRad = 0d,
                meanAnomaly0 = double.IsNaN(o.meanAnomaly0) ? 0d : o.meanAnomaly0,
                gravitationalParamKm3S2 = mu,
                timeScale = 1d
            };
            b.UpdateFromOrbit(0d);
            if (double.IsNaN(b.positionKmD.x))
                b.positionKmD = new double3(a, 0d, 0d);   // absolute last resort
            b.positionKm = (Vector3)(Unity.Mathematics.float3)b.positionKmD;
        }

        public double3 CosmicPositionOf(BodyInstance body)
        {
            if (body == null) return double3.zero;
            double3 p = body.positionKmD;
            if (body.parentBody != null) p += CosmicPositionOf(body.parentBody);
            return p;
        }

        /// <summary>Direction from a cosmic position toward the star.</summary>
        public double3 GetSunDirectionKm(double3 posKm)
        {
            if (Sun == null) return new double3(0d, 1d, 0d);
            double3 d = Sun.positionKmD - posKm;
            double len = math.length(d);
            return len < 1e-9 ? new double3(0d, 1d, 0d) : d / len;
        }

        /// <summary>Legacy wrapper: direction toward the star from a km-space position.</summary>
        public Vector3 GetSunDirection(Vector3 worldPositionKm)
            => (Vector3)(float3)GetSunDirectionKm(ToDouble3(worldPositionKm));

        /// <summary>Nearest body (by cosmic distance) to a km-space position.</summary>
        public BodyInstance FindNearestBody(Vector3 worldPositionKm) => FindNearestBodyKm(ToDouble3(worldPositionKm));

        /// <summary>Nearest body (by cosmic distance) to a cosmic position.</summary>
        public BodyInstance FindNearestBodyKm(double3 posKm)
        {
            BodyInstance best = null;
            double bestD = double.MaxValue;
            for (int i = 0; i < _bodies.Count; i++)
            {
                double d = math.lengthsq(_bodies[i].positionKmD - posKm);
                if (d < bestD) { bestD = d; best = _bodies[i]; }
            }
            return best;
        }

        /// <summary>Total star intensity of the system (strength × count).</summary>
        public float TotalSunIntensity => Sun != null && Sun.settings != null
            ? Sun.settings.intensity * Mathf.Max(1, Sun.settings.sunCount)
            : 1f;

        // ── Editor visualisation ───────────────────────────────────
        private void OnDrawGizmos()
        {
            if (Sun == null) return;

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawSphere((Vector3)(float3)Sun.positionKmD, Mathf.Max(20f, TotalSunIntensity * 30f));

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                bool isPlanet = b.isPlanet;
                double3 absolute = CosmicPositionOf(b);
                double3 ringCenter = b.parentBody != null ? CosmicPositionOf(b.parentBody) : double3.zero;

                // Orbit ring around the parent.
                Gizmos.color = isPlanet ? new Color(0.3f, 0.7f, 1f, 0.25f) : new Color(0.8f, 0.8f, 0.8f, 0.18f);
                DrawOrbit((Vector3)(float3)ringCenter, (float)b.orbit.semiMajorAxisKm, (float)b.orbit.inclinationRad,
                    (Vector3)(float3)b.orbit.PlaneNormal);

                // Body marker.
                float size = isPlanet ? Mathf.Max(6f, b.settings.radiusKm * 0.5f) : 3f;
                Gizmos.color = isPlanet ? new Color(0.3f, 0.8f, 0.5f, 0.9f) : new Color(0.7f, 0.7f, 0.75f, 0.9f);
                Gizmos.DrawSphere((Vector3)(float3)absolute, size);
            }

            if (_asteroids != null)
            {
                Gizmos.color = new Color(0.6f, 0.55f, 0.5f, 0.5f);
                for (int i = 0; i < _asteroids.Count; i++)
                    Gizmos.DrawSphere(_asteroids[i].positionKm, _asteroids[i].sizeKm);
            }
        }

        private static void DrawOrbit(Vector3 center, float radius, float inclination, Vector3 axis)
        {
            const int Segs = 48;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= Segs; i++)
            {
                float a = (i / (float)Segs) * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * Mathf.Sin(inclination), Mathf.Sin(a) * radius);
                if (axis.sqrMagnitude > 0.001f) p = Quaternion.LookRotation(axis, Vector3.up) * p;
                p += center;
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        /// <summary>Uniform random double in [lo, hi). Version-safe wrapper around Random.NextDouble().</summary>
        private static double ND(ref Random rng, double lo, double hi)
            => lo + (hi - lo) * rng.NextDouble();

        /// <summary>UnityEngine.Vector3 → Unity.Mathematics.double3 (via float3 — no direct cast exists).</summary>
        public static double3 ToDouble3(Vector3 v) => new double3(v.x, v.y, v.z);

        private static double3 RandomUnitAxisD(ref Random rng)
        {
            double3 v = new double3(ND(ref rng, -1d, 1d), ND(ref rng, -0.6d, 0.6d), ND(ref rng, -1d, 1d));
            return math.lengthsq(v) < 1e-8 ? new double3(0d, 0d, 1d) : math.normalize(v);
        }

        // ── Double-precision helper ────────────────────────────────
        private static class Mathd
        {
            public const double Deg2Rad = 0.017453292519943295d;
            public const double TwoPi = 6.283185307179586d;
            public static double Max(double a, double b) => a > b ? a : b;
            public static double Min(double a, double b) => a < b ? a : b;
            public static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
        }
    }

    // ── Runtime POD instances ─────────────────────────────────────
    public class SunInstance
    {
        public SunSettings settings;
        public double3 positionKmD;
        public double gravitationalParamKm3S2 = 180d;
        public Vector3 positionKm => (Vector3)(float3)positionKmD;
    }

    public class BodyInstance
    {
        public bool isPlanet;
        public BodySettings settings;        // shared designer settings
        public PlanetTemplate planetTemplate;
        public MoonTemplate moonTemplate;
        public BodyInstance parentBody;      // moon → planet
        public SunInstance sunOrigin;        // planet → sun

        /// <summary>Keplerian elements around the parent (valid after propagation starts).</summary>
        public OrbitElements orbit;

        /// <summary>Position relative to the parent (km, double).</summary>
        public double3 positionKmD;

        /// <summary>Velocity relative to the parent (km/s, double).</summary>
        public double3 velocityKmS;

        /// <summary>This body's own gravitational parameter (km³/s²) = g·r².</summary>
        public double gravitationalParamKm3S2;

        // ── Legacy float fields (kept for sky renderers / gizmos) ──
        public Vector3 positionKm;
        public float orbitRadiusKm;
        public float orbitAngle;
        public float orbitAngularSpd;
        public float inclination;
        public Vector3 phaseAxis;

        public string DisplayName => settings != null ? settings.bodyName : "Body";

        /// <summary>Propagate position/velocity from the orbital elements at time t (sim seconds).</summary>
        public void UpdateFromOrbit(double t)
        {
            double3 newPos = OrbitMath.PositionKm(orbit, t);
            double3 newVel = OrbitMath.VelocityKmS(orbit, t);

            // NaN guard (9.3.0): corrupt elements (e ≥ 1, degenerate μ/a) must never
            // poison the body's position — keep the last valid state instead.
            bool posOk = !(double.IsNaN(newPos.x) || double.IsNaN(newPos.y) || double.IsNaN(newPos.z));
            bool velOk = !(double.IsNaN(newVel.x) || double.IsNaN(newVel.y) || double.IsNaN(newVel.z));
            if (posOk) positionKmD = newPos;
            if (velOk) velocityKmS = newVel;

            positionKm = (Vector3)(float3)positionKmD;
            orbitRadiusKm = (float)orbit.semiMajorAxisKm;
            orbitAngle = (float)OrbitMath.MeanAnomalyAt(orbit, t);
            orbitAngularSpd = (float)orbit.MeanMotion;
            inclination = (float)orbit.inclinationRad;
            phaseAxis = (Vector3)(float3)orbit.PlaneNormal;
        }
    }

    public class AsteroidInstance
    {
        public Vector3 positionKm;
        public float sizeKm;
        public MaterialId material;
    }
}
