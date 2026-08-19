// Assets/Scripts/VoxelEngine/Cosmos/OrbitMath.cs
//
// Pure Keplerian orbital mechanics in DOUBLE precision (kilometres / seconds).
//
// This is the "real orbits" core of the space system. Every celestial body in the
// solar system is propagated with the same mathematics real astrodynamics uses:
//
//   1. Mean anomaly M(t) = M0 + n·t, with mean motion n = sqrt(μ / a³).
//   2. Kepler's equation M = E − e·sin(E) solved by Newton iteration → eccentric
//      anomaly E.
//   3. True anomaly ν and radius r = a·(1 − e·cos E) from E.
//   4. Position rotated from the perifocal frame into the reference frame by the
//      classical rotation sequence Rz(Ω)·Rx(i)·Rz(ω) (RAAN → inclination → argument
//      of periapsis).
//   5. Velocity from the vis-viva equation, expressed perifocally:
//        vx = −sqrt(μ/p)·sin ν,   vy = sqrt(μ/p)·(e + cos ν),   p = a(1 − e²).
//
// The result is exact Keplerian motion: elliptical orbits, real orbital periods
// T = 2π√(a³/μ), and physically correct orbital velocities — the standard math of
// real astrodynamics, scaled to a compressed (game-sized) solar system so planets
// are reachable and orbits are observable within a play session.
using System;
using Unity.Mathematics;

namespace VoxelEngine.Cosmos
{
    /// <summary>
    /// Classical orbital elements for one body, relative to its parent (the sun for
    /// planets, the planet for moons, the moon for sub-moons).
    /// </summary>
    [Serializable]
    public struct OrbitElements
    {
        /// <summary>Semi-major axis in km (a).</summary>
        public double semiMajorAxisKm;

        /// <summary>Eccentricity (0 = circular, &lt; 1 = elliptical).</summary>
        public double eccentricity;

        /// <summary>Inclination in radians (i).</summary>
        public double inclinationRad;

        /// <summary>Longitude of the ascending node in radians (Ω).</summary>
        public double raanRad;

        /// <summary>Argument of periapsis in radians (ω).</summary>
        public double argPeriapsisRad;

        /// <summary>Mean anomaly at simulation epoch in radians (M0).</summary>
        public double meanAnomaly0;

        /// <summary>Gravitational parameter of the PARENT in km³/s² (μ).</summary>
        public double gravitationalParamKm3S2;

        /// <summary>Authored orbital-speed multiplier (template orbitSpeed). 0/1 = nominal.</summary>
        public double timeScale;

        public bool IsValid => gravitationalParamKm3S2 > 0.000001 && semiMajorAxisKm > 0.01;

        /// <summary>Mean motion n = √(μ/a³) in rad/s.</summary>
        public double MeanMotion => math.sqrt(gravitationalParamKm3S2 / (semiMajorAxisKm * semiMajorAxisKm * semiMajorAxisKm));

        /// <summary>Orbital period in seconds: T = 2π√(a³/μ).</summary>
        public double PeriodSeconds => 2.0 * math.PI * math.sqrt(
            (semiMajorAxisKm * semiMajorAxisKm * semiMajorAxisKm) / gravitationalParamKm3S2);

        /// <summary>Unit vector perpendicular to the orbit plane (from Ω, i).</summary>
        public double3 PlaneNormal
        {
            get
            {
                double cO = math.cos(raanRad), sO = math.sin(raanRad);
                double ci = math.cos(inclinationRad), si = math.sin(inclinationRad);
                // z-axis of the Rz(Ω)·Rx(i) rotation.
                return new double3(sO * si, ci, cO * si);
            }
        }
    }

    /// <summary>
    /// Static Keplerian propagation helpers. All positions are km, all velocities
    /// km/s, all times seconds, all angles radians.
    /// </summary>
    public static class OrbitMath
    {
        /// <summary>Solve Kepler's equation M = E − e·sin(E) for E (Newton iteration).</summary>
        public static double SolveKepler(double meanAnomaly, double eccentricity)
        {
            double M = meanAnomaly;
            // Elliptical solver only: authored/corrupt e ≥ 1 would diverge to NaN —
            // clamp to a near-parabolic ellipse instead (9.3.0 hardening).
            double e = math.clamp(eccentricity, 0.0, 0.95);
            if (e < 1e-9)
            {
                // Circular orbit: E = M exactly.
                return M;
            }

            // Fold into [-π, π] for stable iteration.
            double twoPi = 2.0 * math.PI;
            M = M % twoPi;
            if (M < -math.PI) M += twoPi;
            else if (M > math.PI) M -= twoPi;

            // Good first guess for e < 1: E = M (or M + e·sin M for larger e).
            double E = M + e * math.sin(M);
            for (int i = 0; i < 24; i++)
            {
                double f = E - e * math.sin(E) - M;
                double fp = 1.0 - e * math.cos(E);
                double step = f / fp;
                E -= step;
                if (math.abs(step) < 1e-12) break;
            }
            return E;
        }

        /// <summary>True anomaly ν from the eccentric anomaly E.</summary>
        public static double TrueAnomaly(double eccentricAnomaly, double eccentricity)
        {
            double E = eccentricAnomaly;
            double e = math.clamp(eccentricity, 0.0, 0.95);   // e ≥ 1 → √(1−e) = NaN
            double nu = 2.0 * math.atan2(
                math.sqrt(1.0 + e) * math.sin(E * 0.5),
                math.sqrt(1.0 - e) * math.cos(E * 0.5));
            return nu;
        }

        /// <summary>
        /// Position on the orbit at simulation time t (km), in the parent's reference
        /// frame. The parent's own position must be added by the caller.
        /// </summary>
        public static double3 PositionKm(OrbitElements o, double tSeconds)
        {
            if (!o.IsValid) return double3.zero;

            double n = o.MeanMotion;
            double M = o.meanAnomaly0 + n * tSeconds * math.max(0.001, o.timeScale);
            double E = SolveKepler(M, o.eccentricity);
            double nu = TrueAnomaly(E, o.eccentricity);
            double r = o.semiMajorAxisKm * (1.0 - o.eccentricity * math.cos(E));

            // Perifocal frame position.
            double x = r * math.cos(nu);
            double y = r * math.sin(nu);

            return RotatePerifocalToReference(new double3(x, y, 0.0), o);
        }

        /// <summary>
        /// Velocity on the orbit at simulation time t (km/s), from vis-viva. The
        /// parent's own velocity must be added by the caller.
        /// </summary>
        public static double3 VelocityKmS(OrbitElements o, double tSeconds)
        {
            if (!o.IsValid) return double3.zero;

            double n = o.MeanMotion;
            double M = o.meanAnomaly0 + n * tSeconds * math.max(0.001, o.timeScale);
            double E = SolveKepler(M, o.eccentricity);
            double nu = TrueAnomaly(E, o.eccentricity);

            double a = o.semiMajorAxisKm;
            double e = o.eccentricity;
            double p = a * (1.0 - e * e);
            if (p < 1e-9) return double3.zero;

            double mu = o.gravitationalParamKm3S2;
            double vx = -math.sqrt(mu / p) * math.sin(nu);
            double vy = math.sqrt(mu / p) * (e + math.cos(nu));

            return RotatePerifocalToReference(new double3(vx, vy, 0.0), o);
        }

        /// <summary>Current mean anomaly (rad) at time t — used for legacy HUD fields.</summary>
        public static double MeanAnomalyAt(OrbitElements o, double tSeconds)
        {
            if (!o.IsValid) return 0.0;
            return o.meanAnomaly0 + o.MeanMotion * tSeconds * math.max(0.001, o.timeScale);
        }

        /// <summary>Rotation Rz(Ω)·Rx(i)·Rz(ω) applied to a perifocal vector.</summary>
        private static double3 RotatePerifocalToReference(double3 perifocal, OrbitElements o)
        {
            double cw = math.cos(o.argPeriapsisRad), sw = math.sin(o.argPeriapsisRad);
            double ci = math.cos(o.inclinationRad), si = math.sin(o.inclinationRad);
            double cO = math.cos(o.raanRad), sO = math.sin(o.raanRad);

            double x = perifocal.x, y = perifocal.y, z = perifocal.z;

            // Rotate by ω about z.
            double x1 = cw * x - sw * y;
            double y1 = sw * x + cw * y;
            double z1 = z;

            // Rotate by i about x.
            double x2 = x1;
            double y2 = ci * y1 - si * z1;
            double z2 = si * y1 + ci * z1;

            // Rotate by Ω about z.
            double x3 = cO * x2 - sO * y2;
            double y3 = sO * x2 + cO * y2;
            double z3 = z2;

            return new double3(x3, y3, z3);
        }
    }
}
