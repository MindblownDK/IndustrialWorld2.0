// Assets/Scripts/VoxelEngine/Cosmos/DistantBodyBeacons.cs
//
// TRUE-POSITION BEACONS (9.5.0) — honest long-range visibility for REAL bodies.
//
// A planet 8 km wide at 40,000 km subtends far less than a pixel: physically
// present, optically invisible. Real night skies solve this the same way we do —
// planets show as bright points. Each beacon is a small emissive sphere placed AT
// THE BODY'S REAL POSITION (parented to it, zero offset — nothing follows the
// player, nothing is fake), scaled so the body's apparent size never drops below
// a findable minimum. As you approach and the real surface grows past that
// minimum, the beacon fades away and the actual terrain carries the view.
using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public sealed class DistantBodyBeacons : MonoBehaviour
    {
        [Tooltip("Minimum apparent size (radians) a body is displayed at (0.009 ≈ 0.5° — about the full moon). Below this, the beacon boosts the body to stay clearly findable for navigation; above it, the real surface carries itself and the beacon fades.")]
        public float minApparentAngularRadians = 0.009f;

        [Tooltip("Seconds between beacon refreshes.")]
        public float refreshInterval = 0.25f;

        private sealed class Beacon
        {
            public CelestialBody body;
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

            foreach (var kv in registry.SceneBodies)
            {
                CelestialBody body = kv.Value;
                if (body == null || body.settings == null) continue;

                if (!_beacons.TryGetValue(body, out Beacon beacon))
                {
                    beacon = CreateBeacon(body);
                    _beacons.Add(body, beacon);
                }
                if (beacon.go == null) continue;

                Vector3 bodyPos = body.transform.position;
                if (float.IsNaN(bodyPos.x) || float.IsNaN(bodyPos.y) || float.IsNaN(bodyPos.z))
                {
                    beacon.go.SetActive(false);
                    continue;
                }

                float dist = Vector3.Distance(_viewer.position, bodyPos);
                float radius = Mathf.Max(1f, body.SurfaceRadius);
                float realAngular = radius / Mathf.Max(1f, dist);

                // Real surface already visible → no beacon (fade band 1.0–1.6×).
                float fade = Mathf.Clamp01((minApparentAngularRadians * 1.6f - realAngular)
                                           / (minApparentAngularRadians * 0.6f));
                if (fade <= 0.02f || dist < radius * 3f)
                {
                    beacon.go.SetActive(false);
                    continue;
                }

                if (!beacon.go.activeSelf) beacon.go.SetActive(true);
                // Boost the REAL body to the minimum apparent size at its REAL position.
                // The fade shrinks the beacon back inside the real body as the actual
                // surface takes over (URP Unlit is opaque — scale IS the fade).
                float shownRadius = Mathf.Max(radius * 0.98f, dist * minApparentAngularRadians * fade);
                beacon.go.transform.localScale = Vector3.one * shownRadius;
                if (beacon.material != null)
                {
                    Color c = BeaconColor(body);
                    if (beacon.material.HasProperty("_BaseColor")) beacon.material.SetColor("_BaseColor", c);
                    else if (beacon.material.HasProperty("_Color")) beacon.material.SetColor("_Color", c);
                }
            }
        }

        private static Color BeaconColor(CelestialBody body)
        {
            var s = body.settings;
            if (s != null && s.displayColor.a > 0.01f) return s.displayColor;
            return new Color(0.95f, 0.97f, 1f, 1f);
        }

        private static Beacon CreateBeacon(CelestialBody body)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"Beacon_{body.DisplayName}";
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            // Parent to the body itself: the beacon IS the body's position, always.
            go.transform.SetParent(body.transform, false);
            go.transform.localPosition = Vector3.zero;

            var renderer = go.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color")
                         ?? Shader.Find("Standard");
            var material = new Material(shader) { name = $"Mat_Beacon_{body.DisplayName}" };
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return new Beacon { body = body, go = go, renderer = renderer, material = material };
        }
    }
}
