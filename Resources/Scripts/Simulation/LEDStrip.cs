// Assets/Scripts/VoxelEngine/Simulation/LEDStrip.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — LED STRIP LIGHT                             ║
// ║  Thin configurable accent light strip for grids/static surfaces. ║
// ╚══════════════════════════════════════════════════════════════════╝
// v5.59.3-dev — even emissive lighting, clean-strip chase, and motion wake chase.

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
        [Tooltip("Local-space visual offset from the grid block anchor. Used by corner-to-corner placement so the strip can extend from its first selected corner.")]
        public Vector3 stripOffset = Vector3.zero;
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
        [Tooltip("When motion turns the strip on, run one start-to-end chase pass before staying solid.")]
        public bool motionChaseOnActivation;

        [Header("Power")]
        [Tooltip("Power draw in watts. Grid variants also expose this through their generated grid block item balance.")]
        public float wattsDraw = 5f;

        private readonly List<Light> _lights = new();
        private MeshRenderer _stripRenderer;
        private Renderer _chaseRenderer;
        private Material _stripMaterial;
        private Material _backingMaterial;
        private Material _chaseMaterial;
        private MaterialPropertyBlock _diodeBlock;
        private readonly List<Renderer> _diodes = new();
        private float _animTime;
        private bool _enabled = true;
        private GridBlock _gridBlock;
        private float _motionCheckTimer;
        private float _lastMotionTime = -999f;
        private bool _wasLit;
        private bool _motionChaseActive;
        private float _motionChaseStartTime;

        private bool HasGridPower => _gridBlock == null || (_gridBlock.Enabled && _gridBlock.Grid != null && _gridBlock.Grid.HasPower);
        private bool MotionSatisfied => !motionActivated || Time.time - _lastMotionTime <= Mathf.Max(0.1f, motionGraceSeconds);
        private bool ShouldBeLit => _enabled && HasGridPower && MotionSatisfied;
        private float MotionChaseDuration => 1f / Mathf.Max(0.1f, animSpeed);
        private bool ShouldRunContinuousChase => mode == LEDMode.Chase && ShouldBeLit && !(motionActivated && motionChaseOnActivation && !_motionChaseActive);

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
                   "Brightness " + brightness.ToString("0.##") + "\n" +
                   (motionChaseOnActivation ? "Motion Chase" : motionActivated ? "Motion Sensor" : "Manual") + "\n" +
                   (stripOffset.sqrMagnitude > 0.0001f ? "Stretched" : "Standard");
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

            bool lit = ShouldBeLit;
            if (lit && !_wasLit && motionActivated && motionChaseOnActivation)
            {
                _motionChaseActive = true;
                _motionChaseStartTime = Time.time;
                _animTime = 0f;
            }
            if (!lit) _motionChaseActive = false;
            _wasLit = lit;

            float intensity = lit ? brightness : 0f;
            if (lit)
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
                        // Chase is applied by a moving pulse/diode pass. Keep the base diffuser steady.
                        if (ShouldRunContinuousChase) intensity *= 0.28f;
                        break;
                }
            }

            bool oneShot = _motionChaseActive;
            float oneShotPhase = 1f;
            if (_motionChaseActive)
            {
                oneShotPhase = Mathf.Clamp01((Time.time - _motionChaseStartTime) / MotionChaseDuration);
                if (oneShotPhase >= 1f)
                    _motionChaseActive = false;
                intensity = brightness;
            }

            ApplyEmission(intensity, oneShot, oneShotPhase);
        }

        private void TickMotionSensor()
        {
            if (!motionActivated) return;
            _motionCheckTimer -= Time.deltaTime;
            if (_motionCheckTimer > 0f) return;
            _motionCheckTimer = 0.20f;

            var players = Object.FindObjectsByType<VoxelEngine.Player.PlayerController>(FindObjectsInactive.Exclude);
            float radiusSqr = Mathf.Max(0.1f, motionRadius) * Mathf.Max(0.1f, motionRadius);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null) continue;
                if (DistanceSqrToStrip(players[i].transform.position) <= radiusSqr)
                {
                    _lastMotionTime = Time.time;
                    return;
                }
            }
        }

        private float DistanceSqrToStrip(Vector3 worldPosition)
        {
            Vector3 a = transform.TransformPoint(stripOffset + new Vector3(-stripLength * 0.5f, 0f, 0f));
            Vector3 b = transform.TransformPoint(stripOffset + new Vector3(stripLength * 0.5f, 0f, 0f));
            Vector3 ab = b - a;
            float lenSqr = ab.sqrMagnitude;
            if (lenSqr < 0.0001f) return (worldPosition - transform.position).sqrMagnitude;
            float t = Mathf.Clamp01(Vector3.Dot(worldPosition - a, ab) / lenSqr);
            Vector3 closest = a + ab * t;
            return (worldPosition - closest).sqrMagnitude;
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
            backing.transform.localPosition = stripOffset + new Vector3(0f, 0.006f, 0f);
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
            Vector3 diffuserLocalPosition = stripOffset + new Vector3(0f, 0.020f, 0f);
            strip.transform.localPosition = diffuserLocalPosition;
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

            UpdateInteractionCollider(diffuserLocalPosition);
            BuildEndCaps();
            BuildDiodes();
            BuildChasePulse();
            BuildLights();
        }

        private void UpdateInteractionCollider(Vector3 center)
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = new Vector3(stripLength + 0.14f, Mathf.Max(0.08f, stripWidth + 0.04f), Mathf.Max(0.08f, stripWidth + 0.08f));
        }

        private void BuildEndCaps()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child != null && child.name.StartsWith("Generated_LEDEndCap_", System.StringComparison.Ordinal))
                    Destroy(child.gameObject);
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var capMaterial = new Material(shader) { name = "LEDStripEndCap_Runtime", color = new Color(0.025f, 0.028f, 0.032f) };
            if (capMaterial.HasProperty("_BaseColor")) capMaterial.SetColor("_BaseColor", new Color(0.025f, 0.028f, 0.032f));
            if (capMaterial.HasProperty("_Metallic")) capMaterial.SetFloat("_Metallic", 0.65f);
            if (capMaterial.HasProperty("_Smoothness")) capMaterial.SetFloat("_Smoothness", 0.45f);

            float capWidth = Mathf.Max(0.025f, stripWidth + 0.10f);
            for (int i = 0; i < 2; i++)
            {
                float sign = i == 0 ? -1f : 1f;
                var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cap.name = "Generated_LEDEndCap_" + i;
                cap.transform.SetParent(transform, false);
                cap.transform.localPosition = stripOffset + new Vector3(sign * (stripLength * 0.5f + 0.035f), 0.025f, 0f);
                cap.transform.localScale = new Vector3(0.035f, 0.055f, capWidth);
                var collider = cap.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                var renderer = cap.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = capMaterial;
            }
        }

        public bool CoversGridCell(Vector3Int gridPos, float cellSize)
        {
            _gridBlock ??= GetComponent<GridBlock>();
            if (_gridBlock == null || _gridBlock.Grid == null) return false;
            Vector3 local = transform.InverseTransformPoint(_gridBlock.Grid.GridToWorld(gridPos));
            float halfLength = stripLength * 0.5f + cellSize * 0.45f;
            float crossTolerance = Mathf.Max(cellSize * 0.55f, stripWidth + 0.08f);
            return Mathf.Abs(local.x - stripOffset.x) <= halfLength
                && Mathf.Abs(local.y) <= cellSize * 0.65f
                && Mathf.Abs(local.z - stripOffset.z) <= crossTolerance;
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
                diode.transform.localPosition = stripOffset + new Vector3(x, 0.032f, 0f);
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

        private void BuildChasePulse()
        {
            var pulse = transform.Find("Generated_LEDChasePulse")?.gameObject;
            if (pulse == null)
            {
                pulse = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pulse.name = "Generated_LEDChasePulse";
                pulse.transform.SetParent(transform, false);
                var collider = pulse.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _chaseMaterial = new Material(shader) { name = "LEDStripChasePulse_Runtime" };
            _chaseMaterial.EnableKeyword("_EMISSION");
            _chaseRenderer = pulse.GetComponent<Renderer>();
            if (_chaseRenderer != null)
            {
                _chaseRenderer.sharedMaterial = _chaseMaterial;
                _chaseRenderer.enabled = false;
            }
        }

        private void BuildLights()
        {
            _lights.Clear();
            // Remove point lights from LED strips. Their local hotspots made every few cells
            // brighter than the rest; the strip now uses one continuous emissive surface.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                if (child.name.StartsWith("Generated_LEDPoint_", System.StringComparison.Ordinal) || child.name == "LEDLight")
                    Destroy(child.gameObject);
            }
        }

        private void ApplyEmission(float intensity, bool oneShotChase = false, float oneShotPhase = 1f)
        {
            Color emission = stripColor * intensity * 0.8f;
            Color diffuserEmission = showSegments ? stripColor * intensity * 0.06f : emission;

            if (_stripMaterial != null)
            {
                _stripMaterial.color = stripColor * (showSegments ? 0.12f : 0.35f);
                if (_stripMaterial.HasProperty("_BaseColor")) _stripMaterial.SetColor("_BaseColor", stripColor * (showSegments ? 0.12f : 0.35f));
                if (_stripMaterial.HasProperty("_EmissionColor")) _stripMaterial.SetColor("_EmissionColor", diffuserEmission);
            }

            _diodeBlock ??= new MaterialPropertyBlock();
            int count = Mathf.Max(1, _diodes.Count);
            float chase = Mathf.Repeat(_animTime, 1f);
            bool continuousChase = ShouldRunContinuousChase;
            bool showChase = continuousChase || oneShotChase;
            float chasePhase = oneShotChase ? Mathf.Clamp01(oneShotPhase) : chase;

            for (int i = 0; i < _diodes.Count; i++)
            {
                var renderer = _diodes[i];
                if (renderer == null) continue;
                float diodeIntensity = intensity;
                if (oneShotChase && ShouldBeLit)
                {
                    float t = count == 1 ? 0f : i / (float)(count - 1);
                    diodeIntensity = t <= chasePhase ? brightness : 0f;
                }
                else if (continuousChase)
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

            UpdateCleanChasePulse(showChase, chasePhase, oneShotChase);
        }

        private void UpdateCleanChasePulse(bool active, float phase, bool oneShot)
        {
            if (_chaseRenderer == null) return;
            bool visible = active && !showSegments && ShouldBeLit && brightness > 0.001f;
            _chaseRenderer.enabled = visible;
            if (!visible) return;

            float usable = Mathf.Max(0.05f, stripLength * 0.92f);
            float width = Mathf.Clamp(stripWidth * 0.86f, 0.02f, stripWidth);
            if (oneShot)
            {
                float fillLength = Mathf.Max(0.025f, usable * Mathf.Clamp01(phase));
                float center = -usable * 0.5f + fillLength * 0.5f;
                _chaseRenderer.transform.localPosition = stripOffset + new Vector3(center, 0.034f, 0f);
                _chaseRenderer.transform.localScale = new Vector3(fillLength, 0.014f, width);
            }
            else
            {
                float pulseLength = Mathf.Clamp(stripLength * 0.18f, 0.08f, Mathf.Max(0.09f, stripLength * 0.45f));
                float minX = -usable * 0.5f + pulseLength * 0.5f;
                float maxX = usable * 0.5f - pulseLength * 0.5f;
                if (maxX < minX) { minX = 0f; maxX = 0f; }
                float x = Mathf.Lerp(minX, maxX, Mathf.Repeat(phase, 1f));
                _chaseRenderer.transform.localPosition = stripOffset + new Vector3(x, 0.034f, 0f);
                _chaseRenderer.transform.localScale = new Vector3(pulseLength, 0.014f, width);
            }

            Color pulseColor = stripColor * Mathf.Max(1.0f, brightness * 1.45f);
            if (_chaseMaterial != null)
            {
                _chaseMaterial.color = stripColor;
                if (_chaseMaterial.HasProperty("_BaseColor")) _chaseMaterial.SetColor("_BaseColor", stripColor);
                if (_chaseMaterial.HasProperty("_EmissionColor")) _chaseMaterial.SetColor("_EmissionColor", pulseColor);
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

        public void SyncEffectPhase(float phase = 0f)
        {
            _animTime = Mathf.Max(0f, phase);
            if (motionActivated && motionChaseOnActivation && ShouldBeLit)
            {
                _motionChaseActive = true;
                _motionChaseStartTime = Time.time - Mathf.Clamp01(phase) * MotionChaseDuration;
            }
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

        public void SetStretch(float meters, Vector3 localOffset)
        {
            stripLength = Mathf.Max(0.25f, meters);
            stripOffset = localOffset;
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
