// Assets/Scripts/VoxelEngine/Cosmos/OrbitalRails.cs
//
// Puts a player construct into a stable, analytic orbit.
//
// WHY THIS EXISTS
// A rigidbody cannot hold an orbit. Float precision, a 50 Hz fixed step and the
// fact that an unloaded chunk stops simulating all mean a physics-integrated
// satellite will decay, drift, or simply stop existing while the player is away.
// That is fatal for a feature whose entire promise is "put a station up and it
// stays there".
//
// So a construct the player commits to orbit switches to the SAME Keplerian
// propagation the planets and moons already use. It becomes kinematic and is
// placed from solved elements each frame. It cannot decay, it keeps its orbit
// while streamed out, and the map can draw an exact ellipse instead of guessing.
//
// The player is never trapped: any thrust input releases the rails and hands the
// construct back to physics with the correct instantaneous velocity, so leaving
// orbit feels continuous rather than modal.

using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridEntity))]
    public sealed class OrbitalRails : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private bool _onRails;
        [SerializeField] private double _semiMajorAxisKm;
        [SerializeField] private double _eccentricity;
        [SerializeField] private double _inclinationRad;
        [SerializeField] private double _raanRad;
        [SerializeField] private double _argPeriapsisRad;
        [SerializeField] private double _meanAnomaly0;
        [SerializeField] private double _epochSeconds;

        /// <summary>Minimum clearance above the surface for a legal orbit, as a fraction of radius.</summary>
        public const double MinimumAltitudeFraction = 0.02d;

        /// <summary>Speed below which residual drift counts as "held still" when committing.</summary>
        private const float CommitSpeedTolerance = 0.35f;

        private GridEntity _grid;
        private BodyInstance _parent;
        private CelestialBody _parentScene;
        private OrbitElements _elements;

        public bool IsOnRails => _onRails;
        public BodyInstance Parent => _parent;

        private GridEntity Grid
        {
            get
            {
                if (_grid == null) _grid = GetComponent<GridEntity>();
                return _grid;
            }
        }

        // ── Committing to orbit ──────────────────────────────────────────────────

        /// <summary>
        /// Why a construct cannot be committed to orbit right now, or null if it can.
        /// Returned as text because every caller wants to tell the player.
        /// </summary>
        public static string ValidateCommit(GridEntity grid)
        {
            if (grid == null) return "No construct selected.";
            if (grid.Body == null) return "This construct has no physics body.";

            var body = GravityProvider.ActiveBody;
            if (body == null) return "Not within a planet or moon's sphere of influence.";

            Vector3 pos = grid.Body.worldCenterOfMass;
            var sample = OrbitalTelemetry.Sample(pos, grid.Body.linearVelocity, grid.gravityScale);
            if (!sample.IsAvailable) return "No orbital solution at this position.";

            // The construct must actually be in space. Committing inside an atmosphere
            // would freeze a ship mid-descent and look like a bug rather than a feature.
            if (!AtmosphereManager.IsInSpace(pos))
                return "Too low — climb clear of the atmosphere before committing to orbit.";

            double minAltitude = body.SurfaceRadius * MinimumAltitudeFraction;
            if (sample.Altitude < minAltitude)
                return $"Too low — orbit must be at least {minAltitude / 1000d:0.0} km above the surface.";

            // Refuse a path that hits the ground. An orbit whose periapsis is underground is
            // not an orbit, and silently circularising it would steal the player's intent.
            if (sample.State == OrbitalFlightState.Suborbital)
                return "Suborbital — raise periapsis above the surface before committing.";
            if (sample.State == OrbitalFlightState.Escape)
                return "Escaping — slow down to a closed orbit before committing.";

            return null;
        }

        /// <summary>
        /// Freezes the construct onto the orbit it is currently flying. Returns false with a
        /// reason if the state is not orbit-worthy.
        /// </summary>
        public bool Commit(out string reason)
        {
            reason = ValidateCommit(Grid);
            if (reason != null) return false;

            var registry = CosmicRegistry.Instance;
            var scene = GravityProvider.ActiveBody;
            if (registry == null || scene == null) { reason = "No cosmic frame available."; return false; }

            _parentScene = scene;
            _parent = null;
            foreach (var kv in registry.SceneBodies)
            {
                if (kv.Value == scene) { _parent = kv.Key; break; }
            }
            if (_parent == null) { reason = "This body is not part of the system registry."; return false; }

            var rb = Grid.Body;

            // Work in km relative to the parent's centre, which is the frame the Keplerian
            // elements are expressed in.
            double3 rKm = ToKm(rb.worldCenterOfMass - scene.transform.position);
            double3 vKmS = ToKm(rb.linearVelocity);

            double mu = _parent.gravitationalParamKm3S2;
            if (mu <= 0d) { reason = "Parent body has no gravitational parameter."; return false; }

            if (!TryDeriveElements(rKm, vKmS, mu, out _elements))
            {
                reason = "Could not solve a closed orbit from the current state.";
                return false;
            }

            _semiMajorAxisKm = _elements.semiMajorAxisKm;
            _eccentricity = _elements.eccentricity;
            _inclinationRad = _elements.inclinationRad;
            _raanRad = _elements.raanRad;
            _argPeriapsisRad = _elements.argPeriapsisRad;
            _meanAnomaly0 = _elements.meanAnomaly0;
            _epochSeconds = registry.SimulationSeconds;

            _onRails = true;

            // Park the rigidbody. Kinematic means physics stops fighting the analytic
            // position, and nothing can nudge the station out of its orbit by collision.
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            return true;
        }

        /// <summary>
        /// Returns the construct to physics, handing back the exact velocity its orbit implies
        /// so the release is continuous rather than a dead stop.
        /// </summary>
        public void Release()
        {
            if (!_onRails) return;
            _onRails = false;

            var rb = Grid != null ? Grid.Body : null;
            if (rb == null) return;

            Vector3 velocity = CurrentSceneVelocity();
            rb.isKinematic = false;
            rb.linearVelocity = velocity;
        }

        // ── Propagation ──────────────────────────────────────────────────────────
        private void LateUpdate()
        {
            if (!_onRails) return;

            var registry = CosmicRegistry.Instance;
            if (registry == null || _parent == null || _parentScene == null) return;

            // The parent may have been unloaded (streamed out or destroyed). Hold position
            // rather than teleporting the station to a stale origin.
            if (!_parentScene.isActiveAndEnabled) return;

            double t = registry.SimulationSeconds;
            double3 posKm = OrbitMath.PositionKm(Elements, t);
            if (double.IsNaN(posKm.x) || double.IsNaN(posKm.y) || double.IsNaN(posKm.z)) return;

            Vector3 local = FromKm(posKm);
            Vector3 world = _parentScene.transform.position + local;

            var rb = Grid.Body;
            if (rb != null) rb.position = world;
            transform.position = world;
        }

        private OrbitElements Elements
        {
            get
            {
                if (_elements.IsValid) return _elements;
                _elements = new OrbitElements
                {
                    semiMajorAxisKm = _semiMajorAxisKm,
                    eccentricity = _eccentricity,
                    inclinationRad = _inclinationRad,
                    raanRad = _raanRad,
                    argPeriapsisRad = _argPeriapsisRad,
                    meanAnomaly0 = _meanAnomaly0,
                    gravitationalParamKm3S2 = _parent != null ? _parent.gravitationalParamKm3S2 : 0d,
                    timeScale = 1d,
                };
                return _elements;
            }
        }

        private Vector3 CurrentSceneVelocity()
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null) return Vector3.zero;
            double3 vKmS = OrbitMath.VelocityKmS(Elements, registry.SimulationSeconds);
            if (double.IsNaN(vKmS.x)) return Vector3.zero;
            return FromKm(vKmS);
        }

        // ── Readout for the map ──────────────────────────────────────────────────
        public void Describe(out BodyInstance parent, out string parentName, out double altitudeKm,
            out double apoapsisKm, out double periapsisKm, out double periodSeconds,
            out double inclinationDeg, out double speedMs)
        {
            parent = _parent;
            parentName = _parent != null ? _parent.DisplayName : "";

            var e = Elements;
            double surfaceKm = _parentScene != null ? _parentScene.SurfaceRadius / 1000d : 0d;
            apoapsisKm = e.semiMajorAxisKm * (1d + e.eccentricity) - surfaceKm;
            periapsisKm = e.semiMajorAxisKm * (1d - e.eccentricity) - surfaceKm;
            periodSeconds = e.PeriodSeconds;
            inclinationDeg = e.inclinationRad * Mathf.Rad2Deg;

            var registry = CosmicRegistry.Instance;
            double t = registry != null ? registry.SimulationSeconds : 0d;
            double3 posKm = OrbitMath.PositionKm(e, t);
            altitudeKm = math.length(posKm) - surfaceKm;
            speedMs = math.length(OrbitMath.VelocityKmS(e, t)) * 1000d;
        }

        // ── Orbit determination ──────────────────────────────────────────────────
        // Standard state-vector → classical-elements conversion. Kept local rather than
        // added to OrbitMath because OrbitMath is the propagation half (elements → state)
        // and is used on the hot path for every body; this runs once, on commit.
        private static bool TryDeriveElements(double3 rKm, double3 vKmS, double mu, out OrbitElements e)
        {
            e = default;

            double r = math.length(rKm);
            double v = math.length(vKmS);
            if (r < 0.0001d || mu <= 0d) return false;

            double3 h = math.cross(rKm, vKmS);          // specific angular momentum
            double hMag = math.length(h);
            if (hMag < 1e-9d) return false;             // radial trajectory: no orbital plane

            double energy = v * v * 0.5d - mu / r;
            if (energy >= -1e-12d) return false;        // parabolic or hyperbolic: not a closed orbit

            double a = -mu / (2d * energy);
            if (a <= 0d) return false;

            double3 eVec = (math.cross(vKmS, h) / mu) - (rKm / r);
            double ecc = math.length(eVec);
            if (ecc >= 1d) return false;

            double inc = math.acos(math.clamp(h.y / hMag, -1d, 1d));

            // Node vector points at the ascending node. The reference plane here is the
            // engine's XZ plane, so "up" for the inclination measurement is +Y.
            double3 n = new double3(-h.z, 0d, h.x);
            double nMag = math.length(n);

            double raan = 0d;
            double argPe;

            const double EquatorialTolerance = 1e-8d;
            if (nMag < EquatorialTolerance)
            {
                // Equatorial orbit: the ascending node is undefined, so fold the argument of
                // periapsis into the longitude of periapsis measured from +X.
                raan = 0d;
                argPe = math.atan2(eVec.z, eVec.x);
                if (h.y < 0d) argPe = -argPe;
            }
            else
            {
                raan = math.atan2(n.z, n.x);
                if (ecc > EquatorialTolerance)
                {
                    argPe = math.acos(math.clamp(math.dot(n, eVec) / (nMag * ecc), -1d, 1d));
                    if (eVec.y < 0d) argPe = -argPe;
                }
                else
                {
                    argPe = 0d;   // circular: periapsis undefined, anchor it at the node
                }
            }

            // True anomaly now, then back out the mean anomaly at epoch.
            double trueAnomaly;
            if (ecc > EquatorialTolerance)
            {
                trueAnomaly = math.acos(math.clamp(math.dot(eVec, rKm) / (ecc * r), -1d, 1d));
                if (math.dot(rKm, vKmS) < 0d) trueAnomaly = -trueAnomaly;
            }
            else if (nMag >= EquatorialTolerance)
            {
                trueAnomaly = math.acos(math.clamp(math.dot(n, rKm) / (nMag * r), -1d, 1d));
                if (rKm.y < 0d) trueAnomaly = -trueAnomaly;
            }
            else
            {
                trueAnomaly = math.atan2(rKm.z, rKm.x);
                if (h.y < 0d) trueAnomaly = -trueAnomaly;
            }

            double eccentricAnomaly = 2d * math.atan2(
                math.sqrt(1d - ecc) * math.sin(trueAnomaly * 0.5d),
                math.sqrt(1d + ecc) * math.cos(trueAnomaly * 0.5d));
            double meanAnomaly = eccentricAnomaly - ecc * math.sin(eccentricAnomaly);

            // OrbitMath propagates from M0 at t = 0, so rewind the mean anomaly from "now"
            // back to the simulation epoch. Without this the station would jump on commit.
            var registry = CosmicRegistry.Instance;
            double now = registry != null ? registry.SimulationSeconds : 0d;
            double meanMotion = math.sqrt(mu / (a * a * a));
            double m0 = meanAnomaly - meanMotion * now;
            m0 %= math.PI * 2d;

            e = new OrbitElements
            {
                semiMajorAxisKm = a,
                eccentricity = ecc,
                inclinationRad = inc,
                raanRad = raan,
                argPeriapsisRad = argPe,
                meanAnomaly0 = m0,
                gravitationalParamKm3S2 = mu,
                timeScale = 1d,
            };
            return e.IsValid;
        }

        // Scene units are metres; the orbital layer is km.
        private static double3 ToKm(Vector3 metres) => new double3(metres.x, metres.y, metres.z) / 1000d;
        private static Vector3 FromKm(double3 km) => (Vector3)(float3)(km * 1000d);

        // ── Save/load support ────────────────────────────────────────────────────

        /// <summary>Snapshot for persistence. Zeroed when the construct is not on rails.</summary>
        public void CaptureState(out bool onRails, out double[] elements, out string parentName)
        {
            onRails = _onRails;
            parentName = _parent != null ? _parent.DisplayName : "";
            elements = new[]
            {
                _semiMajorAxisKm, _eccentricity, _inclinationRad,
                _raanRad, _argPeriapsisRad, _meanAnomaly0, _epochSeconds,
            };
        }

        /// <summary>Restores a saved orbit. Re-resolves the parent by name against the registry.</summary>
        public void RestoreState(double[] elements, string parentName)
        {
            if (elements == null || elements.Length < 7) return;

            _semiMajorAxisKm = elements[0];
            _eccentricity = elements[1];
            _inclinationRad = elements[2];
            _raanRad = elements[3];
            _argPeriapsisRad = elements[4];
            _meanAnomaly0 = elements[5];
            _epochSeconds = elements[6];

            var registry = CosmicRegistry.Instance;
            if (registry == null) return;

            foreach (var kv in registry.SceneBodies)
            {
                if (kv.Key != null && kv.Key.DisplayName == parentName)
                {
                    _parent = kv.Key;
                    _parentScene = kv.Value;
                    break;
                }
            }
            if (_parent == null) return;

            _elements = default;   // force a rebuild with the restored values
            _onRails = true;

            var rb = Grid != null ? Grid.Body : null;
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }
}
