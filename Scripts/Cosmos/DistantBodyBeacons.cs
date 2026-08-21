// Assets/Scripts/VoxelEngine/Cosmos/DistantBodyBeacons.cs
//
// ╔══════════════════════════════════════════════════════════════════════╗
// ║       DISTANT BODY BEACONS — planets visible from ANYWHERE (9.15)     ║
// ║                                                                      ║
// ║  Long-range visibility for REAL bodies, done honestly:               ║
// ║                                                                      ║
// ║   • Each beacon is a SHADED PLANET DISC (DistantPlanet shader):       ║
// ║     sun-lit day side, soft terminator, dark starlit night side and   ║
// ║     an atmosphere rim — a real little world, not a flat dot.         ║
// ║   • CONVERGING PROJECTION: the beacon sits in the body's TRUE        ║
// ║     direction at min(real distance, 62,000 km) — far away it pins    ║
// ║     inside the camera far clip (the old beacons sat at the body's    ║
// ║     REAL position and were culled by the far plane: invisible),      ║
// ║     and inside the pin it converges so approaching feels real.       ║
// ║   • Apparent size = the body's TRUE angular size, boosted to a       ║
// ║     navigation minimum (≈0.57°) that shrinks away as you close in.   ║
// ║   • The beacon crossfades out between 60,000 and 80,000 km —         ║
// ║     exactly the band where the body's REAL LOD geometry takes over   ║
// ║     (far clip guaranteed by CosmosBootstrap). No invisible gap,      ║
// ║     no double image: the beacon's apparent size equals the real      ║
// ║     body's at the handover.                                          ║
// ╚══════════════════════════════════════════════════════════════════════╝
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public sealed class DistantBodyBeacons : MonoBehaviour
    {
        [Tooltip("Minimum apparent size (radians) a body is displayed at (0.010 ≈ 0.57° — slightly larger than the full moon). Below this, the beacon boosts the body to stay clearly findable; above it, the real surface carries itself.")]
        public float minApparentAngularRadians = 0.010f;

        [Tooltip("Beyond this real distance (m) the beacon is pinned — inside it, it sits at the body's real position (converging projection).")]
        public float pinCapMeters = 62000000f;

        [Tooltip("The beacon fades out between these REAL distances (m): the band where the body's real LOD takes over. Must match the far-clip coverage (CosmosBootstrap).")]
        public float fadeInMeters = 60000000f;

        [Tooltip("Beacon fully visible beyond this real distance (m).")]
        public float fadeOutMeters = 80000000f;

        [Tooltip("Seconds between beacon refreshes.")]
        public float refreshInterval = 0.25f;

        private sealed class Beacon
        {
            public CelestialBody body;
            public BodyInstance instance;
            public GameObject go;
            public MeshRenderer renderer;
            public Material material;
        }

        private readonly Dictionary<CelestialBody, Beacon> _beacons = new();
        private float _timer;
        private Transform _viewer;

        private void OnDestroy()
        {
            foreach (var kv in _beacons)
            {
                if (kv.Value.material != null) Destroy(kv.Value.material);
                if (kv.Value.go != null) Destroy(kv.Value.go);
            }
            _beacons.Clear();
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;

            var registry = CosmicRegistry.Instance;
            if (registry == null || registry.SceneBodies == null) return;
            if (_viewer == null)
            {
                var origin = SpaceOrigin.Instance;
                _viewer = origin != null && origin.viewer != null
                    ? origin.viewer
                    : (Camera.main != null ? Camera.main.transform : null);
                if (_viewer == null) return;
            }

            Vector3 viewerPos = _viewer.position;
            if (float.IsNaN(viewerPos.x) || float.IsNaN(viewerPos.y) || float.IsNaN(viewerPos.z)) return;

            foreach (var kv in registry.SceneBodies)
            {
                CelestialBody body = kv.Value;
                if (body == null || body.settings == null) continue;

                if (!_beacons.TryGetValue(body, out Beacon beacon))
                {
                    beacon = CreateBeacon(body, kv.Key);
                    _beacons.Add(body, beacon);
                }
                if (beacon.go == null) continue;

                Vector3 bodyPos = body.transform.position;
                if (float.IsNaN(bodyPos.x) || float.IsNaN(bodyPos.y) || float.IsNaN(bodyPos.z))
                {
                    beacon.go.SetActive(false);
                    continue;
                }

                float dist = Vector3.Distance(viewerPos, bodyPos);
                float radius = Mathf.Max(1f, body.SurfaceRadius);
                float realAngular = radius / Mathf.Max(1f, dist);

                // ── Crossfade band: beacon fully visible beyond fadeOut, gone at
                // fadeIn where the real LOD geometry owns the view ──
                float fade = Mathf.Clamp01((dist - fadeInMeters) / Mathf.Max(1f, fadeOutMeters - fadeInMeters));
                if (fade <= 0.02f || dist < radius * 3f)
                {
                    beacon.go.SetActive(false);
                    continue;
                }
                if (!beacon.go.activeSelf) beacon.go.SetActive(true);

                // ── Converging projection: true direction, pinned inside the far clip ──
                Vector3 dir = (bodyPos - viewerPos) / Mathf.Max(1f, dist);
                float sceneDistM = Mathf.Min(dist, pinCapMeters);
                beacon.go.transform.position = viewerPos + dir * sceneDistM;

                // ── Apparent size: the REAL angular size, boosted toward the navigation
                // minimum while far (the boost fades as the real body grows) ──
                float shownAngular = Mathf.Max(realAngular, minApparentAngularRadians * fade);
                float diameter = shownAngular * sceneDistM * 2f;
                beacon.go.transform.localScale = Vector3.one * diameter;

                // ── Per-body material feeds ──
                if (beacon.material != null)
                {
                    Color baseColor = BeaconColor(body);
                    // The terminator uses the body→sun direction — the REAL sun
                    // direction as seen from that world (matches the lit side of
                    // the real LOD at the handover).
                    Vector3 localSun = Vector3.right;
                    if (registry.Sun != null)
                    {
                        double3 bodyCosmic = registry.CosmicPositionOf(kv.Key);
                        double3 toSun = registry.Sun.positionKmD - bodyCosmic;
                        Vector3 ls = (Vector3)(float3)math.normalizesafe(toSun, new double3(1d, 0d, 0d));
                        if (!float.IsNaN(ls.x) && ls.sqrMagnitude > 0.01f) localSun = ls.normalized;
                    }
                    if (beacon.material.HasProperty("_BaseColor")) beacon.material.SetColor("_BaseColor", baseColor);
                    if (beacon.material.HasProperty("_SunDir")) beacon.material.SetVector("_SunDir", new Vector4(localSun.x, localSun.y, localSun.z, 0f));
                    if (beacon.material.HasProperty("_AtmoColor"))
                        beacon.material.SetColor("_AtmoColor", AtmoColor(body, baseColor));
                    if (beacon.material.HasProperty("_AtmoStrength"))
                        beacon.material.SetFloat("_AtmoStrength", body.HasAtmosphere ? 0.85f : 0.22f);
                    // Opaque disc — the crossfade is size-based, so brightness must not
                    // pop at the handover: keep the disc at full strength.
                }
            }
        }

        private static Color BeaconColor(CelestialBody body)
        {
            var s = body.settings;
            if (s != null && s.displayColor.a > 0.01f) return s.displayColor;
            return new Color(0.92f, 0.95f, 1f, 1f);
        }

        private static Color AtmoColor(CelestialBody body, Color baseColor)
        {
            // Atmospheric worlds get a blue-ish rim; airless bodies a subtle grey rim.
            if (body.HasAtmosphere)
                return Color.Lerp(baseColor, new Color(0.36f, 0.55f, 0.95f), 0.55f);
            return baseColor * 0.8f;
        }

        private static Beacon CreateBeacon(CelestialBody body, BodyInstance instance)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"PlanetBeacon_{body.DisplayName}";
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // World-space beacon: the converging projection positions it each refresh
            // (parenting at the body's real position put it beyond the far clip —
            // the "planets invisible in space" bug).
            go.transform.SetParent(null, false);

            var renderer = go.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("VoxelEngine/DistantPlanet")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Standard");
            var material = new Material(shader) { name = $"Mat_PlanetBeacon_{body.DisplayName}" };
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return new Beacon { body = body, instance = instance, go = go, renderer = renderer, material = material };
        }
    }
}
