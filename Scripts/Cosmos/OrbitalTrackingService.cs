// Assets/Scripts/VoxelEngine/Cosmos/OrbitalTrackingService.cs
//
// The data layer behind the orbital map.
//
// One query returns everything the map draws: every celestial body from the
// CosmicRegistry and every player construct that has a GridIdentity, each with a
// name, a classification, a position in cosmic km, and — where one exists — a
// solved orbit around its dominant body.
//
// Deliberately separate from the map UI. The UI should be a renderer of this
// snapshot and nothing more, so the same data can later feed a station's map
// screen, a navigation computer, or an autopilot target picker without any of
// them depending on UIElements.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    /// <summary>What kind of thing a map entry represents.</summary>
    public enum MapEntryKind
    {
        Sun = 0,
        Planet = 1,
        Moon = 2,
        Vessel = 3,
        Satellite = 4,
        Station = 5,
        Asteroid = 6,
    }

    /// <summary>Coarse motion state, in the player's language rather than an orbital mechanic's.</summary>
    public enum MapMotionState
    {
        /// <summary>No solution — landed, or not moving relative to anything.</summary>
        Landed = 0,
        /// <summary>In a closed orbit around a parent body.</summary>
        Orbiting = 1,
        /// <summary>Sub-orbital: the path intersects the surface.</summary>
        Suborbital = 2,
        /// <summary>Above escape velocity relative to the parent.</summary>
        Escaping = 3,
        /// <summary>Coasting with no dominant body — solar frame.</summary>
        Drifting = 4,
        /// <summary>Flying inside an atmosphere under power or gravity.</summary>
        Flying = 5,
    }

    /// <summary>One row on the orbital map. A value type so a refresh allocates nothing.</summary>
    public readonly struct MapEntry
    {
        public readonly string Name;
        public readonly MapEntryKind Kind;
        public readonly MapMotionState Motion;

        /// <summary>Absolute position in cosmic km.</summary>
        public readonly double3 PositionKm;

        /// <summary>The body this entry orbits, or null for the sun and for drifting craft.</summary>
        public readonly BodyInstance Parent;
        public readonly string ParentName;

        /// <summary>Altitude above the parent's surface, km. NaN when there is no parent.</summary>
        public readonly double AltitudeKm;
        public readonly double ApoapsisKm;
        public readonly double PeriapsisKm;
        public readonly double PeriodSeconds;
        public readonly double InclinationDeg;
        public readonly double SpeedMs;

        /// <summary>
        /// Orbit orientation (rad) + live true anomaly (rad). Bodies only — the map's
        /// exact ellipse/trail sampler needs them; craft pass NaN and keep the
        /// axis-aligned approximation, which carries no orientation data.
        /// </summary>
        public readonly double RaanRad;
        public readonly double ArgPeriapsisRad;
        public readonly double TrueAnomalyRad;

        /// <summary>Radius in km, for drawing bodies to scale. Zero for craft.</summary>
        public readonly double RadiusKm;

        /// <summary>The construct this entry describes, when it is a player grid.</summary>
        public readonly GridEntity Grid;

        /// <summary>False when the construct is outside the equipped map's tracking range.</summary>
        public readonly bool InRange;

        public MapEntry(string name, MapEntryKind kind, MapMotionState motion, double3 positionKm,
            BodyInstance parent, string parentName, double altitudeKm, double apoapsisKm,
            double periapsisKm, double periodSeconds, double inclinationDeg, double speedMs,
            double radiusKm, GridEntity grid, bool inRange,
            double raanRad, double argPeriapsisRad, double trueAnomalyRad)
        {
            Name = name; Kind = kind; Motion = motion; PositionKm = positionKm;
            Parent = parent; ParentName = parentName; AltitudeKm = altitudeKm;
            ApoapsisKm = apoapsisKm; PeriapsisKm = periapsisKm; PeriodSeconds = periodSeconds;
            InclinationDeg = inclinationDeg; SpeedMs = speedMs; RadiusKm = radiusKm;
            Grid = grid; InRange = inRange;
            RaanRad = raanRad; ArgPeriapsisRad = argPeriapsisRad; TrueAnomalyRad = trueAnomalyRad;
        }

        public bool IsBody => Kind == MapEntryKind.Sun || Kind == MapEntryKind.Planet
            || Kind == MapEntryKind.Moon || Kind == MapEntryKind.Asteroid;
        public bool IsCraft => !IsBody;

        /// <summary>Player-facing state word, matching what the map legend shows.</summary>
        public string MotionLabel => Motion switch
        {
            MapMotionState.Orbiting => "ORBITING",
            MapMotionState.Suborbital => "SUBORBITAL",
            MapMotionState.Escaping => "ESCAPING",
            MapMotionState.Drifting => "DRIFTING",
            MapMotionState.Flying => "IN FLIGHT",
            _ => "LANDED",
        };

        public string KindLabel => Kind switch
        {
            MapEntryKind.Sun => "STAR",
            MapEntryKind.Planet => "PLANET",
            MapEntryKind.Moon => "MOON",
            MapEntryKind.Satellite => "SATELLITE",
            MapEntryKind.Station => "STATION",
            MapEntryKind.Asteroid => "ASTEROID",
            _ => "VESSEL",
        };
    }

    public static class OrbitalTrackingService
    {
        private static readonly List<MapEntry> _entries = new(64);

        /// <summary>The most recent snapshot. Rebuilt by <see cref="Refresh"/>.</summary>
        public static IReadOnlyList<MapEntry> Entries => _entries;

        /// <summary>Sim time of the last refresh, for the map header.</summary>
        public static double LastRefreshSimSeconds { get; private set; }

        /// <summary>
        /// Rebuilds the snapshot. <paramref name="trackingRangeKm"/> comes from the equipped
        /// map device: constructs beyond it are still listed, but flagged out of range, so the
        /// player can see that a better instrument would reach them.
        /// </summary>
        public static void Refresh(double trackingRangeKm = double.PositiveInfinity)
        {
            _entries.Clear();

            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady) return;

            LastRefreshSimSeconds = registry.SimulationSeconds;

            double3 viewerKm = default;
            var origin = SpaceOrigin.Instance;
            if (origin != null) viewerKm = origin.ViewerCosmicKm;

            AddSun(registry);
            AddBodies(registry);
            AddAsteroidBelt(registry);
            AddCraft(registry, viewerKm, trackingRangeKm);
        }

        // ── Celestial bodies ─────────────────────────────────────────────────────
        private static void AddSun(CosmicRegistry registry)
        {
            if (registry.Sun == null) return;
            string name = registry.Sun.settings != null ? registry.Sun.settings.displayName : "Sun";
            _entries.Add(new MapEntry(name, MapEntryKind.Sun, MapMotionState.Landed,
                registry.Sun.positionKmD, null, "", double.NaN, double.NaN, double.NaN,
                double.NaN, double.NaN, 0d, 0d, null, true,
                double.NaN, double.NaN, double.NaN));
        }

        private static void AddBodies(CosmicRegistry registry)
        {
            var bodies = registry.Bodies;
            if (bodies == null) return;

            for (int i = 0; i < bodies.Count; i++)
            {
                var body = bodies[i];
                if (body == null) continue;

                // Bodies already carry solved Keplerian elements — the same ones that drive
                // their motion. Reading them is exact and costs nothing; re-deriving would
                // only introduce a second source of truth that could disagree.
                var orbit = body.orbit;
                double apo = double.NaN, peri = double.NaN, period = double.NaN, incl = double.NaN;
                double raan = double.NaN, argP = double.NaN, nu = double.NaN;
                if (orbit.IsValid)
                {
                    apo = orbit.semiMajorAxisKm * (1d + orbit.eccentricity);
                    peri = orbit.semiMajorAxisKm * (1d - orbit.eccentricity);
                    period = orbit.PeriodSeconds;
                    incl = orbit.inclinationRad * Mathf.Rad2Deg;
                    raan = orbit.raanRad;
                    argP = orbit.argPeriapsisRad;
                    double m = OrbitMath.MeanAnomalyAt(orbit, registry.SimulationSeconds);
                    nu = OrbitMath.TrueAnomaly(OrbitMath.SolveKepler(m, orbit.eccentricity),
                        orbit.eccentricity);
                }

                var parent = body.parentBody;
                string parentName = parent != null ? parent.DisplayName
                    : (registry.Sun != null && registry.Sun.settings != null ? registry.Sun.settings.displayName : "Sun");

                double radiusKm = 0d;
                if (registry.SceneBodies.TryGetValue(body, out var scene) && scene != null)
                    radiusKm = scene.SurfaceRadius / 1000d;

                _entries.Add(new MapEntry(
                    body.DisplayName,
                    body.isPlanet ? MapEntryKind.Planet : MapEntryKind.Moon,
                    MapMotionState.Orbiting,
                    registry.CosmicPositionOf(body),
                    parent, parentName,
                    double.NaN, apo, peri, period, incl,
                    math.length(body.velocityKmS) * 1000d,
                    radiusKm, null, true,
                    raan, argP, nu));
            }
        }

        // ── Player constructs ────────────────────────────────────────────────────
        /// <summary>
        /// Aggregates the system's individually-scattered asteroid rocks into one
        /// "Asteroid Belt" contact: the map draws the shell region (centroid +
        /// radius), not up to 400 sub-pixel rocks. Always shown, like the bodies.
        /// </summary>
        private static void AddAsteroidBelt(CosmicRegistry registry)
        {
            var rocks = registry.Asteroids;
            if (rocks == null || rocks.Count == 0) return;

            double3 centroid = NavigationTarget.BeltCentroidKm(registry);
            double radiusKm = NavigationTarget.BeltRadiusKm(registry, centroid);

            _entries.Add(new MapEntry(NavigationTarget.BeltName, MapEntryKind.Asteroid,
                MapMotionState.Drifting, centroid, null, "", double.NaN, double.NaN,
                double.NaN, double.NaN, double.NaN, 0d, Mathf.Max((float)radiusKm, 1f),
                null, true,
                double.NaN, double.NaN, double.NaN));
        }

        private static void AddCraft(CosmicRegistry registry, double3 viewerKm, double trackingRangeKm)
        {
            var origin = SpaceOrigin.Instance;

            // Only NAMED/CLASSIFIED constructs are tracked. An unnamed scrap heap on the
            // launch pad is not interesting on a system map, and making the player opt in
            // by naming a craft keeps the map readable as a base grows.
            var identities = GridIdentity.All;
            for (int i = 0; i < identities.Count; i++)
            {
                var identity = identities[i];
                if (identity == null) continue;

                var grid = identity.Grid;
                if (grid == null || grid.BlockCount == 0) continue;

                Vector3 scenePos = grid.Body != null ? grid.Body.worldCenterOfMass : grid.transform.position;
                double3 cosmicKm = origin != null ? origin.GetCosmicKm(scenePos) : default;

                bool inRange = double.IsPositiveInfinity(trackingRangeKm)
                    || math.length(cosmicKm - viewerKm) <= trackingRangeKm;

                MapEntryKind kind = identity.Class switch
                {
                    GridClass.Satellite => MapEntryKind.Satellite,
                    GridClass.Station => MapEntryKind.Station,
                    _ => MapEntryKind.Vessel,
                };

                SolveCraft(grid, scenePos, out MapMotionState motion, out BodyInstance parent,
                    out string parentName, out double altKm, out double apoKm, out double periKm,
                    out double period, out double incl, out double speedMs);

                _entries.Add(new MapEntry(identity.DisplayName, kind, motion, cosmicKm,
                    parent, parentName, altKm, apoKm, periKm, period, incl, speedMs,
                    0d, grid, inRange,
                    double.NaN, double.NaN, double.NaN));
            }
        }

        /// <summary>
        /// Classifies one construct's motion. Reuses <see cref="OrbitalTelemetry"/> rather than
        /// re-deriving a conic, so the map and the cockpit flight computer can never disagree
        /// about whether the ship is in orbit.
        /// </summary>
        private static void SolveCraft(GridEntity grid, Vector3 scenePos,
            out MapMotionState motion, out BodyInstance parent, out string parentName,
            out double altitudeKm, out double apoapsisKm, out double periapsisKm,
            out double periodSeconds, out double inclinationDeg, out double speedMs)
        {
            motion = MapMotionState.Landed;
            parent = null;
            parentName = "";
            altitudeKm = apoapsisKm = periapsisKm = periodSeconds = inclinationDeg = double.NaN;

            Vector3 velocity = grid.Body != null ? grid.Body.linearVelocity : Vector3.zero;
            speedMs = velocity.magnitude;

            // An on-rails satellite reports its authored orbit directly. Its rigidbody is
            // parked, so a velocity-based solution would wrongly call it LANDED.
            var rails = grid.GetComponent<OrbitalRails>();
            if (rails != null && rails.IsOnRails)
            {
                rails.Describe(out parent, out parentName, out altitudeKm, out apoapsisKm,
                    out periapsisKm, out periodSeconds, out inclinationDeg, out speedMs);
                motion = MapMotionState.Orbiting;
                return;
            }

            var sample = OrbitalTelemetry.Sample(scenePos, velocity, grid.gravityScale);
            if (!sample.IsAvailable)
            {
                motion = speedMs > 1f ? MapMotionState.Drifting : MapMotionState.Landed;
                return;
            }

            var body = GravityProvider.ActiveBody;
            if (body != null)
            {
                parentName = body.DisplayName;
                altitudeKm = sample.Altitude / 1000d;
                var registry = CosmicRegistry.Instance;
                if (registry != null)
                {
                    foreach (var kv in registry.SceneBodies)
                    {
                        if (kv.Value == body) { parent = kv.Key; break; }
                    }
                }
            }

            apoapsisKm = sample.ApoapsisAltitude / 1000d;
            periapsisKm = sample.PeriapsisAltitude / 1000d;

            motion = sample.State switch
            {
                OrbitalFlightState.Orbiting => MapMotionState.Orbiting,
                OrbitalFlightState.Suborbital => MapMotionState.Suborbital,
                OrbitalFlightState.Escape => MapMotionState.Escaping,
                OrbitalFlightState.DeepSpace => MapMotionState.Drifting,
                OrbitalFlightState.Atmospheric => MapMotionState.Flying,
                _ => MapMotionState.Landed,
            };
        }

        // ── Convenience queries ──────────────────────────────────────────────────

        /// <summary>All tracked satellites currently in orbit. Used by the satellite payloads.</summary>
        public static int CountSatellitesOrbiting(BodyInstance around)
        {
            int n = 0;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Kind != MapEntryKind.Satellite) continue;
                if (e.Motion != MapMotionState.Orbiting) continue;
                if (around != null && e.Parent != around) continue;
                n++;
            }
            return n;
        }

        /// <summary>Formats a duration as the map shows it: 1h 04m, 3d 11h, and so on.</summary>
        public static string FormatPeriod(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0d) return "—";
            if (seconds < 60d) return $"{seconds:0}s";
            if (seconds < 3600d) return $"{(int)(seconds / 60d)}m {(int)(seconds % 60d):00}s";
            if (seconds < 86400d) return $"{(int)(seconds / 3600d)}h {(int)(seconds % 3600d / 60d):00}m";
            return $"{(int)(seconds / 86400d)}d {(int)(seconds % 86400d / 3600d):00}h";
        }

        /// <summary>Formats a distance in km with a sensible unit.</summary>
        public static string FormatKm(double km)
        {
            if (double.IsNaN(km) || double.IsInfinity(km)) return "—";
            double abs = System.Math.Abs(km);
            if (abs < 1d) return $"{km * 1000d:0} m";
            if (abs < 1000d) return $"{km:0.0} km";
            if (abs < 1000000d) return $"{km / 1000d:0.00}k km";
            return $"{km / 1000000d:0.00}M km";
        }
    }
}
