// Assets/Scripts/VoxelEngine/Simulation/LEDStrip.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — LED STRIP LIGHT                              ║
// ║  Thin, flexible accent light strip for grids and static         ║
// ║  surfaces. Configurable color, brightness, and blink/pulse.     ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Simulation
{
    /// <summary>LED animation mode.</summary>
    public enum LEDMode { Static, Pulse, Blink, Chase }

    /// <summary>
    /// Thin light strip that snaps to grid edges and static surfaces.
    /// Supports multiple animation modes for accent and signal lighting.
    /// </summary>
    public class LEDStrip : MonoBehaviour
    {
        [Header("LED Configuration")]
        public Color stripColor = new(0.18f, 0.72f, 0.88f); // accent cyan default
        [Range(0.1f, 5f)] public float brightness = 1.5f;
        [Tooltip("Length of the strip in meters.")]
        public float stripLength = 1f;

        [Header("Animation")]
        public LEDMode mode = LEDMode.Static;
        [Tooltip("Speed of the animation (pulses/blinks per second).")]
        public float animSpeed = 2f;

        [Header("Power")]
        [Tooltip("Power draw in watts.")]
        public float wattsDraw = 5f;

        // ── Runtime ───────────────────────────────────────────────────

        private Light _light;
        private MeshRenderer _stripRenderer;
        private Material _stripMaterial;
        private float _animTime;
        private bool _enabled = true;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            BuildStripVisuals();
        }

        private void Update()
        {
            if (!_enabled) return;

            _animTime += Time.deltaTime * animSpeed;

            float intensity = brightness;

            switch (mode)
            {
                case LEDMode.Pulse:
                    // Smooth sine wave pulse.
                    intensity *= 0.5f + 0.5f * Mathf.Sin(_animTime * Mathf.PI * 2f);
                    break;

                case LEDMode.Blink:
                    // Hard on/off blink.
                    intensity *= Mathf.Sin(_animTime * Mathf.PI * 2f) > 0f ? 1f : 0f;
                    break;

                case LEDMode.Chase:
                    // Moving light effect — handled via UV offset on the material.
                    if (_stripMaterial != null)
                        _stripMaterial.SetFloat("_ChaseOffset", _animTime % 1f);
                    break;
            }

            // Apply to light.
            if (_light != null)
            {
                _light.intensity = intensity;
                _light.color = stripColor;
            }

            // Apply emissive to strip mesh.
            if (_stripMaterial != null)
            {
                _stripMaterial.SetColor("_EmissionColor", stripColor * intensity * 0.8f);
            }
        }

        // ── Visuals ───────────────────────────────────────────────────

        private void BuildStripVisuals()
        {
            // Strip mesh — thin flat bar.
            var strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = "LEDStripMesh";
            strip.transform.SetParent(transform, false);
            strip.transform.localPosition = Vector3.zero;
            strip.transform.localScale = new Vector3(stripLength, 0.02f, 0.04f);

            var col = strip.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _stripRenderer = strip.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _stripMaterial = new Material(shader);
            _stripMaterial.color = stripColor * 0.3f; // dark base
            _stripMaterial.SetColor("_EmissionColor", stripColor * brightness * 0.8f);
            _stripMaterial.EnableKeyword("_EMISSION");
            _stripMaterial.SetFloat("_Metallic", 0.5f);
            _stripMaterial.SetFloat("_Smoothness", 0.6f);
            _stripRenderer.material = _stripMaterial;

            // Point light for illumination.
            var lightGo = new GameObject("LEDLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.05f;

            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = stripColor;
            _light.intensity = brightness;
            _light.range = 4f;
            _light.shadows = LightShadows.None; // performance: no shadows for accent lights
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>Change the strip colour at runtime.</summary>
        public void SetColor(Color color)
        {
            stripColor = color;
            if (_light != null) _light.color = color;
            if (_stripMaterial != null)
            {
                _stripMaterial.color = color * 0.3f;
                _stripMaterial.SetColor("_EmissionColor", color * brightness * 0.8f);
            }
        }

        /// <summary>Change the animation mode at runtime.</summary>
        public void SetMode(LEDMode newMode)
        {
            mode = newMode;
            _animTime = 0f;
        }

        /// <summary>Toggle the strip on/off.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            if (_light != null) _light.enabled = enabled;
            if (_stripMaterial != null)
            {
                if (!enabled)
                    _stripMaterial.SetColor("_EmissionColor", Color.black);
            }
        }
    }
}
