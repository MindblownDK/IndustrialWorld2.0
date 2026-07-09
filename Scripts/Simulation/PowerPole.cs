// Assets/Scripts/VoxelEngine/Simulation/PowerPole.cs
//
// ╔══════════════════════════════════════════════════════════════════╗
// ║  INDUSTRIAL WORLD — POWER POLE & WIRE SYSTEM                    ║
// ║  Placeable pole that connects to machines via player-crafted     ║
// ║  Wire items. Supports up to 6 connections. Generators feed       ║
// ║  Cable Outputs; machines connect to Cable Inputs.               ║
// ╚══════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using UnityEngine;
using VoxelEngine.Power;

namespace VoxelEngine.Simulation
{
    /// <summary>
    /// A power pole that distributes electricity through wire connections.
    /// Players craft Wire items and run them between poles, generators,
    /// and machines. Each standard pole supports up to 6 connections.
    /// </summary>
    public class PowerPole : MonoBehaviour
    {
        [Header("Pole Configuration")]
        [Tooltip("Maximum number of wire connections this pole supports.")]
        public int maxConnections = 6;

        [Tooltip("Maximum distance a single wire can span from this pole.")]
        public float wireReach = 15f;

        [Header("Visual")]
        [Tooltip("Height of the pole mesh.")]
        public float poleHeight = 3f;

        // ── Runtime ───────────────────────────────────────────────────

        private readonly List<PowerPoleConnection> _connections = new(6);
        private PowerNode _powerNode;
        private LineRenderer[] _wireRenderers;

        /// <summary>Read-only view of active wire connections.</summary>
        public IReadOnlyList<PowerPoleConnection> Connections => _connections;

        /// <summary>How many connection slots are still available.</summary>
        public int AvailableSlots => maxConnections - _connections.Count;

        // ── Lifecycle ─────────────────────────────────────────────────

        private void Awake()
        {
            _powerNode = GetComponent<PowerNode>();
            if (_powerNode == null) _powerNode = gameObject.AddComponent<PowerCable>();

            // Configure the power cable node.
            var cable = _powerNode as PowerCable;
            if (cable != null)
            {
                cable.connectRadius = wireReach;
            }

            BuildPoleVisuals();
            _wireRenderers = new LineRenderer[maxConnections];
        }

        // ── Connection API ────────────────────────────────────────────

        /// <summary>
        /// Try to connect this pole to a target (another pole, a machine, or a generator).
        /// Returns true if the connection was established.
        /// </summary>
        public bool TryConnect(PowerPole target)
        {
            if (target == null || target == this) return false;
            if (AvailableSlots <= 0) return false;
            if (target.AvailableSlots <= 0) return false;

            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist > wireReach) return false;

            // Check for duplicate connection.
            foreach (var c in _connections)
                if (c.target == target) return false;

            var conn = new PowerPoleConnection
            {
                target = target,
                distance = dist,
                isActive = true
            };

            _connections.Add(conn);
            target._connections.Add(new PowerPoleConnection
            {
                target = this,
                distance = dist,
                isActive = true
            });

            UpdateWireVisuals();
            target.UpdateWireVisuals();
            return true;
        }

        /// <summary>
        /// Disconnect from a specific target.
        /// </summary>
        public void Disconnect(PowerPole target)
        {
            _connections.RemoveAll(c => c.target == target);
            target._connections.RemoveAll(c => c.target == this);
            UpdateWireVisuals();
            target.UpdateWireVisuals();
        }

        /// <summary>
        /// Disconnect all wires from this pole.
        /// </summary>
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

        // ── Visuals ───────────────────────────────────────────────────

        private void BuildPoleVisuals()
        {
            // Main pole shaft.
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "PoleShaft";
            shaft.transform.SetParent(transform, false);
            shaft.transform.localPosition = Vector3.up * (poleHeight * 0.5f);
            shaft.transform.localScale = new Vector3(0.12f, poleHeight * 0.5f, 0.12f);

            var col = shaft.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = shaft.GetComponent<MeshRenderer>();
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = new Color(0.45f, 0.40f, 0.35f); // weathered wood/steel
            mat.SetFloat("_Metallic", 0.3f);
            mat.SetFloat("_Smoothness", 0.3f);
            mr.material = mat;

            // Cross arm at top.
            var arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
            arm.name = "CrossArm";
            arm.transform.SetParent(transform, false);
            arm.transform.localPosition = Vector3.up * poleHeight;
            arm.transform.localScale = new Vector3(1.2f, 0.08f, 0.08f);

            var acol = arm.GetComponent<Collider>();
            if (acol != null) Destroy(acol);

            arm.GetComponent<MeshRenderer>().material = mat;

            // Connection point indicators (small glowing spheres).
            for (int i = 0; i < maxConnections; i++)
            {
                float angle = (360f / maxConnections) * i * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(
                    Mathf.Cos(angle) * 0.4f,
                    poleHeight + 0.1f,
                    Mathf.Sin(angle) * 0.4f
                );

                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"ConnPoint_{i}";
                dot.transform.SetParent(transform, false);
                dot.transform.localPosition = offset;
                dot.transform.localScale = Vector3.one * 0.08f;

                var dcol = dot.GetComponent<Collider>();
                if (dcol != null) Destroy(dcol);

                var dmr = dot.GetComponent<MeshRenderer>();
                var dmat = new Material(shader);
                dmat.color = new Color(0.22f, 0.78f, 0.42f); // green = available
                dmat.SetColor("_EmissionColor", new Color(0.22f, 0.78f, 0.42f) * 1.5f);
                dmat.EnableKeyword("_EMISSION");
                dmr.material = dmat;
            }
        }

        private void UpdateWireVisuals()
        {
            for (int i = 0; i < maxConnections; i++)
            {
                if (i < _connections.Count && _connections[i].target != null)
                {
                    if (_wireRenderers[i] == null)
                    {
                        var wireGo = new GameObject($"Wire_{i}");
                        wireGo.transform.SetParent(transform, false);
                        _wireRenderers[i] = wireGo.AddComponent<LineRenderer>();
                        _wireRenderers[i].positionCount = 12;
                        _wireRenderers[i].startWidth = 0.03f;
                        _wireRenderers[i].endWidth = 0.03f;
                        _wireRenderers[i].useWorldSpace = true;

                        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                        var mat = new Material(shader);
                        mat.color = new Color(0.15f, 0.15f, 0.15f); // dark wire
                        mat.SetFloat("_Metallic", 0.7f);
                        _wireRenderers[i].material = mat;
                    }

                    // Draw catenary curve between the two poles.
                    DrawCatenary(_wireRenderers[i], transform.position + Vector3.up * poleHeight,
                                 _connections[i].target.transform.position + Vector3.up * poleHeight);
                    _wireRenderers[i].gameObject.SetActive(true);
                }
                else if (_wireRenderers[i] != null)
                {
                    _wireRenderers[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Draw a catenary (hanging wire) curve between two points.
        /// </summary>
        private static void DrawCatenary(LineRenderer lr, Vector3 a, Vector3 b)
        {
            int segments = lr.positionCount;
            float dist = Vector3.Distance(a, b);
            float sag = dist * 0.08f; // 8% sag

            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / (segments - 1);
                Vector3 pos = Vector3.Lerp(a, b, t);
                // Catenary sag: y = -sag * 4 * t * (1 - t)
                pos.y -= sag * 4f * t * (1f - t);
                lr.SetPosition(i, pos);
            }
        }

        private void OnDestroy()
        {
            DisconnectAll();
        }
    }

    /// <summary>
    /// Data for a single wire connection from one pole to another.
    /// </summary>
    [System.Serializable]
    public struct PowerPoleConnection
    {
        public PowerPole target;
        public float distance;
        public bool isActive;
    }
}
