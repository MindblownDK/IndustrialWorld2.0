// Assets/Scripts/VoxelEngine/Networks/NetworkVisualController.cs
//
// Updates visual properties on pipes and gauges using MaterialPropertyBlocks.
// Zero GC allocations. Preserves GPU instancing.
//
// Attach to any pipe/gauge GameObject with a Renderer.
// - Glass pipes: updates _FillColor and _FillLevel based on fluid volume.
// - Gauges: updates _NeedleAngle based on flow rate.
// - Power cables: updates _GlowColor based on tier/active state.

using UnityEngine;

namespace VoxelEngine.Networks
{
    [RequireComponent(typeof(Renderer))]
    public class NetworkVisualController : MonoBehaviour
    {
        public enum VisualMode { GlassPipe, Gauge, PowerCable }

        [Header("Mode")]
        public VisualMode mode = VisualMode.GlassPipe;

        [Header("Gauge")]
        [Tooltip("Transform to rotate for the gauge needle.")]
        public Transform needleTransform;
        [Tooltip("Max rotation angle (degrees) for full flow.")]
        public float maxNeedleAngle = 270f;

        private Renderer _renderer;
        private MaterialPropertyBlock _mpb;
        private ConnectionAnchor _anchor;

        // Shader property IDs (cached for performance).
        private static readonly int _FillColor = Shader.PropertyToID("_FillColor");
        private static readonly int _FillLevel = Shader.PropertyToID("_FillLevel");
        private static readonly int _FlowSpeed = Shader.PropertyToID("_FlowSpeed");
        private static readonly int _GlowColor = Shader.PropertyToID("_GlowColor");

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            _mpb = new MaterialPropertyBlock();
            _anchor = GetComponentInParent<ConnectionAnchor>();
        }

        private void LateUpdate()
        {
            if (_anchor == null) { _anchor = GetComponentInParent<ConnectionAnchor>(); return; }

            switch (mode)
            {
                case VisualMode.GlassPipe: UpdateGlassPipe(); break;
                case VisualMode.Gauge:     UpdateGauge();     break;
                case VisualMode.PowerCable:UpdatePowerCable();break;
            }
        }

        // ── Glass Pipe ───────────────────────────────────────────

        private void UpdateGlassPipe()
        {
            if (!_anchor.isGlass) return;

            float fill = _anchor.fluidCapacity > 0 ? _anchor.fluidVolume / _anchor.fluidCapacity : 0;
            Color col = _anchor.currentFluid != null ? _anchor.currentFluid.color : new Color(0.3f, 0.6f, 0.9f, 0.5f);

            // Get flow rate from network.
            float flow = 0;
            if (_anchor.network is FluidNetworkNew fn2) flow = fn2.flowRate;

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_FillColor, col);
            _mpb.SetFloat(_FillLevel, fill);
            _mpb.SetFloat(_FlowSpeed, flow * 0.1f);
            _renderer.SetPropertyBlock(_mpb);
        }

        // ── Gauge ────────────────────────────────────────────────

        private void UpdateGauge()
        {
            if (needleTransform == null) return;

            float flow = 0;
            float maxFlow = 100f;

            if (_anchor.network is FluidNetworkNew fnG)
            {
                flow = fnG.flowRate;
                maxFlow = fnG.totalCapacity * 0.1f;
            }
            else if (_anchor.network is PowerNetworkNew pnG)
            {
                flow = pnG.totalGenerated;
                maxFlow = pnG.bottleneckWatts;
            }

            float t = Mathf.Clamp01(flow / Mathf.Max(1, maxFlow));
            float angle = Mathf.Lerp(0, maxNeedleAngle, t);
            needleTransform.localRotation = Quaternion.Euler(0, 0, -angle);

            // Also update MPB for any shader-based gauge rendering.
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(_FlowSpeed, t);
            _renderer.SetPropertyBlock(_mpb);
        }

        // ── Power Cable ──────────────────────────────────────────

        private void UpdatePowerCable()
        {
            Color glow;
            if (_anchor.network is PowerNetworkNew pnC)
            {
                if (pnC.isShortCircuited)
                    glow = new Color(1f, 0.2f, 0.1f) * (1f + Mathf.Sin(Time.time * 8f) * 0.5f); // red flicker
                else if (_anchor.isPowered)
                    glow = _anchor.powerTier switch
                    {
                        PowerTier.Low    => new Color(0.8f, 0.5f, 0.2f, 1f),  // copper
                        PowerTier.Medium => new Color(0.9f, 0.8f, 0.2f, 1f),  // gold
                        PowerTier.High   => new Color(0.3f, 0.7f, 1.0f, 1f),  // blue
                        _ => Color.gray
                    };
                else
                    glow = new Color(0.2f, 0.2f, 0.25f, 1f); // unpowered
            }
            else
            {
                glow = new Color(0.2f, 0.2f, 0.25f, 1f);
            }

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor(_GlowColor, glow);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
