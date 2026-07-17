// Assets/Scripts/VoxelEngine/Simulation/LEDStrip.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — LED STRIP LIGHT                             ║
// ║  Thin configurable accent light strip for grids/static surfaces. ║
// ╚══════════════════════════════════════════════════════════════════╝
// v5.57.0-dev — segmented/clean modes, visible chase animation, and motion activation.

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Simulation
{
    /// <summary>LED animation mode.</summary>
    public enum LEDMode { Static, Pulse, Blink, Chase }

    /// <summary>
    /// Thin light strip that snaps to grid edges and static surfaces.
    /// Supports clean/segmented visuals, animation modes, and optional motion activation.
    /// </summary>
    public class LEDStrip : MonoBehaviour, IGridDataProvider
    {
        [Header("LED Configuration")]
        public Color stripColor = new(0.18f, 0.72f, 0.88f);
        [Range(0.1f, 5f)] public float brightness = 1.5f;
        [Tooltip("Length of the strip in meters. Setup-authored grid variants use this to create small and large strips.")]
        public float stripLength = 1f;
        [Range(2, 32)] public int segmentCount = 8;
        [Tooltip("Width of the lit diffuser bar in meters.")]
        public float stripWidth = 0.08f;
        [Tooltip("When enabled, individual diode segments are visible. When disabled, the strip is one clean continuous diffuser.")]
        public bool showSegments = true;

        [Header("Animation")]
        public LEDMode mode = LEDMode.Static;
        [Tooltip("Speed of the animation (pulses/blinks per second).")]
        public float animSpeed = 2f;

        [Header("Motion Activation")]
        [Tooltip("Only turn on when a player is near this strip.")]
        public bool motionActivated;
        [Tooltip("Player detection radius in meters.")]
        public float motionRadius = 6f;
        [Tooltip("Seconds to stay on after the last player detection.")]
        public float motionGraceSeconds = 2.5f;

        [Header("Power")]
        [Tooltip("Power draw in watts. Grid variants also expose this through their generated grid block item balance.")]
        public float wattsDraw = 5f;

        private readonly List<Light> _lights = new();
        private MeshRenderer _stripRenderer;
        private Material _stripMaterial;
        private Material _backingMaterial;
        private MaterialPropertyBlock _diodeBlock;
        private readonly List<Renderer> _diodes = new();
        private float _animTime;
        private bool _enabled = true;
        private GridBlock _gridBlock;
        private float _motionCheckTimer;
        private float _lastMotionTime = -999f;

        private bool HasGridPower => _gridBlock == null || (_gridBlock.Enabled && _gridBlock.Grid != null && _gridBlock.Grid.HasPower);
        private bool MotionSatisfied => !motionActivated || Time.time - _lastMotionTime <= Mathf.Max(0.1f, motionGraceSeconds);
        private bool ShouldBeLit => _enabled && HasGridPower && MotionSatisfied;

        public string SourceName
        {
            get
            {
                _gridBlock ??= GetComponent<GridBlock>();
                if (_gridBlock != null && !string.IsNullOrWhiteSpace(_gridBlock.blockName) && _gridBlock.blockName != "Armor Block")
                    return _gridBlock.blockName;
                return "LED Strip";
            }
        }

        public string DataCategory => "Light";

        public string GetDisplayData()
        {
            _gridBlock ??= GetComponent<GridBlock>();
            string state = !_enabled || (_gridBlock != null && !_gridBlock.Enabled) ? "OFF"
                : !HasGridPower ? "NO POWER"
                : motionActivated && !MotionSatisfied ? "MOTION STANDBY"
                : "ON";
            return "LED STRIP\n" + state + "\n" +
                   "Mode " + mode + (showSegments ? " Seg" : " Clean") + "\n" +
                   "Draw " + FormatWatts(wattsDraw) + "\n" +
                   "Length " + stripLength.ToString("0.##") + "m\n" +
                   "Brightness " + brightness.ToString("0.##");
        }

        private void Awake()
        {
            _gridBlock = GetComponent<GridBlock>();
            BuildStripVisuals();
        }

        private void Update()
        {
            _gridBlock ??= GetComponent<GridBlock>();
            TickMotionSensor();

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
                        // Per-diode chase is applied in ApplyEmission(). Keep the diffuser low but visible.
                        intensity *= showSegments ? 0.35f : (0.55f + 0.45f * Mathf.Sin(_animTime * Mathf.PI * 2f));
                        break;
                }
            }

            ApplyEmission(intensity);
        }

        private void TickMotionSensor()
        {
            if (!motionActivated) return;
            _motionCheckTimer -= Time.deltaTime;
            if (_motionCheckTimer > 0f) return;
            _motionCheckTimer = 0.20f;

            var players = Object.FindObjectsByType<VoxelEngine.Player.PlayerController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            float radiusSqr = Mathf.Max(0.1f, motionRadius) * Mathf.Max(0.1f, motionRadius);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                if ((players[i].transform.position - transform.position).sqrMagnitude <= radiusSqr)
                {
                    _lastMotionTime = Time.time;
                    return;
                }
            }
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
            BuildLights();
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

            if (!showSegments)
            {
                _diodeBlock ??= new MaterialPropertyBlock();
                return;
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

        private void BuildLights()
        {
            _lights.Clear();
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (child.name.StartsWith("Generated_LEDPoint_", System.StringComparison.Ordinal) || child.name == "LEDLight")
                    Destroy(child.gameObject);
            }

            int lightCount = Mathf.Clamp(Mathf.CeilToInt(stripLength / 1.75f), 1, 6);
            float usable = Mathf.Max(0.05f, stripLength * 0.82f);
            for (int i = 0; i < lightCount; i++)
            {
                float t = lightCount == 1 ? 0.5f : i / (float)(lightCount - 1);
                float x = Mathf.Lerp(-usable * 0.5f, usable * 0.5f, t);
                var lightGo = new GameObject("Generated_LEDPoint_" + i);
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = new Vector3(x, 0.10f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = stripColor;
                light.intensity = brightness / lightCount;
                light.range = Mathf.Max(1.35f, stripLength * 0.62f);
                light.shadows = LightShadows.None;
                _lights.Add(light);
            }
        }

        private void ApplyEmission(float intensity)
        {
            Color emission = stripColor * intensity * 0.8f;
            int lightCount = Mathf.Max(1, _lights.Count);
            for (int i = 0; i < _lights.Count; i++)
            {
                var light = _lights[i];
                if (light == null) continue;
                light.enabled = intensity > 0.001f;
                light.intensity = intensity / lightCount;
                light.color = stripColor;
                light.range = Mathf.Max(1.35f, stripLength * 0.62f);
            }

            if (_stripMaterial != null)
            {
                _stripMaterial.color = stripColor * 0.35f;
                if (_stripMaterial.HasProperty("_BaseColor")) _stripMaterial.SetColor("_BaseColor", stripColor * 0.35f);
                if (_stripMaterial.HasProperty("_EmissionColor")) _stripMaterial.SetColor("_EmissionColor", emission);
            }

            _diodeBlock ??= new MaterialPropertyBlock();
            int count = Mathf.Max(1, _diodes.Count);
            float chase = Mathf.Repeat(_animTime, 1f);
            for (int i = 0; i < _diodes.Count; i++)
            {
                var renderer = _diodes[i];
                if (renderer == null) continue;
                float diodeIntensity = intensity;
                if (mode == LEDMode.Chase && ShouldBeLit)
                {
                    float t = count == 1 ? 0f : i / (float)(count - 1);
                    float distance = Mathf.Abs(Mathf.DeltaAngle(t * 360f, chase * 360f)) / 180f;
                    diodeIntensity = brightness * Mathf.Clamp01(1f - distance * 3.2f);
                }
                Color diodeEmission = stripColor * diodeIntensity * 1.35f;
                renderer.GetPropertyBlock(_diodeBlock);
                _diodeBlock.SetColor("_Color", stripColor);
                _diodeBlock.SetColor("_BaseColor", stripColor);
                _diodeBlock.SetColor("_EmissionColor", diodeEmission);
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

        public void SetSegmented(bool segmented)
        {
            showSegments = segmented;
            BuildDiodes();
        }

        public void SetMotionActivated(bool activated)
        {
            motionActivated = activated;
            if (!activated) _lastMotionTime = Time.time;
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

        private static string FormatWatts(float watts)
        {
            float abs = Mathf.Abs(watts);
            if (abs >= 1000000f) return (watts / 1000000f).ToString("0.##") + " MW";
            if (abs >= 1000f) return (watts / 1000f).ToString("0.#") + " kW";
            return watts.ToString("0") + " W";
        }
    }
}
