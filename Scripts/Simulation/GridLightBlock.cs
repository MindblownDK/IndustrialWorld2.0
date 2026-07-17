// Assets/Scripts/VoxelEngine/Simulation/GridLightBlock.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — GRID LIGHT BLOCK                            ║
// ║  Small spotlight / floodlight for grid vehicles and bases.      ║
// ║  Configurable color, intensity, range. Toggles with grid power. ║
// ╚══════════════════════════════════════════════════════════════════╝
// v5.52.0-dev — Power-state hardening + grid screen data provider.

using System.Collections.Generic;
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

        private readonly List<Light> _lights = new();
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
            CacheLights();
            if (_lights.Count == 0)
            {
                Transform existingLight = transform.Find("GridLight");
                GameObject lightGo = existingLight != null ? existingLight.gameObject : new GameObject("GridLight");
                lightGo.transform.SetParent(transform, false);
                lightGo.transform.localPosition = Vector3.forward * 0.3f;
                lightGo.transform.localRotation = Quaternion.identity;

                var light = lightGo.GetComponent<Light>();
                if (light == null) light = lightGo.AddComponent<Light>();
                light.shadows = LightShadows.Soft;
                _lights.Add(light);
            }

            ApplyLightSettings();
            CreateEmissiveIndicator();
        }

        private void CacheLights()
        {
            _lights.Clear();
            var found = GetComponentsInChildren<Light>(true);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;
                // Status/indicator lights are decorative and should not become beam emitters.
                if (found[i].name.IndexOf("Status", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                _lights.Add(found[i]);
            }
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
            if (_lights.Count == 0) CreateLight();

            ApplyLightSettings();

            bool shouldBeOn = IsOnline;
            for (int i = 0; i < _lights.Count; i++)
            {
                if (_lights[i] != null) _lights[i].enabled = shouldBeOn;
            }
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
            if (_lights.Count == 0) return;
            for (int i = 0; i < _lights.Count; i++)
            {
                var light = _lights[i];
                if (light == null) continue;
                light.type = lightType;
                light.color = lightColor;
                light.range = Mathf.Max(0f, range);
                light.intensity = Mathf.Max(0f, intensity);
                light.spotAngle = Mathf.Clamp(spotAngle, 10f, 170f);
                light.shadows = LightShadows.Soft;
            }
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
