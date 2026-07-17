// Assets/Scripts/VoxelEngine/Simulation/GridLightBlock.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — GRID LIGHT BLOCK                            ║
// ║  Small spotlight / floodlight for grid vehicles and bases.      ║
// ║  Configurable color, intensity, range. Toggles with grid power. ║
// ╚══════════════════════════════════════════════════════════════════╝
// v5.52.0-dev — Power-state hardening + grid screen data provider.

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// A light block that attaches to a grid (ship, rover, station).
    /// Draws power from the grid and can be toggled on/off.
    /// </summary>
    public class GridLightBlock : GridBlock, IGridDataProvider
    {
        [Header("Light Configuration")]
        [Tooltip("Light colour. Can be changed at runtime.")]
        public Color lightColor = Color.white;

        [Tooltip("Maximum range of the spotlight in meters.")]
        public float range = 20f;

        [Tooltip("Spotlight cone angle in degrees.")]
        [Range(10f, 170f)]
        public float spotAngle = 60f;

        [Tooltip("Light intensity (brightness).")]
        public float intensity = 3f;

        [Tooltip("Type of light — Spot (focused beam) or Point (omni).")]
        public LightType lightType = LightType.Spot;

        [Header("Power")]
        [Tooltip("Power consumed while the light is enabled.")]
        public float wattsDraw = 25f;

        // ── Runtime ───────────────────────────────────────────────────

        private Light _light;
        private Renderer _indicatorRenderer;
        private MaterialPropertyBlock _indicatorBlock;
        private bool _lastOnState;

        /// <summary>Power consumed while the light is enabled. It is still counted when the
        /// grid is under-powered so total current loss remains visible in Power displays.</summary>
        public override float PowerDraw => Enabled ? Mathf.Max(0f, wattsDraw) : 0f;

        public bool IsOnline => Enabled && Grid != null && Grid.HasPower;

        // ── IGridDataProvider ─────────────────────────────────────────
        public string SourceName => string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block"
            ? "Grid Light Block"
            : blockName;

        public string DataCategory => "Light";

        public string GetDisplayData()
        {
            string state = !Enabled ? "DISABLED" : Grid == null ? "UNPLACED" : Grid.HasPower ? "ONLINE" : "NO POWER";
            return "LIGHT\n" + state + "\n" +
                   "Draw " + FormatWatts(PowerDraw) + "\n" +
                   "Range " + range.ToString("0.#") + "m\n" +
                   "Intensity " + intensity.ToString("0.#");
        }

        public override void OnPlaced()
        {
            base.OnPlaced();
            if (string.IsNullOrWhiteSpace(blockName) || blockName == "Armor Block")
                blockName = "Grid Light Block";
            CreateLight();
        }

        private void CreateLight()
        {
            if (_light == null)
            {
                Transform existingLight = transform.Find("GridLight");
                GameObject lightGo = existingLight != null ? existingLight.gameObject : new GameObject("GridLight");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = Vector3.forward * 0.3f;
                lightGo.transform.localRotation = Quaternion.identity;

                _light = lightGo.GetComponent<Light>();
                if (_light == null) _light = lightGo.AddComponent<Light>();
                _light.shadows = LightShadows.Soft;
            }

            ApplyLightSettings();
            CreateEmissiveIndicator();
        }

        private void CreateEmissiveIndicator()
        {
            Transform existingIndicator = transform.Find("LightIndicator");
            GameObject indicator;
            if (existingIndicator == null)
            {
                indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                indicator.name = "LightIndicator";
                indicator.transform.SetParent(transform, false);
                indicator.transform.localPosition = Vector3.forward * 0.4f;
                indicator.transform.localScale = Vector3.one * 0.12f;

                var col = indicator.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            else
            {
                indicator = existingIndicator.gameObject;
            }

            _indicatorRenderer = indicator.GetComponent<Renderer>();
            if (_indicatorRenderer != null && _indicatorRenderer.sharedMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                _indicatorRenderer.sharedMaterial = new Material(shader) { name = "GridLightIndicator_Runtime" };
            }

            _indicatorBlock ??= new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (_light == null) CreateLight();

            ApplyLightSettings();

            bool shouldBeOn = IsOnline;
            if (_light != null) _light.enabled = shouldBeOn;
            if (shouldBeOn != _lastOnState)
            {
                _lastOnState = shouldBeOn;
                UpdateIndicator(shouldBeOn);
            }
            else
            {
                UpdateIndicator(shouldBeOn);
            }
        }

        private void ApplyLightSettings()
        {
            if (_light == null) return;
            _light.type = lightType;
            _light.color = lightColor;
            _light.range = Mathf.Max(0f, range);
            _light.intensity = Mathf.Max(0f, intensity);
            _light.spotAngle = Mathf.Clamp(spotAngle, 10f, 170f);
        }

        private void UpdateIndicator(bool online)
        {
            if (_indicatorRenderer == null) return;

            Color indicatorColor = !Enabled
                ? new Color(0.25f, 0.25f, 0.25f, 0.55f)
                : online ? lightColor : new Color(0.95f, 0.18f, 0.10f, 0.85f);
            float emissionStrength = online ? Mathf.Max(0.25f, intensity) : 0.15f;

            _indicatorBlock ??= new MaterialPropertyBlock();
            _indicatorRenderer.GetPropertyBlock(_indicatorBlock);
            _indicatorBlock.SetColor("_Color", indicatorColor);
            _indicatorBlock.SetColor("_BaseColor", indicatorColor);
            _indicatorBlock.SetColor("_EmissionColor", indicatorColor * emissionStrength);
            _indicatorRenderer.SetPropertyBlock(_indicatorBlock);
        }

        /// <summary>Change the light colour at runtime.</summary>
        public void SetColor(Color newColor)
        {
            lightColor = newColor;
            ApplyLightSettings();
            UpdateIndicator(IsOnline);
        }

        /// <summary>Change the light range at runtime.</summary>
        public void SetRange(float newRange)
        {
            range = Mathf.Max(0f, newRange);
            ApplyLightSettings();
        }

        /// <summary>Change the light intensity at runtime.</summary>
        public void SetIntensity(float newIntensity)
        {
            intensity = Mathf.Max(0f, newIntensity);
            ApplyLightSettings();
            UpdateIndicator(IsOnline);
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
