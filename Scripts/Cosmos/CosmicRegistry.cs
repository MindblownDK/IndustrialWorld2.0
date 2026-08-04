// Assets/Scripts/VoxelEngine/Cosmos/CosmicRegistry.cs
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using VoxelEngine.Materials;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Runtime model of the active solar system: the star, all planets, their moons, and the
    /// asteroid fields — plus the background quasar direction.
    ///
    /// Given a <see cref="SolarSystemTemplate"/> and a world seed, it deterministically lays
    /// out the system (planets 500–10000 km apart, moons on non-intersecting orbits) and
    /// advances real circular orbits every frame. It also exposes the sun direction so solar
    /// panels (Phase 5) can verify line-of-sight to the star from anywhere in their system.
    ///
    /// Positions are stored in KILOMETRES (cosmic space). A future floating-origin space
    /// renderer (Phase 6) bridges km → scene units; <see cref="WorldUnitsPerKm"/> is the hook.
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

        [Header("Cosmic → Scene bridge")]
        [Tooltip("Scene units per km. Tuned by the Phase 6 space renderer + floating-origin system.")]
        public float WorldUnitsPerKm = 1f;

        // ── Runtime graph ─────────────────────────────────────────
        public SunInstance Sun { get; private set; }
        public IReadOnlyList<BodyInstance> Bodies => _bodies;
        public IReadOnlyList<AsteroidInstance> Asteroids => _asteroids;
        private readonly List<BodyInstance> _bodies = new List<BodyInstance>();
        private readonly List<AsteroidInstance> _asteroids = new List<AsteroidInstance>();
        public bool IsReady { get; private set; }

        // Orbit dynamics: inner bodies orbit faster (loosely Keplerian).
        private const float BaseAngularRate = 0.005f;
        private const float MinMoonGapKm = 40f;

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

        // ── Layout generation (deterministic from seed) ───────────
        /// <summary>
        /// Build the whole system graph. Planets get strictly-increasing orbit radii (each
        /// step within the system's 500–10000 km separation band) so no two planet rings can
        /// ever sit closer than the minimum separation — planets therefore never collide.
        /// Moons likewise get strictly-increasing radii around their planet.
        /// </summary>
        public void GenerateLayout(SolarSystemTemplate template, int seed)
        {
            _bodies.Clear();
            _asteroids.Clear();
            if (template == null) { IsReady = false; return; }

            // Keep the inspector/runtime registry contract honest even when CosmosBootstrap
            // creates this component before it has assigned the automatically resolved system.
            systemTemplate = template;
            worldSeed = seed != 0 ? seed : 1337;
            var rng = new Unity.Mathematics.Random((uint)(worldSeed > 0 ? worldSeed : 1));

            Sun = new SunInstance
            {
                settings    = template.sun,
                positionKm  = Vector3.zero,
            };

            // ── Planets ──────────────────────────────────────────
            float planetRadius = rng.NextFloat(400f, 1200f); // innermost ring
            // Never mutate an authored template at runtime merely because a legacy system has
            // no planet array. The setup path owns persistent asset repair.
            PlanetTemplate[] templates = template.planets ?? System.Array.Empty<PlanetTemplate>();

            for (int i = 0; i < templates.Length; i++)
            {
                var pt = templates[i];
                if (pt == null || pt.body == null) continue;

                if (i > 0)
                    planetRadius += rng.NextFloat(template.minPlanetSeparationKm, template.maxPlanetSeparationKm);

                // Use fixed distance if specified, otherwise use the random range.
                if (pt.distanceFromSun > 0f)
                    planetRadius = pt.distanceFromSun;
                else if (i == 0)
                    planetRadius = rng.NextFloat(pt.orbitalDistanceKm.x, pt.orbitalDistanceKm.y);
                else
                    planetRadius += rng.NextFloat(template.minPlanetSeparationKm, template.maxPlanetSeparationKm);

                float inclination = rng.NextFloat(-0.12f, 0.12f); // gentle 3D tilt
                var planet = new BodyInstance
                {
                    isPlanet        = true,
                    settings        = pt.body,
                    planetTemplate  = pt,
                    orbitRadiusKm   = planetRadius,
                    orbitAngle      = (templates.Length > 1 ? (i / (float)(templates.Length - 1)) : 0f) * Mathf.PI * 2f
                                      + rng.NextFloat(0f, 0.6f),
                    orbitAngularSpd = Mathf.Max(0.05f, pt.orbitSpeed) * BaseAngularRate / Mathf.Sqrt(Mathf.Max(1f, planetRadius)),
                    inclination     = inclination,
                    phaseAxis       = RandomUnitAxis(ref rng),
                    sunOrigin       = Sun,
                };
                planet.positionKm = OrbitPosition(planet, Sun.positionKm);
                _bodies.Add(planet);

                // ── Moons of this planet (non-intersecting radii) ──
                if (pt.moons == null || pt.moons.Length == 0) continue;

                float moonRadius = Mathf.Max(pt.body.radiusKm * 2.5f, MinMoonGapKm);
                int moonCount = pt.moons.Length;
                for (int m = 0; m < moonCount; m++)
                {
                    var mt = pt.moons[m];
                    if (mt == null || mt.body == null) continue;

                    float lo = mt.orbitRadiusKm.x, hi = mt.orbitRadiusKm.y;
                    moonRadius += Mathf.Max(MinMoonGapKm, rng.NextFloat(lo, hi)); // strictly increasing

                    float phase = mt.orbitPhaseDegrees > 0f
                        ? mt.orbitPhaseDegrees * Mathf.Deg2Rad
                        : (moonCount > 1 ? (m / (float)(moonCount - 1)) : 0f) * Mathf.PI * 2f;

                    var moon = new BodyInstance
                    {
                        isPlanet        = false,
                        settings        = mt.body,
                        moonTemplate    = mt,
                        parentBody      = planet,
                        orbitRadiusKm   = moonRadius,
                        orbitAngle      = phase,
                        orbitAngularSpd = Mathf.Max(0.1f, mt.orbitSpeed) * BaseAngularRate / Mathf.Sqrt(Mathf.Max(1f, moonRadius)),
                        inclination     = rng.NextFloat(-0.25f, 0.25f),
                        phaseAxis       = RandomUnitAxis(ref rng),
                    };
                    moon.positionKm = OrbitPosition(moon, planet.positionKm);
                    _bodies.Add(moon);

                    // ── Orbiting moons around the moon (sub-satellites / moonlets) ──
                    int subMoonCount = (m == 0 || rng.NextFloat() > 0.45f) ? 1 : 0;
                    for (int sm = 0; sm < subMoonCount; sm++)
                    {
                        float smRadius = Mathf.Max(12f, rng.NextFloat(15f, 32f));
                        var subMoon = new BodyInstance
                        {
                            isPlanet        = false,
                            settings        = mt.body,
                            moonTemplate    = mt,
                            parentBody      = moon,
                            orbitRadiusKm   = smRadius,
                            orbitAngle      = rng.NextFloat(0f, Mathf.PI * 2f),
                            orbitAngularSpd = Mathf.Max(0.25f, mt.orbitSpeed) * BaseAngularRate * 3.5f / Mathf.Sqrt(Mathf.Max(1f, smRadius)),
                            inclination     = rng.NextFloat(-0.35f, 0.35f),
                            phaseAxis       = RandomUnitAxis(ref rng),
                        };
                        subMoon.positionKm = OrbitPosition(subMoon, moon.positionKm);
                        _bodies.Add(subMoon);
                    }
                }
            }

            // ── Asteroids ────────────────────────────────────────
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
            // Existing systems created before the belt asset have an empty array. Populate a
            // deterministic runtime belt instead of leaving the entire solar system lifeless;
            // Step 21 later replaces this with the setup-owned Asteroids_MainBelt asset.
            if (!hasAuthoredField || _asteroids.Count == 0) SpawnAsteroids(CreateFallbackAsteroidSettings(), ref rng);

            IsReady = true;
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

        private void SpawnAsteroids(AsteroidFieldSettings s, ref Unity.Mathematics.Random rng)
        {
            if (s == null) return;
            int count = Mathf.Clamp(Mathf.RoundToInt(60f * s.density), 0, 400);

            // Pick up to resourceCount distinct materials from the pool; remainder = stone.
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
                Vector3 dir = RandomUnitAxis(ref rng);
                _asteroids.Add(new AsteroidInstance
                {
                    positionKm   = Sun.positionKm + dir * r,
                    sizeKm       = rng.NextFloat(s.sizeRangeKm.x, s.sizeRangeKm.y),
                    material     = chosen.Count > 0 ? chosen[rng.NextInt(0, chosen.Count)] : MaterialId.Stone,
                });
            }
        }

        // ── Per-frame orbit advance ────────────────────────────────
        private void Update()
        {
            if (!IsReady) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                b.orbitAngle += b.orbitAngularSpd * dt;
                Vector3 center = b.parentBody != null ? b.parentBody.positionKm
                                                        : (b.sunOrigin != null ? b.sunOrigin.positionKm : Vector3.zero);
                b.positionKm = OrbitPosition(b, center);
            }
        }

        /// <summary>Position of a body on its (optionally inclined) circular orbit.</summary>
        private Vector3 OrbitPosition(BodyInstance b, Vector3 center)
        {
            Vector3 p = new Vector3(
                Mathf.Cos(b.orbitAngle) * b.orbitRadiusKm,
                Mathf.Sin(b.orbitAngle) * b.orbitRadiusKm * b.inclination,
                Mathf.Sin(b.orbitAngle) * b.orbitRadiusKm);

            // Rotate the orbit plane by a per-body axis so systems look 3D.
            if (b.phaseAxis.sqrMagnitude > 0.001f)
                p = Quaternion.LookRotation(b.phaseAxis, Vector3.up) * p;
            return center + p;
        }

        private static Vector3 RandomUnitAxis(ref Unity.Mathematics.Random rng)
        {
            Vector3 v = new Vector3(rng.NextFloat(-1f, 1f), rng.NextFloat(-0.6f, 0.6f), rng.NextFloat(-1f, 1f));
            return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
        }

        // ── Public queries ─────────────────────────────────────────
        /// <summary>
        /// Direction from a world position (km space) toward the nearest/primary star.
        /// Solar panels use this in Phase 5 (plus a sphere-occlusion test) to require
        /// line-of-sight to the sun.
        /// </summary>
        public Vector3 GetSunDirection(Vector3 worldPositionKm)
        {
            if (Sun == null) return Vector3.up;
            Vector3 d = Sun.positionKm - worldPositionKm;
            return d.sqrMagnitude < 1e-4f ? Vector3.up : d.normalized;
        }

        /// <summary>Nearest body to a position (km space). Used by the space renderer.</summary>
        public BodyInstance FindNearestBody(Vector3 worldPositionKm)
        {
            BodyInstance best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < _bodies.Count; i++)
            {
                float d = (_bodies[i].positionKm - worldPositionKm).sqrMagnitude;
                if (d < bestD) { bestD = d; best = _bodies[i]; }
            }
            return best;
        }

        /// <summary>Total star intensity of the system (strength × count).</summary>
        public float TotalSunIntensity => Sun != null && Sun.settings != null
            ? Sun.settings.intensity * Mathf.Max(1, Sun.settings.sunCount)
            : 1f;

        // ── Editor visualisation ───────────────────────────────────
        // Lets you SEE the system in the Scene view before any rendering exists.
        private void OnDrawGizmos()
        {
            if (Sun == null) return;

            Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            Gizmos.DrawSphere(Sun.positionKm, Mathf.Max(20f, TotalSunIntensity * 30f));

            for (int i = 0; i < _bodies.Count; i++)
            {
                var b = _bodies[i];
                bool isPlanet = b.isPlanet;
                Vector3 center = b.parentBody != null ? b.parentBody.positionKm
                                                        : (b.sunOrigin != null ? b.sunOrigin.positionKm : Vector3.zero);

                // Orbit ring.
                Gizmos.color = isPlanet ? new Color(0.3f, 0.7f, 1f, 0.25f) : new Color(0.8f, 0.8f, 0.8f, 0.18f);
                DrawOrbit(center, b.orbitRadiusKm, b.inclination, b.phaseAxis);

                // Body marker.
                float size = isPlanet ? Mathf.Max(6f, b.settings.radiusKm * 0.5f) : 3f;
                Gizmos.color = isPlanet ? new Color(0.3f, 0.8f, 0.5f, 0.9f) : new Color(0.7f, 0.7f, 0.75f, 0.9f);
                Gizmos.DrawSphere(b.positionKm, size);
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
                Vector3 p = new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius * inclination, Mathf.Sin(a) * radius);
                if (axis.sqrMagnitude > 0.001f) p = Quaternion.LookRotation(axis, Vector3.up) * p;
                p += center;
                if (i > 0) Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }

    // ── Runtime POD instances ─────────────────────────────────────
    public class SunInstance
    {
        public SunSettings settings;
        public Vector3 positionKm;
    }

    public class BodyInstance
    {
        public bool isPlanet;
        public BodySettings settings;        // shared designer settings
        public PlanetTemplate planetTemplate;
        public MoonTemplate moonTemplate;
        public BodyInstance parentBody;      // moon → planet
        public SunInstance sunOrigin;        // planet → sun
        public Vector3 positionKm;
        public float orbitRadiusKm;
        public float orbitAngle;
        public float orbitAngularSpd;
        public float inclination;
        public Vector3 phaseAxis;

        public string DisplayName => settings != null ? settings.bodyName : "Body";
    }

    public class AsteroidInstance
    {
        public Vector3 positionKm;
        public float sizeKm;
        public MaterialId material;
    }
}
