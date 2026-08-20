// Assets/Scripts/VoxelEngine/Cosmos/SingularityBeacons.cs
//
// LONG-RANGE VISIBILITY for the singularity remnants (Phase 5).
//
// The black hole / quasar sit hundreds of thousands of km from the star — far beyond
// the camera far clip. Like the true-position planet beacons, these beacons keep the
// bodies honestly findable: a direction-pinned billboard rendered AT the true cosmic
// direction, scaled to the body's REAL angular size (boosted to a minimum, exactly
// like real night-sky navigation). The beacon's disc tilt matches the real disc axis
// projected into the view plane, so what you see from afar is what you get up close.
//
// As the player closes inside the real-render window the beacon fades out and the
// SingularityRenderer's real geometry carries the view. Nothing follows the player —
// only the projection point moves with the camera, the body stays at its real location.
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace VoxelEngine.Cosmos
{
    [DisallowMultipleComponent]
    public sealed class SingularityBeacons : MonoBehaviour
    {
        [Tooltip("Minimum apparent angular radius (rad) a singularity is displayed at (~0.008 ≈ 0.46°).")]
        public float minApparentAngularRadians = 0.008f;

        [Tooltip("Minimum apparent angular radius (rad) for the QUASAR (it is the beacon of the two).")]
        public float quasarMinApparentAngularRadians = 0.010f;

        [Tooltip("Far-distance pin cap (km). Beyond this the beacon is a true direction pin (like a star); " +
                 "inside it the beacon sits at the REAL remaining distance, so flying toward it genuinely " +
                 "approaches it, and the handoff to real geometry is seamless.")]
        public float pinCapKm = 62000f;

        [Tooltip("The beacon crossfades into the real geometry between pinCapKm and this fade-in distance (km).")]
        public float realGeometryFadeInKm = 50000f;

        [Tooltip("Seconds between refreshes.")]
        public float refreshInterval = 0.25f;

        private sealed class Beacon
        {
            public SingularityInstance instance;
            public GameObject go;
            public GameObject haloGO, discGO, jet1GO, jet2GO;
            public Material haloMat, discMat, jetMat;
        }

        private readonly List<Beacon> _beacons = new List<Beacon>();
        private float _timer;
        private Transform _viewer;

        private void OnDestroy()
        {
            foreach (var b in _beacons)
            {
                DestroyMaterial(ref b.haloMat);
                DestroyMaterial(ref b.discMat);
                DestroyMaterial(ref b.jetMat);
                if (b.go != null) Destroy(b.go);
            }
            _beacons.Clear();
        }

        private static void DestroyMaterial(ref Material m)
        {
            if (m != null) Destroy(m);
            m = null;
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;

            var registry = CosmicRegistry.Instance;
            var origin = SpaceOrigin.Instance;
            if (registry == null || !registry.IsReady || origin == null) return;

            if (_viewer == null)
            {
                _viewer = origin.viewer != null
                    ? origin.viewer
                    : (Camera.main != null ? Camera.main.transform : null);
                if (_viewer == null) return;
            }

            // Create beacons for any singularity without one (they never disappear once created).
            for (int i = 0; i < registry.Singularities.Count; i++)
            {
                var s = registry.Singularities[i];
                if (s == null || FindBeacon(s) != null) continue;
                _beacons.Add(CreateBeacon(s));
            }

            for (int i = 0; i < _beacons.Count; i++)
            {
                var b = _beacons[i];
                if (b == null || b.instance == null) continue;

                double3 viewerCosmic = origin.GetCosmicKm(_viewer.position);
                double3 toSing = b.instance.positionKmD - viewerCosmic;
                double distKm = math.length(toSing);
                if (distKm < 1d || double.IsNaN(distKm)) { b.go.SetActive(false); continue; }

                // Beacon projects in the TRUE direction. Its scene distance CONVERGES:
                // far away it pins at pinCapKm (a star-like direction pin — warp-lock to
                // travel), inside the pin cap it sits at the real remaining distance, so
                // flying toward it actually approaches it (the old fixed 9 km pin made it
                // appear to run away forever).
                Vector3 dir = (Vector3)(float3)(toSing / distKm);
                if (float.IsNaN(dir.x)) { b.go.SetActive(false); continue; }

                // Fade band: the beacon hands over to the real geometry inside the render
                // window — it fades between pinCapKm and realGeometryFadeInKm, and the
                // real horizon/disc/halo appear at pinCapKm, so there is no gap where the
                // remnant is invisible (the old band hid the beacon before the real
                // geometry appeared).
                float fade = Mathf.Clamp01(((float)distKm - realGeometryFadeInKm)
                                           / Mathf.Max(1f, pinCapKm - realGeometryFadeInKm));
                if (fade <= 0.001f) { b.go.SetActive(false); continue; }
                b.go.SetActive(true);

                float sceneDistM = (float)math.min(distKm, pinCapKm) * 1000f;
                Vector3 beaconPos = _viewer.position + dir * sceneDistM;
                b.go.transform.position = beaconPos;

                // Real angular radius, boosted to the navigation minimum (the boost fades
                // as the real geometry grows, so the crossfade never doubles the size).
                bool isQuasar = b.instance.kind == SingularityKind.Quasar;
                float minAngular = isQuasar ? quasarMinApparentAngularRadians : minApparentAngularRadians;
                float angular = (float)(b.instance.eventHorizonKm / distKm);
                float shown = Mathf.Max(angular, minAngular);
                float size = shown * sceneDistM * 2f;

                // Disc tilt matches the REAL disc axis projected into the view plane.
                Vector3 axis = b.instance.discAxis.sqrMagnitude > 0.001f ? b.instance.discAxis.normalized : Vector3.up;
                Vector3 proj = Vector3.ProjectOnPlane(axis, dir);
                if (proj.sqrMagnitude < 0.001f) proj = Vector3.up;   // face-on: pick any in-plane up
                Quaternion billboard = Quaternion.LookRotation(-dir, proj.normalized);

                // ── Halo ──
                b.haloGO.transform.rotation = billboard;
                b.haloGO.transform.localScale = Vector3.one * size * 4.5f * fade;

                // ── Disc ──
                b.discGO.transform.rotation = billboard;
                b.discGO.transform.localScale = new Vector3(size * 1.9f, size * 1.9f, 1f) * fade;

                // ── Jets (quasar only), stretched along the projected axis ──
                if (isQuasar && b.jet1GO != null)
                {
                    float jLen = size * 2.6f;
                    float jWid = size * 0.10f;
                    Vector3 up = proj.normalized;
                    Vector3 jetDir = (billboard * Vector3.up).normalized;
                    PlaceBeaconJet(b.jet1GO, beaconPos, jetDir, up, jLen, jWid, fade);
                    PlaceBeaconJet(b.jet2GO, beaconPos, -jetDir, up, jLen, jWid, fade);
                }
                if (b.jet1GO != null) b.jet1GO.SetActive(isQuasar);
                if (b.jet2GO != null) b.jet2GO.SetActive(isQuasar);

                // ── Colours ──
                bool bh = b.instance.blackHoleSettings != null;
                bool qs = b.instance.quasarSettings != null;
                Color core = qs ? b.instance.quasarSettings.coreColor : bh ? b.instance.blackHoleSettings.coreColor : Color.white;
                Color mid = qs ? b.instance.quasarSettings.midColor : bh ? b.instance.blackHoleSettings.midColor : Color.yellow;
                Color outer = qs ? b.instance.quasarSettings.outerColor : bh ? b.instance.blackHoleSettings.outerColor : Color.red;
                float brightness = qs ? b.instance.quasarSettings.brightness : bh ? b.instance.blackHoleSettings.brightness : 1.5f;

                if (b.haloMat != null)
                {
                    b.haloMat.SetFloat("_Brightness", brightness * 0.4f * fade);
                    b.haloMat.SetColor("_InnerColor", core);
                    b.haloMat.SetColor("_OuterColor", outer);
                }
                if (b.discMat != null)
                {
                    b.discMat.SetFloat("_Brightness", brightness * fade);
                    b.discMat.SetColor("_CoreColor", core);
                    b.discMat.SetColor("_MidColor", mid);
                    b.discMat.SetColor("_OuterColor", outer);
                }
                if (b.jetMat != null)
                {
                    b.jetMat.SetFloat("_Brightness", brightness * 0.75f * fade);
                    Color jet = qs ? b.instance.quasarSettings.jetColor : new Color(0.4f, 0.6f, 1f, 0.9f);
                    b.jetMat.SetColor("_CoreColor", jet);
                    if (b.jetMat.HasProperty("_EdgeColor"))
                        b.jetMat.SetColor("_EdgeColor", jet * 0.35f);
                }
            }
        }

        private static void PlaceBeaconJet(GameObject jet, Vector3 beaconPos, Vector3 jetDir, Vector3 up,
            float length, float width, float fade)
        {
            jet.transform.position = beaconPos + jetDir * (length * 0.5f);
            jet.transform.rotation = Quaternion.LookRotation(jetDir, up) * Quaternion.Euler(0f, -90f, 0f);
            jet.transform.localScale = new Vector3(length, width, 1f) * fade;
        }

        private Beacon FindBeacon(SingularityInstance instance)
        {
            for (int i = 0; i < _beacons.Count; i++)
                if (_beacons[i] != null && _beacons[i].instance == instance) return _beacons[i];
            return null;
        }

        private static Beacon CreateBeacon(SingularityInstance instance)
        {
            var go = new GameObject("SingularityBeacon_" + instance.DisplayName);
            go.transform.SetParent(null);

            var beacon = new Beacon { instance = instance, go = go };

            beacon.haloGO = CreateLayer("Halo", CreateQuad());
            beacon.discGO = CreateLayer("Disc", CreateQuad());
            beacon.jet1GO = CreateLayer("Jet1", CreateJetQuad());
            beacon.jet2GO = CreateLayer("Jet2", CreateJetQuad());
            beacon.haloGO.transform.SetParent(go.transform, true);
            beacon.discGO.transform.SetParent(go.transform, true);
            beacon.jet1GO.transform.SetParent(go.transform, true);
            beacon.jet2GO.transform.SetParent(go.transform, true);

            beacon.haloMat = CreateMaterial("QuasarGlow");
            beacon.discMat = CreateMaterial("QuasarAccretionDisc");
            beacon.jetMat = CreateMaterial("QuasarJet");
            beacon.haloGO.GetComponent<MeshRenderer>().sharedMaterial = beacon.haloMat;
            beacon.discGO.GetComponent<MeshRenderer>().sharedMaterial = beacon.discMat;
            beacon.jet1GO.GetComponent<MeshRenderer>().sharedMaterial = beacon.jetMat;
            beacon.jet2GO.GetComponent<MeshRenderer>().sharedMaterial = beacon.jetMat;

            return beacon;
        }

        private static GameObject CreateLayer(string name, Mesh mesh)
        {
            var go = new GameObject(name);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = mesh;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private static Material CreateMaterial(string shaderName)
        {
            var shader = Shader.Find("VoxelEngine/" + shaderName);
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            return new Material(shader) { name = "Mat_" + shaderName };
        }

        private static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "BeaconQuad" };
            mesh.vertices = new Vector3[]
            {
                new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0),
                new(-0.5f,  0.5f, 0), new(0.5f,  0.5f, 0),
            };
            mesh.uv = new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateJetQuad()
        {
            var mesh = new Mesh { name = "BeaconJetQuad" };
            mesh.vertices = new Vector3[]
            {
                new(-0.5f, -0.5f, 0), new(0.5f, -0.5f, 0),
                new(-0.5f,  0.5f, 0), new(0.5f,  0.5f, 0),
            };
            mesh.uv = new Vector2[] { new(0,0), new(1,0), new(0,1), new(1,1) };
            mesh.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
