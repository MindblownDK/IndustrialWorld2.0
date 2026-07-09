// Assets/Scripts/VoxelEngine/Simulation/StepDownTransformer.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — STEP-DOWN TRANSFORMER STATION (HV → LV)    ║
// ║  Large transformer station that converts high voltage back to   ║
// ║  low voltage for use by machines and buildings. Distinctive     ║
// ║  AMBER/ORANGE accent lighting and labelling — visually distinct ║
// ║  from the blue Step-Up station.                                 ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Step-down transformer station. Receives high-voltage power from
    /// the transmission grid (HighVoltagePoles) and converts it to low
    /// voltage for the local distribution network (PowerPoles, machines).
    ///
    /// Visual identity: AMBER/ORANGE accent — wider layout with more
    /// prominent bushings on the input (HV) side, "STEP-DOWN" labelling,
    /// downward arrow indicators. Layout is mirrored compared to Step-Up
    /// so experienced players can instantly tell them apart.
    /// </summary>
    public class StepDownTransformer : MonoBehaviour
    {
        [Header("Transformer Configuration")]
        [Tooltip("Maximum wattage this station can convert from HV to LV.")]
        public float maxThroughputWatts = 200_000_000f; // 200 MW

        [Tooltip("Power lost during conversion (percentage).")]
        [Range(0f, 0.1f)]
        public float conversionLoss = 0.02f;

        [Header("Connections (auto-detected)")]
        public HighVoltagePole hvInputPole;
        public PowerPole lvOutputPole;

        // ── Runtime ───────────────────────────────────────────────────

        private float _currentThroughput;
        private float _scanTimer;

        /// <summary>Current watts being converted from HV to LV.</summary>
        public float CurrentThroughput => _currentThroughput;

        /// <summary>Watts lost during conversion.</summary>
        public float ConversionLossWatts => _currentThroughput * conversionLoss;

        /// <summary>True when both HV input and LV output are connected.</summary>
        public bool IsConnected => hvInputPole != null && lvOutputPole != null;

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

            _currentThroughput = CalculateThroughput();
        }

        // ── Connections ───────────────────────────────────────────────

        private void ScanConnections()
        {
            // Find HV pole on the input side (right of station — mirrored from Step-Up).
            if (hvInputPole == null)
            {
                Vector3 inputPos = transform.position + transform.right * 6f;
                var hits = Physics.OverlapSphere(inputPos, 6f);
                foreach (var col in hits)
                {
                    var pole = col.GetComponentInParent<HighVoltagePole>();
                    if (pole != null) { hvInputPole = pole; break; }
                }
            }

            // Find LV pole on the output side (left of station).
            if (lvOutputPole == null)
            {
                Vector3 outputPos = transform.position + transform.left * 5f;
                var hits = Physics.OverlapSphere(outputPos, 4f);
                foreach (var col in hits)
                {
                    var pole = col.GetComponentInParent<PowerPole>();
                    if (pole != null) { lvOutputPole = pole; break; }
                }
            }
        }

        private float CalculateThroughput()
        {
            if (!IsConnected) return 0f;
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
        private static Material _amberAccentMat;
        private static Material _pipeMat;
        private static Material _warningMat;

        private void BuildStationVisuals()
        {
            EnsureMaterials();

            // ── Concrete foundation pad (wider than Step-Up) ──────────
            CreateBox("Foundation", Vector3.zero, new Vector3(12f, 0.3f, 7f), _concreteMat);

            // ── Primary transformer tank (larger than Step-Up) ────────
            CreateBox("TransformerTank_Primary", Vector3.up * 2.2f, new Vector3(5f, 4f, 3.5f), _tankMat);

            // Tank radiator fins (both sides — more fins than Step-Up).
            for (int side = -1; side <= 1; side += 2)
            {
                for (int f = 0; f < 7; f++)
                {
                    float z = -1.5f + f * 0.5f;
                    CreateBox($"Radiator_{(side > 0 ? "R" : "L")}_{f}",
                        new Vector3(side * 2.8f, 1.8f, z),
                        new Vector3(0.08f, 3.0f, 0.38f), _tankMat);
                }
            }

            // ── Secondary regulation tank ─────────────────────────────
            CreateBox("RegulationTank", new Vector3(-4f, 1.5f, 0), new Vector3(2f, 2.5f, 2.2f), _tankMat);

            // ── HV bushings (tall, on the right/input side) ──────────
            for (int i = 0; i < 3; i++)
            {
                float x = 1f + i * 1.5f;
                CreateBushing("HV_Bushing", new Vector3(x, 4.2f, 0), 2.5f, true);
            }

            // ── LV bushings (shorter, on the left/output side) ───────
            for (int i = 0; i < 3; i++)
            {
                float x = -1.5f + i * 1.2f;
                CreateBushing("LV_Bushing", new Vector3(x, 4.2f, -1.4f), 1.0f, false);
            }

            // ── Surge arresters (tall thin cylinders flanking HV side) ─
            for (int side = -1; side <= 1; side += 2)
            {
                CreateBox($"SurgeArrester_{(side > 0 ? "R" : "L")}",
                    new Vector3(side * 4.5f, 2.5f, 0),
                    new Vector3(0.2f, 4f, 0.2f), _pipeMat);

                // Cap with amber sphere.
                var cap = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cap.name = "ArresterCap";
                cap.transform.SetParent(transform, false);
                cap.transform.localPosition = new Vector3(side * 4.5f, 4.5f, 0);
                cap.transform.localScale = Vector3.one * 0.25f;
                Destroy(cap.GetComponent<Collider>());
                cap.GetComponent<MeshRenderer>().material = _amberAccentMat;
            }

            // ── Cooling fans (on primary tank) ────────────────────────
            for (int i = 0; i < 3; i++)
            {
                CreateFan($"CoolingFan_{i}", new Vector3(0, 1.0f, 1.85f + i * 0.1f),
                    new Vector3(-1.5f + i * 1.5f, 1.0f, 1.85f));
            }

            // ── Control building (small hut) ──────────────────────────
            CreateBox("ControlBuilding", new Vector3(-4.5f, 1.2f, -2.5f), new Vector3(2f, 2.2f, 1.8f), _concreteMat);
            CreateBox("BuildingDoor", new Vector3(-4.5f, 0.8f, -1.6f), new Vector3(0.8f, 1.5f, 0.05f), _tankMat);

            // Amber accent stripe on building.
            CreateBox("BuildingStripe", new Vector3(-4.5f, 1.8f, -1.58f), new Vector3(1.9f, 0.15f, 0.02f), _amberAccentMat);

            // ── Warning stripes (amber/black) on foundation edge ──────
            for (int i = 0; i < 6; i++)
            {
                CreateBox($"WarningStripe_{i}",
                    new Vector3(-5f + i * 2f, 0.16f, 3.5f),
                    new Vector3(0.8f, 0.02f, 0.3f),
                    i % 2 == 0 ? _warningMat : _amberAccentMat);
            }

            // ── STEP-DOWN label sign ──────────────────────────────────
            CreateBox("StepDownSign", new Vector3(0, 5f, 1.8f), new Vector3(3f, 0.7f, 0.05f), _amberAccentMat);

            // ── Downward arrow indicators (amber) ─────────────────────
            CreateBox("ArrowDown_1", new Vector3(-0.4f, 5f, 1.83f), new Vector3(0.15f, 0.4f, 0.02f), _amberAccentMat);
            CreateBox("ArrowDown_2", new Vector3(0.4f, 5f, 1.83f), new Vector3(0.15f, 0.4f, 0.02f), _amberAccentMat);

            // ── Pipe runs between tanks ───────────────────────────────
            CreatePipe("OilPipe_1", new Vector3(-2.5f, 2.5f, 1f), new Vector3(-4f, 1.8f, 1f));
            CreatePipe("OilPipe_2", new Vector3(-2.5f, 1.0f, 1f), new Vector3(-4f, 0.8f, 1f));
            CreatePipe("OilPipe_3", new Vector3(2.5f, 2.5f, 1f), new Vector3(2.5f, 2.5f, 2f));

            // ── Safety fencing ────────────────────────────────────────
            for (int i = 0; i < 10; i++)
            {
                float angle = (i / 10f) * Mathf.PI * 2f;
                float rx = 6.5f, rz = 4f;
                CreateBox($"FencePost_{i}",
                    new Vector3(Mathf.Cos(angle) * rx, 0.6f, Mathf.Sin(angle) * rz),
                    new Vector3(0.06f, 1.2f, 0.06f), _pipeMat);
            }
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
            int discs = isHV ? 10 : 4;
            float discSize = isHV ? 0.25f : 0.16f;

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
                mat.color = isHV ? new Color(0.72f, 0.62f, 0.42f) : new Color(0.58f, 0.48f, 0.32f);
                mat.SetFloat("_Metallic", 0.05f);
                mat.SetFloat("_Smoothness", 0.7f);
                disc.GetComponent<MeshRenderer>().material = mat;
            }

            CreateBox($"{prefix}_Cap", localBase + Vector3.up * height,
                Vector3.one * (discSize * 0.8f), _pipeMat);
        }

        private void CreateFan(string name, Vector3 localPos, Vector3 actualPos)
        {
            var fan = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fan.name = name;
            fan.transform.SetParent(transform, false);
            fan.transform.localPosition = actualPos;
            fan.transform.localScale = new Vector3(0.9f, 0.1f, 0.9f);
            fan.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Destroy(fan.GetComponent<Collider>());

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = new Color(0.32f, 0.35f, 0.38f);
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
            go.transform.localScale = new Vector3(0.07f, length * 0.5f, 0.07f);
            go.transform.localRotation = Quaternion.LookRotation(dir) * Quaternion.Euler(90, 0, 0);

            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().material = _pipeMat;
        }

        private static void EnsureMaterials()
        {
            if (_concreteMat != null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _concreteMat = new Material(shader);
            _concreteMat.color = new Color(0.60f, 0.58f, 0.55f);
            _concreteMat.SetFloat("_Metallic", 0.0f);
            _concreteMat.SetFloat("_Smoothness", 0.15f);

            _tankMat = new Material(shader);
            _tankMat.color = new Color(0.50f, 0.52f, 0.48f);
            _tankMat.SetFloat("_Metallic", 0.4f);
            _tankMat.SetFloat("_Smoothness", 0.3f);

            _amberAccentMat = new Material(shader);
            _amberAccentMat.color = new Color(0.92f, 0.60f, 0.12f); // hazard amber/orange
            _amberAccentMat.SetColor("_EmissionColor", new Color(0.92f, 0.60f, 0.12f) * 1.5f);
            _amberAccentMat.EnableKeyword("_EMISSION");
            _amberAccentMat.SetFloat("_Metallic", 0.5f);

            _pipeMat = new Material(shader);
            _pipeMat.color = new Color(0.38f, 0.40f, 0.42f);
            _pipeMat.SetFloat("_Metallic", 0.7f);
            _pipeMat.SetFloat("_Smoothness", 0.4f);

            _warningMat = new Material(shader);
            _warningMat.color = new Color(0.12f, 0.12f, 0.12f); // near-black
            _warningMat.SetFloat("_Metallic", 0.1f);
        }
    }
}
