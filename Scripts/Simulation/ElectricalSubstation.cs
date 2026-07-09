// Assets/Scripts/VoxelEngine/Simulation/ElectricalSubstation.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — ELECTRICAL SUBSTATION                       ║
// ║  Relays power between distant wire networks over 100+ meters.   ║
// ║  Acts as a voltage step-up/step-down hub for large bases.       ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Long-range power relay. Connects two PowerPole networks that are
    /// too far apart for standard wire reach. Acts as a bridge node in
    /// the power topology.
    /// </summary>
    public class ElectricalSubstation : MonoBehaviour
    {
        [Header("Substation Configuration")]
        [Tooltip("Maximum distance this substation can relay power.")]
        public float relayDistance = 150f;

        [Tooltip("Maximum power throughput in watts.")]
        public float maxThroughputWatts = 50000f;

        [Header("Visual")]
        [Tooltip("Height of the substation structure.")]
        public float structureHeight = 5f;

        // ── Runtime ───────────────────────────────────────────────────

        private PowerPole _inputPole;
        private PowerPole _outputPole;
        private PowerCable _powerNode;

        /// <summary>Current power flowing through the substation (watts).</summary>
        public float CurrentThroughput { get; private set; }

        /// <summary>True when both input and output poles are connected.</summary>
        public bool IsRelaying => _inputPole != null && _outputPole != null;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            // Create internal pole nodes for the power network.
            CreateInternalPoles();
            BuildSubstationVisuals();
        }

        private void CreateInternalPoles()
        {
            // Input side.
            var inputGo = new GameObject("SubstationInput");
            inputGo.transform.SetParent(transform, false);
            inputGo.transform.localPosition = Vector3.left * 1.5f + Vector3.up * structureHeight;
            _inputPole = inputGo.AddComponent<PowerPole>();
            _inputPole.maxConnections = 2;
            _inputPole.wireReach = relayDistance;

            // Output side.
            var outputGo = new GameObject("SubstationOutput");
            outputGo.transform.SetParent(transform, false);
            outputGo.transform.localPosition = Vector3.right * 1.5f + Vector3.up * structureHeight;
            _outputPole = outputGo.AddComponent<PowerPole>();
            _outputPole.maxConnections = 2;
            _outputPole.wireReach = relayDistance;

            // Internal bridge cable.
            _powerNode = gameObject.AddComponent<PowerCable>();
            _powerNode.connectRadius = 3f; // connects to the internal poles
        }

        // ── Public API ────────────────────────────────────────────────

        /// <summary>
        /// Connect this substation's input side to a power pole network.
        /// </summary>
        public bool ConnectInput(PowerPole sourcePole)
        {
            if (_inputPole == null || sourcePole == null) return false;
            return _inputPole.TryConnect(sourcePole);
        }

        /// <summary>
        /// Connect this substation's output side to a power pole network.
        /// </summary>
        public bool ConnectOutput(PowerPole destPole)
        {
            if (_outputPole == null || destPole == null) return false;
            return _outputPole.TryConnect(destPole);
        }

        // ── Visuals ───────────────────────────────────────────────────

        private void BuildSubstationVisuals()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var metalMat = new Material(shader);
            metalMat.color = new Color(0.40f, 0.42f, 0.46f);
            metalMat.SetFloat("_Metallic", 0.7f);
            metalMat.SetFloat("_Smoothness", 0.5f);

            // Main transformer body.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "TransformerBody";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.up * (structureHeight * 0.4f);
            body.transform.localScale = new Vector3(2f, structureHeight * 0.6f, 1.5f);

            var col = body.GetComponent<Collider>();
            if (col != null) Destroy(col);

            body.GetComponent<MeshRenderer>().material = metalMat;

            // Cooling fins on each side.
            for (int i = 0; i < 4; i++)
            {
                var fin = GameObject.CreatePrimitive(PrimitiveType.Cube);
                fin.name = $"CoolingFin_{i}";
                fin.transform.SetParent(transform, false);
                fin.transform.localPosition = new Vector3(
                    (i % 2 == 0 ? -1.1f : 1.1f),
                    structureHeight * 0.3f + i * 0.4f,
                    0f
                );
                fin.transform.localScale = new Vector3(0.08f, 0.6f, 1.3f);

                var fcol = fin.GetComponent<Collider>();
                if (fcol != null) Destroy(fcol);

                fin.GetComponent<MeshRenderer>().material = metalMat;
            }

            // Top insulators.
            for (int side = -1; side <= 1; side += 2)
            {
                var insulator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                insulator.name = side < 0 ? "InputInsulator" : "OutputInsulator";
                insulator.transform.SetParent(transform, false);
                insulator.transform.localPosition = new Vector3(
                    side * 1.5f,
                    structureHeight,
                    0f
                );
                insulator.transform.localScale = new Vector3(0.15f, 0.6f, 0.15f);

                var icol = insulator.GetComponent<Collider>();
                if (icol != null) Destroy(icol);

                var imat = new Material(shader);
                imat.color = new Color(0.85f, 0.82f, 0.75f); // ceramic white
                imat.SetFloat("_Metallic", 0.1f);
                imat.SetFloat("_Smoothness", 0.6f);
                insulator.GetComponent<MeshRenderer>().material = imat;
            }

            // Warning label (amber stripe).
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "WarningStripe";
            stripe.transform.SetParent(transform, false);
            stripe.transform.localPosition = Vector3.up * (structureHeight * 0.55f) + Vector3.forward * 0.76f;
            stripe.transform.localScale = new Vector3(1.8f, 0.15f, 0.01f);

            var scol = stripe.GetComponent<Collider>();
            if (scol != null) Destroy(scol);

            var smat = new Material(shader);
            smat.color = new Color(0.92f, 0.60f, 0.12f); // hazard amber
            smat.SetColor("_EmissionColor", new Color(0.92f, 0.60f, 0.12f) * 0.5f);
            smat.EnableKeyword("_EMISSION");
            stripe.GetComponent<MeshRenderer>().material = smat;
        }
    }
}
