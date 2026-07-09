// Assets/Scripts/VoxelEngine/Simulation/HighVoltagePole.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — HIGH VOLTAGE TRANSMISSION TOWER             ║
// ║  Realistic steel lattice tower for HV power lines. Tall,       ║
// ║  angular metal framework with cross-arms and insulator strings. ║
// ║  Carries unlimited power over very long distances.              ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// Tall steel lattice transmission tower for the high-voltage grid.
    /// Procedurally generates a realistic tower shape: tapered legs,
    /// cross-bracing, multiple cross-arms at different heights, and
    /// ceramic insulator strings hanging from each arm.
    ///
    /// Designed to be visually distinct from the low-voltage wooden/steel
    /// PowerPole — taller (12m), wider stance, lattice framework.
    /// </summary>
    public class HighVoltagePole : MonoBehaviour
    {
        [Header("Tower Configuration")]
        [Tooltip("Total height of the tower in meters.")]
        public float towerHeight = 12f;

        [Tooltip("Width of the tower base (leg spread).")]
        public float baseWidth = 3f;

        [Tooltip("Width of the tower top (narrower than base).")]
        public float topWidth = 1.2f;

        [Tooltip("Number of cross-arm levels (each carries 3 phase lines).")]
        public int crossArmLevels = 2;

        [Tooltip("Length of each cross-arm from centre.")]
        public float crossArmLength = 3.5f;

        [Header("Connections")]
        [Tooltip("Maximum HV wire connections this tower supports.")]
        public int maxConnections = 4;

        [Tooltip("Maximum distance an HV wire can span between towers.")]
        public float wireReach = 200f;

        // ── Runtime ───────────────────────────────────────────────────

        private readonly List<HVPoleConnection> _connections = new(4);
        private LineRenderer[] _wireRenderers;

        /// <summary>Read-only view of active HV wire connections.</summary>
        public IReadOnlyList<HVPoleConnection> Connections => _connections;
        public int AvailableSlots => maxConnections - _connections.Count;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            BuildTowerVisuals();
            _wireRenderers = new LineRenderer[maxConnections];
        }

        // ── Connection API ────────────────────────────────────────────

        public bool TryConnect(HighVoltagePole target)
        {
            if (target == null || target == this) return false;
            if (AvailableSlots <= 0 || target.AvailableSlots <= 0) return false;

            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > wireReach) return false;

            foreach (var c in _connections)
                if (c.target == target) return false;

            _connections.Add(new HVPoleConnection { target = target, distance = dist, isActive = true });
            target._connections.Add(new HVPoleConnection { target = this, distance = dist, isActive = true });

            UpdateWireVisuals();
            target.UpdateWireVisuals();
            return true;
        }

        public void Disconnect(HighVoltagePole target)
        {
            _connections.RemoveAll(c => c.target == target);
            if (target != null)
            {
                target._connections.RemoveAll(c => c.target == this);
                target.UpdateWireVisuals();
            }
            UpdateWireVisuals();
        }

        public void DisconnectAll()
        {
            foreach (var c in _connections)
            {
                if (c.target != null)
                    c.target._connections.RemoveAll(x => x.target == this);
            }
            _connections.Clear();
            UpdateWireVisuals();
        }

        // ── Tower Visuals ─────────────────────────────────────────────

        private static Material _steelMat;
        private static Material _insulatorMat;
        private static Material _wireMat;

        private void BuildTowerVisuals()
        {
            EnsureMaterials();
            float h = towerHeight;
            float bw = baseWidth * 0.5f;
            float tw = topWidth * 0.5f;

            // ── Four tapered legs ─────────────────────────────────────
            Vector3[] baseCorners = new[]
            {
                new Vector3(-bw, 0, -bw),
                new Vector3( bw, 0, -bw),
                new Vector3( bw, 0,  bw),
                new Vector3(-bw, 0,  bw)
            };
            Vector3[] topCorners = new[]
            {
                new Vector3(-tw, h, -tw),
                new Vector3( tw, h, -tw),
                new Vector3( tw, h,  tw),
                new Vector3(-tw, h,  tw)
            };

            for (int i = 0; i < 4; i++)
                CreateBeam(baseCorners[i], topCorners[i], 0.10f);

            // ── Cross-bracing (X pattern on each face) ────────────────
            int braceCount = Mathf.Max(2, (int)(h / 3f));
            for (int b = 0; b < braceCount; b++)
            {
                float t0 = (float)b / braceCount;
                float t1 = (float)(b + 1) / braceCount;

                for (int face = 0; face < 4; face++)
                {
                    int next = (face + 1) % 4;
                    Vector3 bl = Vector3.Lerp(baseCorners[face], topCorners[face], t0);
                    Vector3 br = Vector3.Lerp(baseCorners[next], topCorners[next], t0);
                    Vector3 tl = Vector3.Lerp(baseCorners[face], topCorners[face], t1);
                    Vector3 tr = Vector3.Lerp(baseCorners[next], topCorners[next], t1);

                    // X-brace
                    CreateBeam(bl, tr, 0.04f);
                    CreateBeam(br, tl, 0.04f);

                    // Horizontal tie
                    CreateBeam(tl, tr, 0.05f);
                }
            }

            // ── Cross-arms at top ─────────────────────────────────────
            for (int arm = 0; arm < crossArmLevels; arm++)
            {
                float armY = h - 0.5f - arm * 1.8f;

                // Main arm beam (left to right).
                CreateBeam(
                    new Vector3(-crossArmLength, armY, 0),
                    new Vector3( crossArmLength, armY, 0), 0.08f);

                // Vertical support from arm to tower body.
                float towerWidthAtArm = Mathf.Lerp(bw, tw, armY / h);
                CreateBeam(
                    new Vector3(-crossArmLength, armY, 0),
                    new Vector3(-towerWidthAtArm, armY, 0), 0.06f);
                CreateBeam(
                    new Vector3( crossArmLength, armY, 0),
                    new Vector3( towerWidthAtArm, armY, 0), 0.06f);

                // Diagonal brace under each arm.
                CreateBeam(
                    new Vector3(-crossArmLength, armY, 0),
                    new Vector3(-towerWidthAtArm, armY - 1.0f, 0), 0.04f);
                CreateBeam(
                    new Vector3( crossArmLength, armY, 0),
                    new Vector3( towerWidthAtArm, armY - 1.0f, 0), 0.04f);

                // Insulator strings (3 per arm — one at each end + centre).
                float[] insulatorX = { -crossArmLength + 0.3f, 0f, crossArmLength - 0.3f };
                foreach (var ix in insulatorX)
                    CreateInsulatorString(new Vector3(ix, armY, 0), 0.8f);
            }

            // ── Peak / lightning rod ──────────────────────────────────
            CreateBeam(
                new Vector3(0, h, 0),
                new Vector3(0, h + 1.5f, 0), 0.04f);

            // Small sphere at peak.
            var peak = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            peak.name = "LightningRod";
            peak.transform.SetParent(transform, false);
            peak.transform.localPosition = new Vector3(0, h + 1.5f, 0);
            peak.transform.localScale = Vector3.one * 0.12f;
            Destroy(peak.GetComponent<Collider>());
            peak.GetComponent<MeshRenderer>().material = _steelMat;
        }

        private void CreateBeam(Vector3 localA, Vector3 localB, float thickness)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Beam";
            go.transform.SetParent(transform, false);

            Vector3 mid = (localA + localB) * 0.5f;
            float length = Vector3.Distance(localA, localB);
            Vector3 dir = (localB - localA).normalized;

            go.transform.localPosition = mid;
            go.transform.localScale = new Vector3(thickness, thickness, length);
            go.transform.localRotation = Quaternion.LookRotation(dir);

            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().material = _steelMat;
        }

        private void CreateInsulatorString(Vector3 localTop, float length)
        {
            // Ceramic insulator discs stacked vertically.
            int discs = Mathf.Max(2, (int)(length / 0.15f));
            for (int d = 0; d < discs; d++)
            {
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "Insulator";
                disc.transform.SetParent(transform, false);
                disc.transform.localPosition = localTop + Vector3.down * (d * 0.15f + 0.1f);
                disc.transform.localScale = new Vector3(0.12f, 0.03f, 0.12f);
                Destroy(disc.GetComponent<Collider>());
                disc.GetComponent<MeshRenderer>().material = _insulatorMat;
            }
        }

        // ── Wire Visuals ──────────────────────────────────────────────

        private void UpdateWireVisuals()
        {
            for (int i = 0; i < maxConnections; i++)
            {
                if (i < _connections.Count && _connections[i].target != null)
                {
                    if (_wireRenderers[i] == null)
                    {
                        var wireGo = new GameObject($"HVWire_{i}");
                        wireGo.transform.SetParent(transform, false);
                        _wireRenderers[i] = wireGo.AddComponent<LineRenderer>();
                        _wireRenderers[i].positionCount = 20;
                        _wireRenderers[i].startWidth = 0.05f;
                        _wireRenderers[i].endWidth = 0.05f;
                        _wireRenderers[i].useWorldSpace = true;
                        _wireRenderers[i].material = _wireMat;
                    }

                    Vector3 a = transform.position + Vector3.up * (towerHeight - 1f);
                    Vector3 b = _connections[i].target.transform.position + Vector3.up * (_connections[i].target.towerHeight - 1f);
                    DrawCatenary(_wireRenderers[i], a, b, _connections[i].distance);
                    _wireRenderers[i].gameObject.SetActive(true);
                }
                else if (_wireRenderers[i] != null)
                {
                    _wireRenderers[i].gameObject.SetActive(false);
                }
            }
        }

        private static void DrawCatenary(LineRenderer lr, Vector3 a, Vector3 b, float dist)
        {
            int segs = lr.positionCount;
            float sag = dist * 0.04f;
            for (int i = 0; i < segs; i++)
            {
                float t = (float)i / (segs - 1);
                Vector3 pos = Vector3.Lerp(a, b, t);
                pos.y -= sag * 4f * t * (1f - t);
                lr.SetPosition(i, pos);
            }
        }

        private void OnDestroy() => DisconnectAll();

        // ── Shared Materials ──────────────────────────────────────────

        private static void EnsureMaterials()
        {
            if (_steelMat != null) return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

            _steelMat = new Material(shader);
            _steelMat.color = new Color(0.52f, 0.54f, 0.56f);
            _steelMat.SetFloat("_Metallic", 0.8f);
            _steelMat.SetFloat("_Smoothness", 0.45f);

            _insulatorMat = new Material(shader);
            _insulatorMat.color = new Color(0.65f, 0.55f, 0.40f); // brown ceramic
            _insulatorMat.SetFloat("_Metallic", 0.05f);
            _insulatorMat.SetFloat("_Smoothness", 0.7f);

            _wireMat = new Material(shader);
            _wireMat.color = new Color(0.20f, 0.20f, 0.22f);
            _wireMat.SetFloat("_Metallic", 0.85f);
        }
    }

    [System.Serializable]
    public struct HVPoleConnection
    {
        public HighVoltagePole target;
        public float distance;
        public bool isActive;
    }
}
