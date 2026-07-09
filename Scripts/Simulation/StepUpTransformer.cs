// Assets/Scripts/VoxelEngine/Simulation/StepUpTransformer.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — STEP-UP TRANSFORMER STATION (LV → HV)      ║
// ║  Large transformer station that converts low voltage to high    ║
// ║  voltage for long-distance transmission. Required when power    ║
// ║  exceeds 25 MW. Distinctive BLUE accent lighting and labelling. ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Step-up transformer station. Sits between the low-voltage power
    /// network (standard PowerPoles, machines) and the high-voltage
    /// transmission grid (HighVoltagePoles).
    ///
    /// When total network wattage exceeds <see cref="VoltageSystemConfig.lvThresholdWatts"/>,
    /// the player must build this station to step voltage up for transmission.
    ///
    /// Visual identity: BLUE accent — large transformer tanks with
    /// upward-pointing arrows, "STEP-UP" labelling, cooling fans.
    /// </summary>
    public class StepUpTransformer : MonoBehaviour
    {
        [Header("Transformer Configuration")]
        [Tooltip("Maximum wattage this station can convert from LV to HV.")]
        public float maxThroughputWatts = 200_000_000f; // 200 MW

        [Tooltip("Power lost during conversion (percentage). Realistic loss is 1-3%.")]
        [Range(0f, 0.1f)]
        public float conversionLoss = 0.02f;

        [Header("Connections (auto-detected)")]
        public PowerPole lvInputPole;
        public HighVoltagePole hvOutputPole;

        // ── Runtime ───────────────────────────────────────────────────

        private float _currentThroughput;
        private float _scanTimer;
        private Animator _fanAnimator;

        /// <summary>Current watts being converted from LV to HV.</summary>
        public float CurrentThroughput => _currentThroughput;

        /// <summary>Watts lost during conversion.</summary>
        public float ConversionLossWatts => _currentThroughput * conversionLoss;

        /// <summary>True when both LV input and HV output are connected.</summary>
        public bool IsConnected => lvInputPole != null && hvOutputPole != null;

        /// <summary>True when actively converting power.</summary>
        public bool IsActive => IsConnected && _currentThroughput > 0f;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            BuildStationVisuals();
        }

        private void Update()
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer >= 0.5f)
            {
                _scanTimer = 0f;
                ScanConnections();
            }

            // Calculate throughput based on connected networks.
            _currentThroughput = CalculateThroughput();
        }

        // ── Connections ───────────────────────────────────────────────

        private void ScanConnections()
        {
            // Find LV pole on the input side (left of station).
            if (lvInputPole == null)
            {
                Vector3 inputPos = transform.position + transform.left * 5f;
                var hits = Physics.OverlapSphere(inputPos, 4f);
                foreach (var col in hits)
                {
                    var pole = col.GetComponentInParent<PowerPole>();
                    if (pole != null) { lvInputPole = pole; break; }
                }
            }

            // Find HV pole on the output side (right of station).
            if (hvOutputPole == null)
            {
                Vector3 outputPos = transform.position + transform.right * 5f;
                var hits = Physics.OverlapSphere(outputPos, 6f);
                foreach (var col in hits)
                {
                    var pole = col.GetComponentInParent<HighVoltagePole>();
                    if (pole != null) { hvOutputPole = pole; break; }
                }
            }
        }

        private float CalculateThroughput()
        {
            if (!IsConnected) return 0f;
            // Throughput limited by station capacity.
            return Mathf.Min(maxThroughputWatts, maxThroughputWatts);
        }

        /// <summary>
        /// Called by the power system to get the converted wattage.
        /// Input watts minus conversion loss.
        /// </summary>
        public float ConvertPower(float inputWatts)
        {
            float capped = Mathf.Min(inputWatts, maxThroughputWatts);
            return capped * (1f - conversionLoss);
        }

        // ── Station Visuals ───────────────────────────────────────────

        private static Material _concreteMat;
        private static Material _tankMat;
        private static Material _blueAccentMat;
        private static Material _pipeMat;

        private void BuildStationVisuals()
        {
            EnsureMaterials();

            // ── Concrete foundation pad ───────────────────────────────
            CreateBox("Foundation", Vector3.zero, new Vector3(10f, 0.3f, 6f), _concreteMat);

            // ── Main transformer tank (large, centre) ─────────────────
            CreateBox("TransformerTank_Main", Vector3.up * 2f, new Vector3(4f, 3.5f, 3f), _tankMat);

            // Tank radiator fins (both sides).
            for (int side = -1; side <= 1; side += 2)
            {
                for (int f = 0; f < 5; f++)
                {
                    float z = -1.2f + f * 0.6f;
                    CreateBox($"Radiator_{(side > 0 ? "R" : "L")}_{f}",
                        new Vector3(side * 2.3f, 1.5f, z),
                        new Vector3(0.08f, 2.5f, 0.45f), _tankMat);
                }
            }

            // ── Secondary transformer tank (smaller) ──────────────────
            CreateBox("TransformerTank_Sec", new Vector3(3.5f, 1.3f, 0), new Vector3(2.5f, 2.2f, 2f), _tankMat);

            // ── HV bushings (tall ceramic insulators on top) ──────────
            for (int i = 0; i < 3; i++)
            {
                float x = -1.5f + i * 1.5f;
                CreateBushing("HV_Bushing", new Vector3(x, 3.75f, 0), 2.0f, true);
            }

            // ── LV bushings (shorter, on the other side) ──────────────
            for (int i = 0; i < 3; i++)
            {
                float x = -1.5f + i * 1.5f;
                CreateBushing("LV_Bushing", new Vector3(x, 3.75f, -1.2f), 1.2f, false);
            }

            // ── Cooling fans (on secondary tank) ──────────────────────
            for (int i = 0; i < 2; i++)
            {
                CreateFan($"CoolingFan_{i}", new Vector3(3.5f, 1.0f, -1.2f + i * 2.4f));
            }

            // ── Control cabinet ───────────────────────────────────────
            CreateBox("ControlCabinet", new Vector3(-4f, 1f, 0), new Vector3(1.2f, 1.8f, 0.8f), _tankMat);

            // Blue accent stripe on cabinet.
            CreateBox("CabinetStripe", new Vector3(-4f, 1.4f, 0.41f), new Vector3(1.1f, 0.12f, 0.02f), _blueAccentMat);

            // ── Safety fencing posts ──────────────────────────────────
            for (int i = 0; i < 8; i++)
            {
                float angle = (i / 8f) * Mathf.PI * 2f;
                float r = 5.5f;
                CreateBox($"FencePost_{i}",
                    new Vector3(Mathf.Cos(angle) * r, 0.6f, Mathf.Sin(angle) * r * 0.6f),
                    new Vector3(0.06f, 1.2f, 0.06f), _pipeMat);
            }

            // ── STEP-UP label sign ────────────────────────────────────
            CreateBox("StepUpSign", new Vector3(0, 4.5f, 1.55f), new Vector3(2.5f, 0.6f, 0.05f), _blueAccentMat);

            // ── Upward arrow indicators (blue) ────────────────────────
            CreateBox("ArrowUp_1", new Vector3(-0.4f, 4.5f, 1.58f), new Vector3(0.15f, 0.4f, 0.02f), _blueAccentMat);
            CreateBox("ArrowUp_2", new Vector3(0.4f, 4.5f, 1.58f), new Vector3(0.15f, 0.4f, 0.02f), _blueAccentMat);

            // ── Pipe runs between tanks ───────────────────────────────
            CreatePipe("OilPipe_1", new Vector3(2f, 2.5f, 0.8f), new Vector3(3.5f, 1.8f, 0.8f));
            CreatePipe("OilPipe_2", new Vector3(2f, 1.0f, 0.8f), new Vector3(3.5f, 0.8f, 0.8f));
        }

        private void CreateBox(string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().material = mat;
        }

        private void CreateBushing(string prefix, Vector3 localBase, float height, bool isHV)
        {
            int discs = isHV ? 8 : 5;
            float discSize = isHV ? 0.22f : 0.18f;

            for (int d = 0; d < discs; d++)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = $"{prefix}_{d}";
                disc.transform.SetParent(transform, false);
                disc.transform.localPosition = localBase + Vector3.up * (d * (height / discs));
                disc.transform.localScale = new Vector3(discSize, 0.04f, discSize);
                Destroy(disc.GetComponent<Collider>());

                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                var mat = new Material(shader);
                mat.color = isHV ? new Color(0.70f, 0.60f, 0.45f) : new Color(0.60f, 0.50f, 0.35f);
                mat.SetFloat("_Metallic", 0.05f);
                mat.SetFloat("_Smoothness", 0.7f);
                disc.GetComponent<MeshRenderer>().material = mat;
            }

            // Metal cap on top.
            CreateBox($"{prefix}_Cap", localBase + Vector3.up * height,
                Vector3.one * (discSize * 0.8f), _pipeMat);
        }

        private void CreateFan(string name, Vector3 localPos)
        {
            var fan = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fan.name = name;
            fan.transform.SetParent(transform, false);
            fan.transform.localPosition = localPos;
            fan.transform.localScale = new Vector3(0.8f, 0.1f, 0.8f);
            fan.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Destroy(fan.GetComponent<Collider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = new Color(0.35f, 0.38f, 0.42f);
            mat.SetFloat("_Metallic", 0.6f);
            fan.GetComponent<MeshRenderer>().material = mat;
        }

        private void CreatePipe(string name, Vector3 localA, Vector3 localB)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(transform, false);

            Vector3 mid = (localA + localB) * 0.5f;
            float length = Vector3.Distance(localA, localB);
            Vector3 dir = (localB - localA).normalized;

            go.transform.localPosition = mid;
            go.transform.localScale = new Vector3(0.06f, length * 0.5f, 0.06f);
            go.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0);

            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().material = _pipeMat;
        }

        private static void EnsureMaterials()
        {
            if (_concreteMat != null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _concreteMat = new Material(shader);
            _concreteMat.color = new Color(0.62f, 0.60f, 0.58f);
            _concreteMat.SetFloat("_Metallic", 0.0f);
            _concreteMat.SetFloat("_Smoothness", 0.15f);

            _tankMat = new Material(shader);
            _tankMat.color = new Color(0.55f, 0.56f, 0.52f); // olive-grey industrial
            _tankMat.SetFloat("_Metallic", 0.4f);
            _tankMat.SetFloat("_Smoothness", 0.3f);

            _blueAccentMat = new Material(shader);
            _blueAccentMat.color = new Color(0.15f, 0.45f, 0.85f); // bright blue
            _blueAccentMat.SetColor("_EmissionColor", new Color(0.15f, 0.45f, 0.85f) * 1.5f);
            _blueAccentMat.EnableKeyword("_EMISSION");
            _blueAccentMat.SetFloat("_Metallic", 0.5f);

            _pipeMat = new Material(shader);
            _pipeMat.color = new Color(0.40f, 0.42f, 0.44f);
            _pipeMat.SetFloat("_Metallic", 0.7f);
            _pipeMat.SetFloat("_Smoothness", 0.4f);
        }
    }
}
