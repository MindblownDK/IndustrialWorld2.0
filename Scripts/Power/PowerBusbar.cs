// Assets/Scripts/VoxelEngine/Power/PowerBusbar.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║         POWER BUSBAR — clean cable-organization conduit         ║
// ║                                                                  ║
// ║  A "fat" power node that behaves exactly like a PowerCable from  ║
// ║  the network's point of view (auto-discovers cardinal neighbours,║
// ║  carries power, respects PortConfig & WrenchBlacklist), but      ║
// ║  renders as a long horizontal copper bar with multiple cable-tap ║
// ║  sockets along its length. Lets the player run one trunk along   ║
// ║  the ceiling of a factory and snap individual machine cables     ║
// ║  into it instead of nesting cubes everywhere.                    ║
// ║                                                                  ║
// ║  Inherits from PowerCable so 100% of the existing topology /     ║
// ║  CanLinkTo / WrenchBlacklist code "just works".                  ║
// ╚══════════════════════════════════════════════════════════════════╝

using UnityEngine;

namespace VoxelEngine.Power
{
    /// <summary>
    /// A premium-looking power bus segment. The bar itself is a Cylinder
    /// stretched along the configured <see cref="busAxis"/>, ringed with
    /// flanged cable-tap sockets at every 0.25 m so a player can snap many
    /// cables into one trunk without the visual clutter of dozens of nodes.
    /// </summary>
    public class PowerBusbar : PowerCable
    {
        public enum BusAxis { X, Y, Z }

        [Header("Busbar Geometry")]
        [Tooltip("Which world axis the bar runs along.")]
        public BusAxis busAxis = BusAxis.X;

        [Tooltip("Length of the bar in metres (also drives connection reach " +
                 "on the bus axis).")]
        [Range(1f, 6f)] public float busLength = 2f;

        [Tooltip("Outer radius of the bar shaft.")]
        [Range(0.05f, 0.40f)] public float busRadius = 0.18f;

        [Tooltip("Radius of the tap-socket collars riveted along the bar.")]
        [Range(0.06f, 0.40f)] public float socketRadius = 0.24f;

        [Tooltip("Spacing between tap sockets along the bar.")]
        [Range(0.15f, 0.80f)] public float socketSpacing = 0.40f;

        [Header("Colours")]
        public Color barTint    = new(0.78f, 0.45f, 0.20f); // copper bar
        public Color socketTint = new(0.95f, 0.80f, 0.35f); // brass sockets

        // ── Lifecycle ───────────────────────────────────────────
        protected override void OnEnable()
        {
            // Busbars don't enforce the strict 1-grid-step cardinal rule that
            // cables do — players often place a bar 2-3 units away from a
            // nearby machine and expect the cable to still reach. We widen
            // connectRadius so the existing PowerCable.CanLinkTo path can
            // still gate via PortConfig, but distance is generous.
            requireGridAlignedNeighbours = false;
            connectRadius = Mathf.Max(connectRadius, busLength * 0.75f + 1.5f);

            base.OnEnable();

            // Disable the cable-style "core + arms" visual that PowerCable.OnEnable
            // built for us — the busbar replaces it with its own richer geometry.
            var cableVis = transform.Find("CableVisuals");
            if (cableVis != null) cableVis.gameObject.SetActive(false);

            RebuildBusbarVisuals();

            // ── Anti-overlap collider ────────────────────────────────
            // The placement system probes a small box around the click point
            // for existing PlacedBlock colliders. Without resizing our own
            // collider to match `busLength`, the player could insert another
            // busbar partway along this one's body (the centre cells outside
            // the prefab's default 1m cube collider had no overlap).
            // Stretching the collider along busAxis blocks every cell the bar
            // physically occupies, so end-to-end is fine but mid-body insert
            // is correctly rejected.
            ResizeColliderToBusLength();
        }

        /// <summary>
        /// Stretch the prefab's collider (added by BuildSystem at place time)
        /// to cover the full length of the bar. Idempotent — safe to call
        /// repeatedly and on re-enable.
        /// </summary>
        private void ResizeColliderToBusLength()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null) box = gameObject.AddComponent<BoxCollider>();

            // Cross-section a hair smaller than 1m so two busbars stacked on
            // adjacent rows don't accidentally veto each other.
            float cross = 0.55f;
            // Length is busLength minus a tiny margin so end-to-end placement
            // (one bar's tip touching the next bar's tip) still passes the
            // 0.42m overlap probe used by IsPlacementValid.
            float along = Mathf.Max(0.5f, busLength - 0.10f);

            switch (busAxis)
            {
                case BusAxis.X: box.size = new Vector3(along, cross, cross); break;
                case BusAxis.Y: box.size = new Vector3(cross, along, cross); break;
                default:        box.size = new Vector3(cross, cross, along); break;
            }
            box.center = Vector3.zero;
        }

        // The base class fires onNeighboursChanged → its own RebuildVisuals.
        // We piggyback on the same hook so the bar always stays in sync.
        private void Update()
        {
            // Bar geometry is static unless the inspector values change in
            // play mode. We rebuild once at enable; mining/breaking the bar
            // destroys the prefab so we never need to live-update.
        }

        private void RebuildBusbarVisuals()
        {
            var holder = transform.Find("BusbarVisuals");
            if (holder == null)
            {
                holder = new GameObject("BusbarVisuals").transform;
                holder.SetParent(transform, worldPositionStays: false);
            }
            else
            {
                for (int i = holder.childCount - 1; i >= 0; i--)
                    Destroy(holder.GetChild(i).gameObject);
            }

            Vector3 axisDir = busAxis switch
            {
                BusAxis.X => Vector3.right,
                BusAxis.Y => Vector3.up,
                _         => Vector3.forward,
            };

            var barMat    = VoxelEngine.Networks.IndustrialPipeMesh
                              .CreateMetalMaterial(barTint, $"{name}_BusBar");
            var socketMat = VoxelEngine.Networks.IndustrialPipeMesh
                              .CreateMetalMaterial(socketTint, $"{name}_BusSocket", 0.95f, 0.88f);

            // Main bar — one long cylinder centred on the prefab origin.
            BuildCylinder(holder, "Bar", Vector3.zero, axisDir,
                          busLength, busRadius, barMat);

            // Tap sockets along the bar.
            int taps = Mathf.Max(1, Mathf.FloorToInt(busLength / Mathf.Max(0.05f, socketSpacing)));
            float half = busLength * 0.5f;
            for (int i = 0; i < taps; i++)
            {
                // Distribute taps symmetrically around the centre.
                float t = (i + 0.5f) / taps;          // 0..1
                float along = -half + busLength * t;  // -half..+half
                Vector3 pos = axisDir * along;

                // Socket ring (short fat cylinder centred on the bar).
                BuildCylinder(holder, $"Socket_{i}", pos, axisDir,
                              0.08f, socketRadius, socketMat);

                // Two tiny rivet domes either side of each socket for detail.
                Vector3 perp = (busAxis == BusAxis.X) ? Vector3.up
                            : (busAxis == BusAxis.Y) ? Vector3.right
                            :                          Vector3.up;
                BuildSphere(holder, $"Rivet_{i}_A", pos + perp * socketRadius * 0.55f,
                            Vector3.one * 0.05f, socketMat);
                BuildSphere(holder, $"Rivet_{i}_B", pos - perp * socketRadius * 0.55f,
                            Vector3.one * 0.05f, socketMat);
            }

            // End caps so the bar reads as a finished object, not a stub.
            float endOffset = busLength * 0.5f + 0.04f;
            BuildCylinder(holder, "EndA", axisDir *  endOffset, axisDir,
                          0.08f, busRadius * 1.15f, socketMat);
            BuildCylinder(holder, "EndB", axisDir * -endOffset, axisDir,
                          0.08f, busRadius * 1.15f, socketMat);
        }

        // Local primitive helpers — small wrappers around Unity primitives that
        // strip the collider, parent under the visual root, and apply scale.
        private static void BuildCylinder(Transform parent, string name, Vector3 centre,
                                          Vector3 axis, float length, float radius, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = centre;
            t.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            t.localScale    = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }
        private static void BuildSphere(Transform parent, string name, Vector3 centre,
                                        Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var t = go.transform;
            t.SetParent(parent, worldPositionStays: false);
            t.localPosition = centre;
            t.localScale    = size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null && mat != null) mr.sharedMaterial = mat;
        }
    }
}
