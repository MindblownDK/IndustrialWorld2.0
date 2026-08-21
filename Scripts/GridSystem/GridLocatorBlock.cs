// Assets/Scripts/VoxelEngine/GridSystem/GridLocatorBlock.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║            STAR LOCATOR — navigation grid block (Phase 5)            ║
// ║                                                                      ║
// ║  A powered grid block that pinpoints a celestial destination and     ║
// ║  projects a TRUE waypoint marker toward it:                          ║
// ║                                                                      ║
// ║   • Targets: the black hole, the quasar, the sun, every planet and   ║
// ║     moon in the system.                                              ║
// ║   • AUTO mode tracks the nearest body; SPECIFIC mode locks a chosen  ║
// ║     target from the panel (◀ ▶ cycle).                               ║
// ║   • The waypoint is a real scene marker projected at the true        ║
// ║     direction, pinned at 62,000 km until you get close — the same    ║
// ║     honest convergence the singularity beacons use.                  ║
// ║   • The WARP DRIVE reads the active waypoint: aim at the marker and  ║
// ║     jump — any range, any target (planets get an orbital arrival,    ║
// ║     singularities their standoff corridor, the sun a safe 50,000 km  ║
// ║     halo orbit).                                                     ║
// ║   • Draws 6 kW while enabled; unpowered = no waypoint.               ║
// ║                                                                      ║
// ║  Item / prefab / recipe / research authored by Setup Step 55.        ║
// ╚══════════════════════════════════════════════════════════════════════╝
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Cosmos;

namespace VoxelEngine.GridSystem
{
    public class GridLocatorBlock : GridBlock, IGridDataProvider
    {
        public enum LocatorMode { Auto, Specific }

        [Tooltip("Power drawn (W) while the locator is enabled.")]
        public float powerDrawWatts = 6000f;

        [Tooltip("Auto = nearest body; Specific = the selected target index.")]
        public LocatorMode mode = LocatorMode.Auto;

        [Tooltip("Selected target when mode = Specific (index into the shared target list).")]
        public int selectedTargetIndex = -1;

        [Tooltip("Waypoint marker size in metres.")]
        public float waypointMarkerSize = 1.4f;

        public override float PowerDraw => Enabled ? powerDrawWatts : 0f;

        // ── Live state ──
        public bool IsTracking { get; private set; }
        public string TargetName { get; private set; } = "—";
        public double TargetDistanceKm { get; private set; } = -1d;
        public string Status { get; private set; } = "Idle";

        // ── Shared static surface (the warp drive + panels read these) ──
        public static GridLocatorBlock ActiveLocator { get; private set; }
        public static bool HasWaypoint => ActiveLocator != null && ActiveLocator.IsTracking
                                          && ActiveLocator._waypoint != null;
        public static Vector3 WaypointScenePosition =>
            ActiveLocator != null && ActiveLocator._waypoint != null
                ? ActiveLocator._waypoint.transform.position : Vector3.zero;
        public static string WaypointTargetName =>
            ActiveLocator != null ? ActiveLocator.TargetName : "—";

        /// <summary>Total selectable targets: singularities + sun + planets/moons.</summary>
        public static int TargetCount
        {
            get
            {
                var registry = CosmicRegistry.Instance;
                if (registry == null || !registry.IsReady) return 0;
                int n = registry.Singularities != null ? registry.Singularities.Count : 0;
                if (registry.Sun != null) n++;
                return n + (registry.Bodies != null ? registry.Bodies.Count : 0);
            }
        }

        /// <summary>Display name of the target at <paramref name="index"/> (panel cycle).</summary>
        public static string TargetNameAt(int index)
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady) return "—";
            int singCount = registry.Singularities != null ? registry.Singularities.Count : 0;
            if (index >= 0 && index < singCount)
            {
                var s = registry.Singularities[index];
                return s != null ? s.DisplayName : "—";
            }
            if (index == singCount && registry.Sun != null)
                return registry.Sun.settings != null ? registry.Sun.settings.displayName : "Sun";
            int b = index - singCount - (registry.Sun != null ? 1 : 0);
            if (b >= 0 && registry.Bodies != null && b < registry.Bodies.Count)
                return registry.Bodies[b] != null ? registry.Bodies[b].DisplayName : "—";
            return "—";
        }

        private GameObject _waypoint;
        private Renderer _markerRenderer;
        private float _refreshTimer;
        private float _staticRegisterTimer;
        private BodyInstance _trackedBody;      // null for sun / singularities
        private SingularityInstance _trackedSingularity;
        private bool _trackedIsSun;

        public override void OnPlaced()
        {
            base.OnPlaced();
            blockName = "Star Locator";
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            if (ActiveLocator == this) ActiveLocator = null;
            DestroyWaypoint();
        }

        private void OnDestroy() => DestroyWaypoint();

        private void Update()
        {
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0f) return;
            _refreshTimer = 0.5f;
            Refresh();
        }

        private void Refresh()
        {
            IsTracking = false;

            if (!Enabled) { Status = "Disabled"; DestroyWaypoint(); return; }
            if (Grid == null) { Status = "No Grid"; DestroyWaypoint(); return; }
            if (!Grid.HasPower) { Status = "No Power"; DestroyWaypoint(); return; }

            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (registry == null || !registry.IsReady || origin == null)
            {
                Status = "No star map";
                DestroyWaypoint();
                return;
            }

            double3 selfCosmic = origin.GetCosmicKm(transform.position);

            // Resolve the current target.
            int kind;                       // 0 = singularity, 1 = sun, 2 = body
            int targetIdx;
            double3 targetPos;
            double distKm;
            if (!ResolveTarget(registry, selfCosmic, out kind, out targetIdx, out targetPos, out distKm))
            {
                Status = "No targets";
                DestroyWaypoint();
                return;
            }

            _trackedSingularity = kind == 0 && registry.Singularities != null
                && targetIdx >= 0 && targetIdx < registry.Singularities.Count
                ? registry.Singularities[targetIdx] : null;
            _trackedBody = kind == 2 ? registry.Bodies[targetIdx] : null;
            _trackedIsSun = kind == 1;

            IsTracking = true;
            TargetDistanceKm = distKm;

            if (kind == 0) TargetName = _trackedSingularity.DisplayName;
            else if (kind == 1) TargetName = registry.Sun != null && registry.Sun.settings != null
                ? registry.Sun.settings.displayName : "Sun";
            else TargetName = _trackedBody.DisplayName;

            Status = $"Tracking {TargetName} ({distKm:0} km)";

            // ── Waypoint marker: true direction, converging pin ──
            double3 toTarget = targetPos - selfCosmic;
            double3 dirD = math.normalizesafe(toTarget, new double3(0d, 1d, 0d));
            Vector3 dir = (Vector3)(float3)dirD;
            float pinKm = (float)math.min(distKm, 62000d);
            Vector3 markerPos = transform.position + dir * pinKm * 1000f;

            if (_waypoint == null)
            {
                _waypoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _waypoint.name = $"LocatorWaypoint ({blockName})";
                var col = _waypoint.GetComponent<Collider>();
                if (col != null) Destroy(col);
                _markerRenderer = _waypoint.GetComponent<Renderer>();
                if (_markerRenderer != null)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    _markerRenderer.material = new Material(shader);
                }
                // The marker must not be culled by a short far plane — the camera far
                // clip already covers 62,000 km when singularities exist, but keep the
                // marker near the block when needed anyway (pin is inside that window).
            }
            if (_waypoint != null)
            {
                _waypoint.transform.position = markerPos;
                _waypoint.transform.localScale = Vector3.one * waypointMarkerSize;
                if (_markerRenderer != null && _markerRenderer.material != null)
                {
                    Color c = kind == 1 ? new Color(1f, 0.75f, 0.25f)
                            : kind == 0 ? new Color(0.75f, 0.4f, 1f)
                            : new Color(0.2f, 0.9f, 1f);
                    float pulse = 1.15f + 0.45f * Mathf.Sin(Time.unscaledTime * 3.2f);
                    var mat = _markerRenderer.material;
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c * pulse);
                    if (mat.HasProperty("_EmissionColor")) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", c * pulse); }
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", c * pulse);
                }
            }

            // Register as the active locator (last enabled wins; unregister when dead).
            if (ActiveLocator == null || ActiveLocator == this || ActiveLocator.Grid == null || !ActiveLocator.IsTracking)
                ActiveLocator = this;
        }

        /// <summary>
        /// Resolve the current target. Returns kind (0 singularity / 1 sun / 2 body),
        /// the index within that list, the cosmic position and distance.
        /// </summary>
        private bool ResolveTarget(CosmicRegistry registry, double3 selfCosmic,
            out int kind, out int idx, out double3 pos, out double dist)
        {
            kind = 0; idx = -1; pos = double3.zero; dist = double.MaxValue;
            bool any = false;

            int singCount = registry.Singularities != null ? registry.Singularities.Count : 0;
            int bodyCount = registry.Bodies != null ? registry.Bodies.Count : 0;
            bool hasSun = registry.Sun != null;

            if (mode == LocatorMode.Specific)
            {
                int sel = selectedTargetIndex;
                if (sel >= 0 && sel < singCount)
                {
                    kind = 0; idx = sel; pos = registry.Singularities[sel].positionKmD; any = true;
                }
                else if (sel == singCount && hasSun)
                {
                    kind = 1; idx = 0; pos = registry.Sun.positionKmD; any = true;
                }
                else
                {
                    int b = sel - singCount - (hasSun ? 1 : 0);
                    if (b >= 0 && b < bodyCount)
                    {
                        kind = 2; idx = b; pos = registry.CosmicPositionOf(registry.Bodies[b]); any = true;
                    }
                }
                if (any) dist = math.length(pos - selfCosmic);
                return any;
            }

            // AUTO: nearest across every target class.
            double best = double.MaxValue;
            for (int i = 0; i < singCount; i++)
            {
                var s = registry.Singularities[i];
                if (s == null) continue;
                double d = math.length(s.positionKmD - selfCosmic);
                if (d < best) { best = d; kind = 0; idx = i; pos = s.positionKmD; }
            }
            if (hasSun)
            {
                double d = math.length(registry.Sun.positionKmD - selfCosmic);
                if (d < best) { best = d; kind = 1; idx = 0; pos = registry.Sun.positionKmD; }
            }
            for (int i = 0; i < bodyCount; i++)
            {
                var b = registry.Bodies[i];
                if (b == null) continue;
                double d = math.length(registry.CosmicPositionOf(b) - selfCosmic);
                if (d < best) { best = d; kind = 2; idx = i; pos = registry.CosmicPositionOf(b); }
            }
            if (best >= double.MaxValue) return false;
            dist = best;
            return true;
        }

        private void DestroyWaypoint()
        {
            if (_waypoint != null) Destroy(_waypoint);
            _waypoint = null;
            _markerRenderer = null;
        }

        // ── Warp integration ───────────────────────────────────────
        /// <summary>
        /// Compute the warp arrival for the current target (null body = deep-space frame).
        /// Singularities: standoff corridor. Planets/moons: 90 km orbit altitude.
        /// Sun: safe 50,000 km halo standoff.
        /// </summary>
        public bool TryGetArrival(double3 gridCosmic, out double3 arrival, out BodyInstance body, out string arrivalName)
        {
            arrival = double3.zero;
            body = _trackedBody;
            arrivalName = TargetName;

            if (!IsTracking) return false;

            if (_trackedSingularity != null)
            {
                double3 toS = _trackedSingularity.positionKmD - gridCosmic;
                double3 fromSing = -math.normalizesafe(toS, new double3(0d, 1d, 0d));
                double3 arrivalDir = fromSing;
                Vector3 axis = _trackedSingularity.discAxis.sqrMagnitude > 0.001f
                    ? _trackedSingularity.discAxis.normalized : Vector3.up;
                double3 axisD = new double3(axis.x, axis.y, axis.z);
                double align = math.dot(arrivalDir, axisD);
                if (math.abs(align) > 0.85d)
                {
                    double3 proj = arrivalDir - axisD * align;
                    double3 projN = math.normalizesafe(proj, axisD);
                    arrivalDir = math.normalize(projN * 0.85d + axisD * math.sign(align) * math.sqrt(1d - 0.85d * 0.85d));
                }
                double standoff = _trackedSingularity.eventHorizonKm
                                  + System.Math.Max(500d, _trackedSingularity.standoffArrivalKm);
                arrival = _trackedSingularity.positionKmD + arrivalDir * standoff;
                arrivalName = $"{TargetName} standoff ({(int)_trackedSingularity.standoffArrivalKm:0} km from horizon)";
                return true;
            }

            if (_trackedIsSun)
            {
                var registry = CosmicRegistry.Instance;
                if (registry == null || registry.Sun == null) return false;
                double3 toSun = registry.Sun.positionKmD - gridCosmic;
                double3 fromSun = -math.normalizesafe(toSun, new double3(0d, 1d, 0d));
                arrival = registry.Sun.positionKmD + fromSun * 50000d;
                arrivalName = $"{TargetName} halo (50,000 km standoff)";
                return true;
            }

            if (_trackedBody != null)
            {
                var registry = CosmicRegistry.Instance;
                if (registry == null) return false;
                double3 bodyPos = registry.CosmicPositionOf(_trackedBody);
                double3 toBody = bodyPos - gridCosmic;
                double3 radial = math.normalizesafe(toBody, new double3(0d, 1d, 0d));
                double surfaceKm = _trackedBody.settings != null ? _trackedBody.settings.radiusKm : 500d;
                arrival = bodyPos + radial * (surfaceKm + 90d);
                arrivalName = $"{TargetName} orbit (90 km altitude)";
                return true;
            }

            return false;
        }

        // ── LCD data provider ──────────────────────────────────────
        public string SourceName => blockName;
        public string DataCategory => "Navigation";
        public string GetDisplayData()
        {
            string dist = TargetDistanceKm >= 0d ? $"{TargetDistanceKm:0} km" : "—";
            return $"STAR LOCATOR\n{Status}\nTarget {TargetName}\nDistance {dist}\nMode {(mode == LocatorMode.Auto ? "AUTO" : "SPECIFIC")}";
        }
    }
}
