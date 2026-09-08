// Assets/Scripts/VoxelEngine/Thermal/BlockDamageVisual.cs
//
// Visible damage & heat for any block: grid hull blocks, static placed blocks and
// tiered building pieces. One component per block, attached lazily the first time
// the block is hurt or heated, so pristine blocks cost nothing.
//
//   • Builds a thin overlay shell (one MeshRenderer per authored renderer) that draws
//     procedural cracks, soot and incandescent glow with BlockDamageOverlayURP.
//   • Drives the overlay through a MaterialPropertyBlock — no per-block material
//     instances, so hundreds of scorched blocks still batch.
//   • Spawns a pooled smoke/ember particle system while the block is burning.
//   • Values are eased, never snapped, so a hit "blooms" cracks and a cooling hull
//     fades from white through orange to dull red the way real steel does.
//
// Persistence: cracks are derived from the block's saved HP, so a damaged ship that
// is reloaded still looks damaged. Scorch is derived from the highest heat the block
// has seen this session and is cosmetic only.

using System.Collections.Generic;
using UnityEngine;

namespace VoxelEngine.Thermal
{
    [DisallowMultipleComponent]
    public class BlockDamageVisual : MonoBehaviour
    {
        private const string OverlayName = "Generated_DamageOverlay";
        private const string SmokeName = "Generated_DamageSmoke";

        /// <summary>Damage fraction below which no cracks are drawn (a scratch is not a fracture).</summary>
        private const float CrackVisibleFrom = 0.06f;

        /// <summary>Heat glow above which smoke and embers spawn.</summary>
        private const float SmokeFromGlow = 0.22f;

        private static readonly int DamageId = Shader.PropertyToID("_Damage");
        private static readonly int HeatId = Shader.PropertyToID("_Heat");
        private static readonly int ScorchId = Shader.PropertyToID("_Scorch");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int CrackScaleId = Shader.PropertyToID("_CrackScale");

        private static Material s_overlayMaterial;
        private static Material s_smokeMaterial;
        private static bool s_shaderMissingLogged;

        private readonly List<MeshRenderer> _overlays = new();
        private readonly List<Renderer> _sources = new();
        private MaterialPropertyBlock _props;
        private ParticleSystem _smoke;
        private float _seed;
        private float _crackScale = 1.6f;

        private float _targetDamage01;
        private float _targetHeat01;
        private float _shownDamage01;
        private float _shownHeat01;
        private float _scorch01;
        private float _idleSeconds;
        private bool _built;
        private bool _dirty;

        /// <summary>Highest heat glow this block has shown; drives permanent soot.</summary>
        public float PeakHeat01 { get; private set; }

        /// <summary>
        /// True for renderers this system generated. Paint, texturizer, highlight and
        /// material-swapping tools skip these so the overlay keeps its own shader.
        /// </summary>
        public static bool IsGeneratedOverlay(Renderer renderer)
            => renderer != null && renderer.name == OverlayName;

        /// <summary>Attach (or fetch) the visual for a block root.</summary>
        public static BlockDamageVisual For(Component blockRoot)
        {
            if (blockRoot == null) return null;
            var visual = blockRoot.GetComponent<BlockDamageVisual>();
            if (visual == null) visual = blockRoot.gameObject.AddComponent<BlockDamageVisual>();
            return visual;
        }

        /// <summary>
        /// Report structural damage as a 0..1 fraction (1 = destroyed). Cheap to call
        /// every frame: it only marks the component dirty.
        /// </summary>
        public static void ReportDamage(Component blockRoot, float damage01)
        {
            if (blockRoot == null) return;
            damage01 = Mathf.Clamp01(damage01);
            var existing = blockRoot.GetComponent<BlockDamageVisual>();
            if (existing == null && damage01 < CrackVisibleFrom) return;   // pristine: stay free
            For(blockRoot)?.SetDamage(damage01);
        }

        /// <summary>Report temperature in °C. Glow begins around 450 °C.</summary>
        public static void ReportTemperature(Component blockRoot, float temperatureC)
        {
            if (blockRoot == null) return;
            float glow = ThermalRules.GlowIntensity01(temperatureC);
            var existing = blockRoot.GetComponent<BlockDamageVisual>();
            if (existing == null && glow <= 0.001f) return;
            For(blockRoot)?.SetHeat(glow);
        }

        public void SetDamage(float damage01)
        {
            damage01 = Mathf.Clamp01(damage01);
            if (Mathf.Approximately(_targetDamage01, damage01)) return;
            // A fresh hit blooms cracks quickly; repair (HP going up) heals them slowly.
            if (damage01 > _targetDamage01 + 0.12f) _shownDamage01 = Mathf.Max(_shownDamage01, damage01 * 0.6f);
            _targetDamage01 = damage01;
            _dirty = true;
            enabled = true;
        }

        public void SetHeat(float glow01)
        {
            glow01 = Mathf.Clamp01(glow01);
            _targetHeat01 = glow01;
            _dirty = true;
            enabled = true;
        }

        private void Awake()
        {
            _seed = Random.Range(0f, 64f);
        }

        private void OnDestroy()
        {
            ReleaseSmoke();
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Cracks bloom fast and heal slowly; glow follows the thermal solve, which is
            // already slewed, so it just needs light smoothing to hide the 0.25 s ticks.
            float crackRate = _targetDamage01 > _shownDamage01 ? 6f : 1.2f;
            _shownDamage01 = Mathf.MoveTowards(_shownDamage01, _targetDamage01, crackRate * dt);
            _shownHeat01 = Mathf.Lerp(_shownHeat01, _targetHeat01, 1f - Mathf.Exp(-4f * dt));

            if (_shownHeat01 > PeakHeat01) PeakHeat01 = _shownHeat01;
            // Soot: the hotter the block has been, the darker it stays. Burning off is not
            // modelled — a scorched hull stays scorched until the block is replaced.
            float sootTarget = Mathf.Clamp01(PeakHeat01 * 0.9f);
            _scorch01 = Mathf.MoveTowards(_scorch01, sootTarget, 0.35f * dt);

            bool anythingToShow = _shownDamage01 >= CrackVisibleFrom || _shownHeat01 > 0.004f || _scorch01 > 0.01f;
            if (anythingToShow && !_built) Build();
            if (!_built) { enabled = false; return; }

            Apply();
            UpdateSmoke(dt);

            // Sleep once the overlay has fully converged and nothing is hot, so a
            // permanently cracked block does not tick forever.
            bool converged = Mathf.Abs(_shownDamage01 - _targetDamage01) < 0.002f
                             && _shownHeat01 < 0.004f && _targetHeat01 < 0.004f
                             && Mathf.Abs(_scorch01 - sootTarget) < 0.002f;
            if (converged && !_dirty)
            {
                _idleSeconds += dt;
                if (_idleSeconds > 1.5f) { ReleaseSmoke(); enabled = false; }
            }
            else _idleSeconds = 0f;
            _dirty = false;
        }

        // ── Overlay construction ────────────────────────────────────────────

        private void Build()
        {
            var material = OverlayMaterial;
            if (material == null) return;

            _props ??= new MaterialPropertyBlock();
            _sources.Clear();
            GetComponentsInChildren(true, _sources);

            float largestExtent = 0.5f;
            for (int i = 0; i < _sources.Count; i++)
            {
                var src = _sources[i];
                if (!IsOverlayCandidate(src)) continue;
                var filter = src.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                var go = new GameObject(OverlayName);
                go.layer = src.gameObject.layer;
                go.transform.SetParent(src.transform, false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = filter.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                mr.allowOcclusionWhenDynamic = false;
                _overlays.Add(mr);

                float extent = src.bounds.extents.magnitude;
                if (extent > largestExtent) largestExtent = extent;
            }

            // Scale the crack cells to the block so a 0.5 m detail block and a 2.5 m hull
            // plate both read as cracked steel rather than one solid smear or a fine mesh.
            _crackScale = Mathf.Clamp(1.4f / Mathf.Max(0.25f, largestExtent), 0.35f, 4f);
            _built = _overlays.Count > 0;
        }

        private static bool IsOverlayCandidate(Renderer r)
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) return false;
            if (r is not MeshRenderer) return false;
            if (r.name == OverlayName) return false;
            if (!r.enabled || !r.gameObject.activeInHierarchy) return false;
            var mat = r.sharedMaterial;
            if (mat == null || mat.shader == null) return false;
            // Glass and other transparent surfaces do not take a sooty crack shell.
            if (mat.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent) return false;
            return true;
        }

        private void Apply()
        {
            float damage = _shownDamage01 >= CrackVisibleFrom ? _shownDamage01 : 0f;
            Color glow = ThermalRules.IncandescentColor(_shownHeat01);

            for (int i = _overlays.Count - 1; i >= 0; i--)
            {
                var mr = _overlays[i];
                if (mr == null) { _overlays.RemoveAt(i); continue; }

                // Follow the source renderer's enabled state (shape variants toggle them).
                var src = mr.transform.parent != null ? mr.transform.parent.GetComponent<Renderer>() : null;
                bool visible = src == null || (src.enabled && src.gameObject.activeInHierarchy);
                if (mr.enabled != visible) mr.enabled = visible;
                if (!visible) continue;

                mr.GetPropertyBlock(_props);
                _props.SetFloat(DamageId, damage);
                _props.SetFloat(HeatId, _shownHeat01);
                _props.SetFloat(ScorchId, _scorch01);
                _props.SetFloat(SeedId, _seed);
                _props.SetFloat(CrackScaleId, _crackScale);
                _props.SetColor(GlowColorId, glow);
                mr.SetPropertyBlock(_props);
            }
        }

        // ── Smoke & embers ──────────────────────────────────────────────────

        private void UpdateSmoke(float dt)
        {
            bool wantSmoke = _shownHeat01 > SmokeFromGlow || (_shownDamage01 > 0.72f && _shownHeat01 > 0.05f);
            if (!wantSmoke)
            {
                if (_smoke != null && _smoke.isEmitting) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                return;
            }

            if (_smoke == null) _smoke = CreateSmoke();
            if (_smoke == null) return;
            if (!_smoke.isPlaying) _smoke.Play(true);

            float intensity = Mathf.InverseLerp(SmokeFromGlow, 1f, _shownHeat01);
            var emission = _smoke.emission;
            emission.rateOverTime = Mathf.Lerp(2f, 14f, intensity);
            var main = _smoke.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.12f, 0.11f, 0.10f, Mathf.Lerp(0.25f, 0.55f, intensity)),
                Color.Lerp(new Color(1f, 0.45f, 0.10f, 0.8f), new Color(1f, 0.85f, 0.55f, 0.9f), intensity));
        }

        private ParticleSystem CreateSmoke()
        {
            var go = new GameObject(SmokeName);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.35f);
            main.gravityModifier = -0.06f;              // smoke drifts up, embers loft
            main.maxParticles = 48;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            float extent = 0.5f;
            var r = GetComponentInChildren<Renderer>();
            if (r != null) extent = Mathf.Max(0.2f, r.bounds.extents.magnitude * 0.7f);
            shape.scale = Vector3.one * extent;

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.3f, 0.3f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(0.0f, 0f), new GradientAlphaKey(0.7f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = grad;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.8f)));

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.6f;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.material = SmokeMaterial;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            return ps;
        }

        private void ReleaseSmoke()
        {
            if (_smoke == null) return;
            Destroy(_smoke.gameObject);
            _smoke = null;
        }

        // ── Shared materials ────────────────────────────────────────────────

        private static Material OverlayMaterial
        {
            get
            {
                if (s_overlayMaterial != null) return s_overlayMaterial;

                var template = Resources.Load<Material>("VoxelEngineRuntime/BlockDamageOverlay");
                if (template != null && template.shader != null)
                {
                    s_overlayMaterial = new Material(template) { name = "Mat_BlockDamageOverlay_Runtime" };
                    return s_overlayMaterial;
                }

                var shader = Shader.Find("VoxelEngine/BlockDamageOverlayURP");
                if (shader == null)
                {
                    if (!s_shaderMissingLogged)
                    {
                        s_shaderMissingLogged = true;
                        Debug.LogWarning("[BlockDamageVisual] Shader 'VoxelEngine/BlockDamageOverlayURP' not found. Run Voxel Engine Setup Step 62 so the overlay material is created; visible damage is disabled until then.");
                    }
                    return null;
                }
                s_overlayMaterial = new Material(shader) { name = "Mat_BlockDamageOverlay_Runtime" };
                return s_overlayMaterial;
            }
        }

        private static Material SmokeMaterial
        {
            get
            {
                if (s_smokeMaterial != null) return s_smokeMaterial;
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Sprites/Default");
                s_smokeMaterial = new Material(sh) { color = Color.white, name = "Mat_BlockDamageSmoke_Runtime" };
                if (s_smokeMaterial.HasProperty("_Surface")) s_smokeMaterial.SetFloat("_Surface", 1f);
                if (s_smokeMaterial.HasProperty("_Blend")) s_smokeMaterial.SetFloat("_Blend", 0f);
                s_smokeMaterial.SetOverrideTag("RenderType", "Transparent");
                if (s_smokeMaterial.HasProperty("_SrcBlend")) s_smokeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (s_smokeMaterial.HasProperty("_DstBlend")) s_smokeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (s_smokeMaterial.HasProperty("_ZWrite")) s_smokeMaterial.SetInt("_ZWrite", 0);
                s_smokeMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                return s_smokeMaterial;
            }
        }
    }
}
