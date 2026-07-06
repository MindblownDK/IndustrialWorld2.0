// Assets/Scripts/VoxelEngine/Power/Wind/WindTurbinePart.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  WIND TURBINE PART — one placeable module of a modular turbine. ║
// ║  Tower / Nacelle / Gearbox / Generator / Hub / Blade (HAWT)     ║
// ║  VerticalRotor / VerticalBlade (VAWT).                          ║
// ║  Parts self-attach to the nearest compatible controller and     ║
// ║  snap into their exact socket pose. Each part carries its own   ║
// ║  condition (100 → degrades slowly under load).                  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Power.Wind
{
    public enum WindTurbinePartKind
    {
        Tower,
        Nacelle,
        Gearbox,
        Generator,
        Hub,
        Blade,
        VerticalRotor,
        VerticalBlade
    }

    public class WindTurbinePart : MonoBehaviour
    {
        [Header("Identity")]
        public WindTurbinePartKind kind = WindTurbinePartKind.Tower;
        [Tooltip("Tier id — must match the controller's tierId to attach (t90 / t150 / t236 / vsmall / vlarge).")]
        public string tierId = "t90";

        [Header("Condition")]
        [Tooltip("100 = factory-new. Degrades slowly while the turbine runs under load.")]
        [Range(0f, 100f)] public float condition = 100f;

        /// <summary>Controller this part is attached to (null while orphaned).</summary>
        public WindTurbineController Controller { get; internal set; }

        /// <summary>Blade slot index (0..n-1) — -1 for non-blade parts.</summary>
        public int SlotIndex { get; internal set; } = -1;

        private float _retryTimer;
        private bool  _isRoot;

        // ── Visual weathering ─────────────────────────────────────────────
        // Metal parts rust toward an oxide brown; blades soot-darken. Applied
        // via MaterialPropertyBlocks so shared material assets are never touched
        // and no material instances leak. Repairing restores the factory look.
        private static readonly Color RustColor = new(0.46f, 0.27f, 0.16f);
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private static readonly int SmoothId    = Shader.PropertyToID("_Smoothness");

        private Renderer[] _weatherRenderers;
        private Color[]    _factoryColors;
        private float[]    _factorySmoothness;
        private MaterialPropertyBlock _mpb;
        private float _appliedWear = -1f;   // last wear level actually pushed to renderers

        private void Awake()
        {
            // Tower / VerticalRotor prefabs carry the controller on the same GameObject.
            _isRoot = GetComponent<WindTurbineController>() != null;
            CacheWeatherTargets();
        }

        private void CacheWeatherTargets()
        {
            var all = GetComponentsInChildren<Renderer>(true);
            var list = new System.Collections.Generic.List<Renderer>(all.Length);
            foreach (var r in all)
            {
                if (r == null || r.sharedMaterial == null) continue;
                // Keep UI markers pristine — the emissive power-port square and
                // the yellow gearbox/generator alignment pads never weather.
                if (r.gameObject.name.StartsWith("PowerPort")) continue;
                if (r.gameObject.name.StartsWith("SnapMarker")) continue;
                list.Add(r);
            }
            _weatherRenderers  = list.ToArray();
            _factoryColors     = new Color[_weatherRenderers.Length];
            _factorySmoothness = new float[_weatherRenderers.Length];
            for (int i = 0; i < _weatherRenderers.Length; i++)
            {
                var m = _weatherRenderers[i].sharedMaterial;
                _factoryColors[i]     = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : m.color;
                _factorySmoothness[i] = m.HasProperty(SmoothId) ? m.GetFloat(SmoothId) : 0.5f;
            }
            _mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Pushes the current condition to the visuals. Cheap (skips when wear
        /// hasn't moved ≥0.5%), called by the controller's 1 Hz degrade tick,
        /// on attach, and after repairs.
        /// </summary>
        public void ApplyWeathering()
        {
            if (_weatherRenderers == null || _mpb == null) return;
            float wear = 1f - Mathf.Clamp01(condition / 100f);
            if (Mathf.Abs(wear - _appliedWear) < 0.005f) return;
            _appliedWear = wear;

            bool isBlade = kind == WindTurbinePartKind.Blade || kind == WindTurbinePartKind.VerticalBlade;

            for (int i = 0; i < _weatherRenderers.Length; i++)
            {
                var r = _weatherRenderers[i];
                if (r == null) continue;

                Color factory = _factoryColors[i];
                Color aged;
                if (isBlade)
                {
                    // Blades: gelcoat soots and greys — darken toward charcoal.
                    aged = Color.Lerp(factory, factory * 0.32f, wear * 0.85f);
                }
                else
                {
                    // Metal: blend toward oxide-brown rust and lose sheen.
                    aged = Color.Lerp(factory, RustColor, wear * 0.70f);
                    aged *= Mathf.Lerp(1f, 0.82f, wear);   // grime darkening
                    aged.a = factory.a;
                }

                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, aged);
                _mpb.SetColor(ColorId, aged);
                _mpb.SetFloat(SmoothId, Mathf.Lerp(_factorySmoothness[i], 0.08f, wear));
                r.SetPropertyBlock(_mpb);
            }
        }

        private bool _live;

        private void Start()
        {
            // Build ghosts are prefab clones WITHOUT a PlacedBlock — never let a
            // ghost part claim a real turbine socket.
            _live = GetComponent<VoxelEngine.Building.PlacedBlock>() != null;
            if (_live && !_isRoot) TryAttach();
            // Save-restored parts carry non-factory condition — show it right away.
            if (_live && condition < 99.5f) ApplyWeathering();
        }

        private void Update()
        {
            if (!_live || _isRoot || Controller != null) return;

            // Orphaned part (e.g. restored from save before its tower spawned) —
            // keep looking for a compatible controller at a relaxed cadence.
            _retryTimer += Time.deltaTime;
            if (_retryTimer >= 0.5f)
            {
                _retryTimer = 0f;
                TryAttach();
            }
        }

        private void TryAttach()
        {
            var c = WindTurbineController.FindBestFor(this, transform.position);
            if (c != null) c.Attach(this);
        }

        private void OnDestroy()
        {
            Controller?.Detach(this);
        }
    }
}
