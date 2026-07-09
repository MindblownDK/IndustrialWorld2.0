// Assets/Scripts/VoxelEngine/Simulation/GridLightBlock.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — GRID LIGHT BLOCK                            ║
// ║  Small spotlight / floodlight for grid vehicles and bases.      ║
// ║  Configurable color, intensity, range. Toggles with grid power. ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.GridSystem;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// A light block that attaches to a grid (ship, rover, station).
    /// Draws power from the grid and can be toggled on/off.
    /// </summary>
    public class GridLightBlock : GridBlock
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

        // ── Runtime ───────────────────────────────────────────────────

        private Light _light;

        /// <summary>Power consumed while the light is on.</summary>
        public override float PowerDraw => Enabled && _light != null && _light.enabled ? 25f : 0f;

        public override void OnPlaced()
        {
            base.OnPlaced();
            CreateLight();
        }

        private void CreateLight()
        {
            if (_light != null) return;

            var lightGo = new GameObject("GridLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = Vector3.forward * 0.3f;
            lightGo.transform.localRotation = Quaternion.identity;

            _light = lightGo.AddComponent<Light>();
            _light.type = lightType;
            _light.color = lightColor;
            _light.range = range;
            _light.intensity = intensity;
            _light.spotAngle = spotAngle;
            _light.shadows = LightShadows.Soft;

            // Small emissive glow on the block itself.
            CreateEmissiveIndicator();
        }

        private void CreateEmissiveIndicator()
        {
            var indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            indicator.name = "LightIndicator";
            indicator.transform.SetParent(transform, false);
            indicator.transform.localPosition = Vector3.forward * 0.4f;
            indicator.transform.localScale = Vector3.one * 0.12f;

            var col = indicator.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = indicator.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = lightColor;
            mat.SetColor("_EmissionColor", lightColor * 2f);
            mat.EnableKeyword("_EMISSION");
            mr.material = mat;
        }

        private void Update()
        {
            if (_light == null) CreateLight();

            // Toggle light based on Enabled state and grid power.
            bool shouldBeOn = Enabled && Grid != null;
            _light.enabled = shouldBeOn;
        }

        /// <summary>
        /// Change the light colour at runtime (e.g., from a config UI).
        /// </summary>
        public void SetColor(Color newColor)
        {
            lightColor = newColor;
            if (_light != null) _light.color = newColor;
        }

        /// <summary>
        /// Change the light range at runtime.
        /// </summary>
        public void SetRange(float newRange)
        {
            range = newRange;
            if (_light != null) _light.range = newRange;
        }

        /// <summary>
        /// Change the light intensity at runtime.
        /// </summary>
        public void SetIntensity(float newIntensity)
        {
            intensity = newIntensity;
            if (_light != null) _light.intensity = newIntensity;
        }
    }
}
