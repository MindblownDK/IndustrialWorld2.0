// Assets/Scripts/VoxelEngine/Cosmos/SolarHazard.cs
//
// The star is DEADLY, and it tells you so before it kills you. This component:
//
//   • Renders a REAL sun mesh at the star's true cosmic position (so flying toward the
//     sun you see in the sky actually approaches the hazard — not a fake 10 km sprite).
//   • Watches the player's cosmic distance to the star and applies a heat warning /
//     damage ramp:
//       > warningRadiusKm    — safe.
//       warning→lethal band  — HUD warnings ("SOL APPROACH — HEAT RISING"), damage ramps.
//       < lethalRadiusKm     — heat damage scales up steeply; inside the corona it is lethal.
//
// Hazard radii are derived from the INNERMOST planet's orbit so they always make sense
// for the system scale: the star's lethal zone sits well inside every planet's orbit.
// Flying into the sun is possible but never accidental.
using Unity.Mathematics;
using UnityEngine;
using VoxelEngine.Player;

namespace VoxelEngine.Cosmos
{
    public class SolarHazard : MonoBehaviour
    {
        [Header("Hazard Zones (km from the star)")]
        [Tooltip("Inside this radius the first warning fires and light heat damage begins. " +
                 "0 = auto-derived from the innermost planet's orbit.")]
        public float warningRadiusKm = 0f;

        [Tooltip("Inside this radius heat damage ramps steeply to lethal. 0 = auto-derived.")]
        public float lethalRadiusKm = 0f;

        [Tooltip("Heat damage per second at the lethal boundary.")]
        public float lethalDamagePerSecond = 60f;

        [Header("Sun Visual")]
        [Tooltip("Radius of the rendered sun mesh (km). ~80 km reads as a dramatic 4-6° disc from the innermost planets.")]
        public float sunVisualRadiusKm = 80f;

        [Tooltip("Corona shell multiplier over the sun radius (soft outer atmosphere of the star).")]
        public float coronaShellMultiplier = 1.45f;

        [Tooltip("Sun core colour.")]
        public Color sunColor = new Color(1f, 0.92f, 0.72f);

        [Header("References")]
        public Transform player;

        private GameObject _sunGO;
        private GameObject _coronaGO;
        private MeshRenderer _sunRenderer;
        private Material _sunMaterial;
        private Material _coronaMaterial;
        private float _nextWarnTime;
        private bool _warned;
        private bool _oneSunLogged;

        private void OnEnable() => EnsureSunVisual();

        private void OnDestroy()
        {
            if (_sunGO != null) Destroy(_sunGO);
        }

        private void EnsureSunVisual()
        {
            if (_sunGO != null) return;
            _sunGO = new GameObject("SunVisual_Real");
            // By instantiating and THEN parenting without keeping world pos, we avoid NaN exceptions
            // if the parent's world position isn't perfectly valid yet.
            _sunGO.transform.SetParent(transform, false);
            _sunGO.transform.localPosition = Vector3.zero;

            var mf = _sunGO.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateSunMesh();

            _sunRenderer = _sunGO.AddComponent<MeshRenderer>();
            // 9.3.0: the sun is a real STAR SURFACE — animated procedural plasma
            // (granulation, starspots, limb darkening), not a flat glow ball.
            Shader shader = Shader.Find("VoxelEngine/StarSurfaceURP")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            _sunMaterial = new Material(shader) { name = "Mat_SunVisual" };
            if (_sunMaterial.HasProperty("_StarColor"))
            {
                _sunMaterial.SetColor("_StarColor", sunColor);
                Color hot = Color.Lerp(sunColor, Color.white, 0.75f);
                _sunMaterial.SetColor("_HotColor", hot);
                Color spot = Color.Lerp(sunColor, Color.black, 0.65f);
                _sunMaterial.SetColor("_SpotColor", spot);
            }
            if (_sunMaterial.HasProperty("_BaseColor")) _sunMaterial.SetColor("_BaseColor", sunColor);
            if (_sunMaterial.HasProperty("_Color")) _sunMaterial.SetColor("_Color", sunColor);
            if (_sunMaterial.HasProperty("_EmissionColor"))
            {
                _sunMaterial.EnableKeyword("_EMISSION");
                _sunMaterial.SetColor("_EmissionColor", sunColor * 2.2f);
            }
            _sunRenderer.sharedMaterial = _sunMaterial;

            // Corona (9.10.0): a soft additive shell around the burning surface so the star
            // reads as an atmosphere, not a crisp billiard ball.
            _coronaGO = new GameObject("SunCorona");
            _coronaGO.transform.SetParent(_sunGO.transform, false);
            _coronaGO.transform.localPosition = Vector3.zero;
            _coronaGO.transform.localScale = Vector3.one * coronaShellMultiplier;
            var coronaMF = _coronaGO.AddComponent<MeshFilter>();
            coronaMF.sharedMesh = CreateSunMesh();
            var coronaMR = _coronaGO.AddComponent<MeshRenderer>();
            coronaMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            coronaMR.receiveShadows = false;
            Shader coronaShader = Shader.Find("VoxelEngine/QuasarGlow")
                                ?? Shader.Find("Universal Render Pipeline/Unlit")
                                ?? Shader.Find("Standard");
            _coronaMaterial = new Material(coronaShader) { name = "Mat_SunCorona" };
            _coronaMaterial.SetColor("_InnerColor", Color.Lerp(sunColor, Color.white, 0.35f));
            _coronaMaterial.SetColor("_OuterColor", Color.Lerp(sunColor, new Color(1f, 0.4f, 0.1f), 0.55f));
            coronaMR.sharedMaterial = _coronaMaterial;
        }

        private void Update()
        {
            var registry = CosmicRegistry.Instance;
            if (registry == null || !registry.IsReady || registry.Sun == null) return;

            var origin = SpaceOrigin.Instance;
            if (origin == null) return;

            // Resolve the real player (also covers late spawns).
            if (player == null || player.GetComponent<PlayerController>() == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc != null) player = pc.transform;
                if (player == null) return;
            }

            // ── Place the REAL sun at its true cosmic position ─────────
            EnsureSunVisual();
            if (_sunGO != null)
            {
                Vector3 sunScene = origin.GetScenePos(registry.Sun.positionKmD);
                if (!float.IsNaN(sunScene.x) && !float.IsNaN(sunScene.y) && !float.IsNaN(sunScene.z))
                {
                    _sunGO.transform.position = sunScene;
                    _sunGO.transform.localScale = Vector3.one * (sunVisualRadiusKm * 1000f);
                }
            }

            // Corona breathes slowly (solar wind pressure pulses).
            if (_coronaMaterial != null)
            {
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 0.6f)
                            + 0.06f * Mathf.Sin(Time.time * 1.7f + 1.3f);
                _coronaMaterial.SetFloat("_Brightness", pulse);
            }

            // ONE SUN policy (7.13.10, re-asserted 9.10.0): the simulation always runs a
            // single star. Authored multi-sun templates are reported, never duplicated.
            if (!_oneSunLogged && registry.Sun != null && registry.Sun.settings != null &&
                registry.Sun.settings.sunCount > 1)
            {
                _oneSunLogged = true;
                Debug.LogWarning("[SolarHazard] ONE SUN policy — the template authors " +
                                 registry.Sun.settings.sunCount + " stars; only one is simulated. " +
                                 "Run Setup Step 52 to normalize the asset.");
            }

            // ── Hazard zones (auto-scale to the system) ────────────────
            float warn = warningRadiusKm > 0f ? warningRadiusKm : ResolveWarningRadiusKm(registry);
            float lethal = lethalRadiusKm > 0f
                ? lethalRadiusKm
                : Mathf.Min(warn * 0.55f, ResolveLethalRadiusKm(registry));
            if (lethal <= 0f) lethal = warn * 0.55f;

            double3 cosmic = origin.GetCosmicKm(player.position);
            double distKm = math.length(cosmic - registry.Sun.positionKmD);
            if (distKm > warn) { _warned = false; return; }

            float t = Mathf.Clamp01((float)((warn - distKm) / Mathf.Max(1f, warn - lethal)));
            if (!_warned)
            {
                _warned = true;
                _nextWarnTime = 0f;
            }

            if (Time.unscaledTime >= _nextWarnTime)
            {
                _nextWarnTime = Time.unscaledTime + 2f;
                string msg = t < 0.35f
                    ? "SOL APPROACH — HEAT RISING"
                    : t < 0.75f
                        ? "SOL FLARE — CRITICAL HEAT, TURN BACK"
                        : "SOL CORONA — CERTAIN DEATH";
                VoxelEngine.UI.BuildFeedbackHud.Show("☀ " + msg,
                    $"{(int)distKm} km from star", null,
                    Color.Lerp(new Color(1f, 0.72f, 0.25f), new Color(1f, 0.2f, 0.1f), t));
            }

            // Damage: ramps from 0 at the warning edge to lethal at the lethal boundary.
            float dmgPerSec = t <= 0f ? 0f : Mathf.Pow(t, 1.6f) * lethalDamagePerSecond;
            if (dmgPerSec > 0f)
            {
                var stats = PlayerStats.Instance != null ? PlayerStats.Instance : player.GetComponent<PlayerStats>();
                if (stats != null)
                {
                    if (t >= 0.9f) PlayerStats.SetDeathCause("VAPORIZED BY THE STAR");
                    stats.TakeDamage(dmgPerSec * Time.deltaTime);
                }
            }
        }

        /// <summary>Warning radius: 80% of the innermost planet's orbit (or 2200 km fallback).</summary>
        private static float ResolveWarningRadiusKm(CosmicRegistry registry)
        {
            double innermost = double.MaxValue;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var b = registry.Bodies[i];
                if (b == null || !b.isPlanet) continue;
                innermost = math.min(innermost, b.orbit.semiMajorAxisKm);
            }
            if (innermost >= double.MaxValue) return 2200f;
            return Mathf.Max(1600f, (float)(innermost * 0.8));
        }

        /// <summary>Lethal radius: 45% of the innermost planet's orbit (or 1100 km fallback).</summary>
        private static float ResolveLethalRadiusKm(CosmicRegistry registry)
        {
            double innermost = double.MaxValue;
            for (int i = 0; i < registry.Bodies.Count; i++)
            {
                var b = registry.Bodies[i];
                if (b == null || !b.isPlanet) continue;
                innermost = math.min(innermost, b.orbit.semiMajorAxisKm);
            }
            if (innermost >= double.MaxValue) return 1100f;
            return Mathf.Max(800f, (float)(innermost * 0.45));
        }

        private static Mesh CreateSunMesh()
        {
            // Low-poly icosphere, scaled up massively at runtime — enough curvature to
            // read as a burning sphere without a heavy mesh.
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] v =
            {
                new Vector3(-1, t, 0).normalized, new Vector3(1, t, 0).normalized,
                new Vector3(-1, -t, 0).normalized, new Vector3(1, -t, 0).normalized,
                new Vector3(0, -1, t).normalized, new Vector3(0, 1, t).normalized,
                new Vector3(0, -1, -t).normalized, new Vector3(0, 1, -t).normalized,
                new Vector3(t, 0, -1).normalized, new Vector3(t, 0, 1).normalized,
                new Vector3(-t, 0, -1).normalized, new Vector3(-t, 0, 1).normalized,
            };
            verts.AddRange(v);
            tris.AddRange(new[]
            {
                0,11, 5,  0, 5, 1,  0, 1, 7,  0, 7,10,  0,10,11,
                1, 5, 9,  5,11, 4, 11,10, 2, 10, 7, 6,  7, 1, 8,
                3, 9, 4,  3, 4, 2,  3, 2, 6,  3, 6, 8,  3, 8, 9,
                4, 9, 5,  2, 4,11,  6, 2,10,  8, 6, 7,  9, 8, 1,
            });
            // One subdivision for a smoother sphere (162 verts — trivial).
            var cache = new System.Collections.Generic.Dictionary<long, int>();
            var nt = new System.Collections.Generic.List<int>(tris.Count * 4);
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int idx)) return idx;
                Vector3 mid = ((verts[a] + verts[b]) * 0.5f).normalized;
                idx = verts.Count; verts.Add(mid); cache[key] = idx; return idx;
            }
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.Add(a); nt.Add(ab); nt.Add(ca);
                nt.Add(b); nt.Add(bc); nt.Add(ab);
                nt.Add(c); nt.Add(ca); nt.Add(bc);
                nt.Add(ab); nt.Add(bc); nt.Add(ca);
            }
            tris.Clear(); tris.AddRange(nt);

            var mesh = new Mesh { name = "SunVisualMesh" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
