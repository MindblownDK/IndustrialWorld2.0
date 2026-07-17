// Assets/Scripts/VoxelEngine/Simulation/LEDStrip.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — LED STRIP LIGHT                             ║
// ║  Thin configurable accent light strip for grids/static surfaces. ║
// ╚══════════════════════════════════════════════════════════════════╝
// v5.53.0-dev — Premium segmented strip visuals + configurable runtime length.

using UnityEngine;
using VoxelEngine.GridSystem;

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
        public Color stripColor = new(0.18f, 0.72f, 0.88f);
        [Range(0.1f, 5f)] public float brightness = 1.5f;
        [Tooltip("Length of the strip in meters. Setup-authored grid variants use this to create small and large strips.")]
        public float stripLength = 1f;
        [Range(2, 32)] public int segmentCount = 8;
        [Tooltip("Width of the lit diffuser bar in meters.")]
        public float stripWidth = 0.08f;

        [Header("Animation")]
        public LEDMode mode = LEDMode.Static;
        [Tooltip("Speed of the animation (pulses/blinks per second).")]
        public float animSpeed = 2f;

        [Header("Power")]
        [Tooltip("Power draw in watts. Grid variants also expose this through their generated grid block item balance.")]
        public float wattsDraw = 5f;

        private Light _light;
        private MeshRenderer _stripRenderer;
        private Material _stripMaterial;
        private Material _backingMaterial;
        private MaterialPropertyBlock _diodeBlock;
        private readonly System.Collections.Generic.List<Renderer> _diodes = new();
        private float _animTime;
        private bool _enabled = true;
        private GridBlock _gridBlock;

        private bool HasGridPower => _gridBlock == null || (_gridBlock.Enabled && _gridBlock.Grid != null && _gridBlock.Grid.HasPower);
        private bool ShouldBeLit => _enabled && HasGridPower;

        private void Awake()
        {
            _gridBlock = GetComponent<GridBlock>();
            BuildStripVisuals();
        }

        private void Update()
        {
            _gridBlock ??= GetComponent<GridBlock>();

            float intensity = ShouldBeLit ? brightness : 0f;
            if (ShouldBeLit)
            {
                _animTime += Time.deltaTime * animSpeed;
                switch (mode)
                {
                    case LEDMode.Pulse:
                        intensity *= 0.5f + 0.5f * Mathf.Sin(_animTime * Mathf.PI * 2f);
                        break;
                    case LEDMode.Blink:
                        intensity *= Mathf.Sin(_animTime * Mathf.PI * 2f) > 0f ? 1f : 0f;
                        break;
                    case LEDMode.Chase:
                        if (_stripMaterial != null && _stripMaterial.HasProperty("_ChaseOffset"))
                            _stripMaterial.SetFloat("_ChaseOffset", _animTime % 1f);
                        break;
                }
            }

            ApplyEmission(intensity);
        }

        private void BuildStripVisuals()
        {
            stripLength = Mathf.Max(0.25f, stripLength);
            stripWidth = Mathf.Clamp(stripWidth, 0.025f, 0.35f);
            segmentCount = Mathf.Clamp(segmentCount, 2, 32);

            var backing = transform.Find("Generated_LEDBackplate")?.gameObject ?? transform.Find("Generated_Backplate")?.gameObject;
            if (backing == null)
            {
                backing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                backing.name = "Generated_LEDBackplate";
                backing.transform.SetParent(transform, false);
            }
            backing.transform.localPosition = new Vector3(0f, 0.012f, 0f);
            backing.transform.localScale = new Vector3(stripLength + 0.12f, 0.045f, stripWidth + 0.08f);
            var backingCol = backing.GetComponent<Collider>();
            if (backingCol != null) Destroy(backingCol);
            var backingRenderer = backing.GetComponent<MeshRenderer>();
            if (backingRenderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _backingMaterial = new Material(shader) { name = "LEDStripBacking_Runtime", color = new Color(0.035f, 0.04f, 0.045f) };
                if (_backingMaterial.HasProperty("_BaseColor")) _backingMaterial.SetColor("_BaseColor", new Color(0.035f, 0.04f, 0.045f));
                if (_backingMaterial.HasProperty("_Metallic")) _backingMaterial.SetFloat("_Metallic", 0.65f);
                if (_backingMaterial.HasProperty("_Smoothness")) _backingMaterial.SetFloat("_Smoothness", 0.45f);
                backingRenderer.material = _backingMaterial;
            }

            GameObject strip = transform.Find("LEDStripMesh")?.gameObject;
            if (strip == null)
            {
                strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
                strip.name = "LEDStripMesh";
                strip.transform.SetParent(transform, false);
            }
            strip.transform.localPosition = new Vector3(0f, 0.055f, 0f);
            strip.transform.localScale = new Vector3(stripLength, 0.018f, stripWidth);
            var col = strip.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _stripRenderer = strip.GetComponent<MeshRenderer>();
            if (_stripRenderer == null) _stripRenderer = strip.AddComponent<MeshRenderer>();
            var stripShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _stripMaterial = new Material(stripShader) { name = "LEDStripDiffuser_Runtime" };
            _stripMaterial.color = stripColor * 0.35f;
            if (_stripMaterial.HasProperty("_BaseColor")) _stripMaterial.SetColor("_BaseColor", stripColor * 0.35f);
            if (_stripMaterial.HasProperty("_EmissionColor")) _stripMaterial.SetColor("_EmissionColor", stripColor * brightness * 0.8f);
            _stripMaterial.EnableKeyword("_EMISSION");
            if (_stripMaterial.HasProperty("_Metallic")) _stripMaterial.SetFloat("_Metallic", 0.05f);
            if (_stripMaterial.HasProperty("_Smoothness")) _stripMaterial.SetFloat("_Smoothness", 0.8f);
            _stripRenderer.material = _stripMaterial;

            BuildDiodes();
            BuildLight();
        }

        private void BuildDiodes()
        {
            _diodes.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_LEDDiode_", System.StringComparison.Ordinal))
                    Destroy(child.gameObject);
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var diodeMaterial = new Material(shader) { name = "LEDStripDiode_Runtime" };
            diodeMaterial.EnableKeyword("_EMISSION");
            int safeCount = Mathf.Clamp(segmentCount, 2, 32);
            float usable = Mathf.Max(0.05f, stripLength * 0.92f);
            for (int i = 0; i < safeCount; i++)
            {
                float t = safeCount == 1 ? 0.5f : i / (float)(safeCount - 1);
                float x = Mathf.Lerp(-usable * 0.5f, usable * 0.5f, t);
                var diode = GameObject.CreatePrimitive(PrimitiveType.Cube);
                diode.name = "Generated_LEDDiode_" + i;
                diode.transform.SetParent(transform, false);
                diode.transform.localPosition = new Vector3(x, 0.079f, 0f);
                diode.transform.localScale = new Vector3(Mathf.Min(0.08f, stripLength / safeCount * 0.35f), 0.012f, stripWidth * 0.82f);
                var collider = diode.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                var renderer = diode.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.sharedMaterial = diodeMaterial;
                    _diodes.Add(renderer);
                }
            }
            _diodeBlock ??= new MaterialPropertyBlock();
        }

        private void BuildLight()
        {
            var lightTransform = transform.Find("LEDLight");
            GameObject lightGo = lightTransform == null ? new GameObject("LEDLight") : lightTransform.gameObject;
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.up * 0.10f;

            _light = lightGo.GetComponent<Light>();
            if (_light == null) _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = stripColor;
            _light.intensity = brightness;
            _light.range = Mathf.Max(2f, stripLength * 1.75f);
            _light.shadows = LightShadows.None;
        }

        private void ApplyEmission(float intensity)
        {
            Color emission = stripColor * intensity * 0.8f;
            if (_light != null)
            {
                _light.enabled = intensity > 0.001f;
                _light.intensity = intensity;
                _light.color = stripColor;
                _light.range = Mathf.Max(2f, stripLength * 1.75f);
            }

            if (_stripMaterial != null)
            {
                _stripMaterial.color = stripColor * 0.35f;
                if (_stripMaterial.HasProperty("_BaseColor")) _stripMaterial.SetColor("_BaseColor", stripColor * 0.35f);
                if (_stripMaterial.HasProperty("_EmissionColor")) _stripMaterial.SetColor("_EmissionColor", emission);
            }

            _diodeBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < _diodes.Count; i++)
            {
                var renderer = _diodes[i];
                if (renderer == null) continue;
                renderer.GetPropertyBlock(_diodeBlock);
                _diodeBlock.SetColor("_Color", stripColor);
                _diodeBlock.SetColor("_BaseColor", stripColor);
                _diodeBlock.SetColor("_EmissionColor", emission * 1.4f);
                renderer.SetPropertyBlock(_diodeBlock);
            }
        }

        public void SetColor(Color color)
        {
            stripColor = color;
            ApplyEmission(ShouldBeLit ? brightness : 0f);
        }

        public void SetMode(LEDMode newMode)
        {
            mode = newMode;
            _animTime = 0f;
        }

        public void SetLength(float meters)
        {
            stripLength = Mathf.Max(0.25f, meters);
            BuildStripVisuals();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            ApplyEmission(ShouldBeLit ? brightness : 0f);
        }
    }
}
